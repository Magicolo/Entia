using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Entia.Core;

namespace Entia.Experiment.V4
{
    /*
        In V4, we remove the ability for entities to change their structure dynamically post-creation.
        - This means that the complete structure of the entity must be determined at creation time through a template.
        - This means that the segment of an entity can be known at creation time and be valid for its whole life.
        - This solves concurrency issues of moving entities between segments.
        - 'Add<T>/Remove<T>' no longer exist.
        - 'Set<T>' can be removed since components should be modified through a 'ref T Get<T>'.
        - This also ensures that the API is more coherent; 'Has<T>' will always agree with 'Get<T>' and 'Group<T>'.
        - This allows to validate the requirements of an entity once and be guarantee that those requirements hold
        as long as the entity is alive which reduces the amount of checks required and allows to declare
        requirements statically. See 'Entity<T>'.
        - The case for memory consumption is harder to determine. On one hand, entities will require more memory
        since they will need to hold additional state to enable/disable some behavior which could've been accomplished
        through the addition/removal of components. On the other hand, static entities will require much less segments.
        The balance likely favors static entities, but this remains to be shown.
        - Segments iteration will be lock free ('Destroy' might be an issue).
        - This reduces the power of queries, but may be a tolerable trade-off.
        - Since segments will be known at system schedule time, dependencies can be expressed as 'Read/Write' with
        a set of segments. This means that exclusive queries can be run in parallel even if they write to the same
        component type. In principle, new segments could be created from outside the execution loop, which would
        require a rescheduling of systems.

        There could be a few variants for the 'Defer' module:
        - 'Defer.Next': defers operations until the next synchronization point.
        - 'Defer.Previous': defers operations until the previous synchronization point of the next frame.
        - 'Defer.End': defers operations until the end of the frame.

        Since it can not be safely predetermined what segments a 'Destroy' operation will affect, it can be treated
        in 2 ways:
        - Give access the instant 'Destroy' operation which will force a synchronization point.
        - Give access to a deferred 'Destroy' operation through variants of the 'Defer' module.

        Resources should be implemented as an entity:
        - Each resource would be associated with an entity that would be stored in a segment of size 1.
        - There must be 1 entity per resource type since dependencies are expressed in terms of segments
        and to prevent improper detection of dependency conflicts, they must be separated.
        - Resource entities should be excluded from queries.

        Messages should be implemented as temporary entities:
        - 'Emitter<T>' will create a new entity with a single 'T' message component on emit which will be destroyed
        before the next execution of the emitting system.
        - 'Receiver<T>' will query entities with the 'T' message component and enqueue the component in a queue.
        - 'Emitter<T>' will have a 'Write' dependency on the message segment.
        - 'Receiver<T>' will have a 'Read' dependency on the message segment.
        - Therefore, messages can not be emitted and received concurrently which is desired since even if it was
        thread-safe, the order would not be deterministic; a property that Entia tries to preserve.
        - Message entities should be excluded from queries.
        - The implementation will use a 'Template<T>' to emit the message. This will force the
        initialization of the segment before execution.
            - It will destroy the entities using a query of 'T' and 'Defer.Previous'. Since the
            creation/destruction segment is known, the dependency is just a 'Write<T>' rather than
            an 'Unknown'.
            - It will use a query of 'T' that does not exclude messages to retrieve them. This only
            imposes a 'Read<T>' dependency that will include only 1 segment.
        - For systems that consume all messages on each execution, the queue in not needed, the
        messages can read directly from the message segment store.
    */

    public static class Test
    {
        public struct OnInitialize { }
        public struct OnFinalize { }

        public struct Position { public Vector3 Value; }
        public struct Velocity { public Vector3 Value; }
        public struct Time { public float Total, Delta; }

        public static void Do()
        {
            var template = Template.Create<Vector3>()
                .Add(state => new Position { Value = state })
                .Add(new Velocity { Value = new(1f, 2f, 3f) });

            var world = new World();
            var populate = Node.All(
                Node.Create(Template.Create().Add(new OnInitialize()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(Template.Create().Add(new OnFinalize()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(Template.Create().Add(new Position()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(Template.Create().Add(new Velocity()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(Template.Create().Add(new Time()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(template.Add(new Time()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(template.Add(new OnInitialize()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(template.Add(new OnFinalize()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(template, creator => Node.Run(() => creator.Create(new(1f, 2f, 3f))))
            ).Schedule(world);

            var sum = 0;
            void Sum(int count = 1000) { for (int i = 0; i < count; i++) sum += i; }

            var increment = Node.All(
                Node.Run((Entity entity, ref OnInitialize _) => Sum()),
                Node.Run((Entity entity, ref Time _) => Sum()),
                Node.Run((Entity entity, ref Position position) => position.Value.X++),
                Node.Run((Entity entity, ref OnFinalize _) => Sum()),
                Node.Run((Entity entity, ref Velocity velocity) => velocity.Value.X++)
            // Node.Run((ref Position position, in Velocity velocity) => position.Value += velocity.Value)
            // Run((in Time time, ref Position position, in Velocity velocity) =>
            //     position.Value += velocity.Value * time.Delta)
            ).Schedule(world);

            var entities = new Entities(world);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 2500; i++)
            {
                if ((i % 100) == 0)
                {
                    Console.WriteLine($"Iteration {i}: {watch.Elapsed} | {world.Count}/{world.Capacity}");
                    watch.Restart();
                }
                populate();
                increment();
            }
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public readonly struct Entity : IEquatable<Entity>, IComparable<Entity>, Queryables.IQueryable
    {
        public static readonly Entity Zero;

        public static bool operator ==(Entity left, Entity right) => left.Identifier == right.Identifier;
        public static bool operator !=(Entity left, Entity right) => left.Identifier != right.Identifier;

        [FieldOffset(0)] public readonly long Identifier;
        [FieldOffset(0)] public readonly int Index;
        [FieldOffset(4)] public readonly uint Generation;

        public Entity(int index, uint generation)
        {
            Identifier = default;
            Index = index;
            Generation = generation;
        }

        public int CompareTo(Entity other) => Identifier.CompareTo(other.Identifier);
        public bool Equals(Entity other) => this == other;
        public override bool Equals(object obj) => obj is Entity entity && this == entity;
        public override int GetHashCode() => Identifier.GetHashCode();
        public override string ToString() => $"{{ Index: {Index}, Generation: {Generation} }}";
    }

    // The only way to obtain an 'Entity<T>' where 'T' represents some requirements is through the 'Components'
    // module which has to validate that an 'Entity' does satisfy 'T'. If entities cannot change their structure
    // post-creation, the 'Entity' will always satisfy 'T' as long as it lives so the only check that remains
    // is the check for life since the checks for component existence are unnecessary.
    public readonly struct Entity<TQuery>
    {
        public static implicit operator Entity(Entity<TQuery> entity) => entity._entity;

        readonly Entity _entity;
        public Entity(Entity entity) { _entity = entity; }
    }

    public struct Or<T1, T2> { }
    public struct And<T1, T2> { }
    public struct Not<T> { }
    public struct Has<T> { }

    public struct Target
    {
        // Express requirements explicitly.
        public Entity<(Targetable, Not<IsInvincible>)> Entity;
    }

    public sealed class World
    {
        public struct Datum
        {
            public uint Generation;
            public byte Index;
            public Segment.Chunk Chunk;
            public Segment Segment;
        }

        public int Capacity => Data.Length * _size;
        public int Count => _last - _free.Count;
        internal Segment[] Segments = { };
        internal Datum[][] Data = { };

        readonly int _shift;
        readonly int _size;
        readonly int _mask;
        readonly ConcurrentBag<int> _free = new();
        readonly object _lock = new();
        Dictionary<Type, Meta> _metas = new();
        int _last;

        public World(byte grow = 6)
        {
            _shift = grow;
            _size = 1 << _shift;
            _mask = _size - 1;
            // Skip 'Entity(0, 0)' to prevent bugs when mistakenly using 'default(Entity)' or 'Entity.Zero'.
            DataAt(0, 1)[0].Generation++;
        }

        public Segment Segment(Meta[] metas, byte? chunk = default)
        {
            if (TrySegment(metas, out var segment)) return segment;
            lock (_lock)
            {
                if (TrySegment(metas, out segment)) return segment;
                segment = new((uint)Segments.Length, metas, chunk);
                Segments = Segments.Append(segment);
                return segment;
            }
        }

        public bool TrySegment(Meta[] metas, out Segment segment)
        {
            for (int i = 0; i < Segments.Length; i++)
            {
                segment = Segments[i];
                if (metas.All(segment, (meta, state) => state.Has(meta)) &&
                    segment.Metas.All(metas, (meta, state) => state.Contains(meta)))
                    return true;
            }
            segment = default;
            return false;
        }

        public bool TryDatum(Entity entity, out Datum datum)
        {
            if (entity.Index < Capacity)
            {
                datum = DatumAt(entity.Index);
                return datum.Generation == entity.Generation;
            }
            datum = default;
            return false;
        }

        public Meta Meta(Type type)
        {
            if (TryMeta(type, out var meta)) return meta;
            lock (_lock)
            {
                if (TryMeta(type, out meta)) return meta;
                meta = new(type, (uint)_metas.Count);
                _metas = new Dictionary<Type, Meta>(_metas) { { meta.Type, meta } };
                return meta;
            }
        }

        public bool TryMeta(Type type, out Meta meta) => _metas.TryGetValue(type, out meta);

        public Entity Create(Segment segment) => Create(segment, default(Unit), (int _, int _, Segment.Chunk _, in Unit _) => { });
        public Entity Create<TState>(Segment segment, in TState state, Initialize<TState> initialize)
        {
            Span<Entity> entities = stackalloc Entity[1];
            Create(entities, segment, state, initialize);
            return entities[0];
        }

        /* TODO:
            The way things are set up, the batch instantiation of 'Create' will only be used the first time that a chunk
            is filled and will always go through the '_free' bag after. To remedy this, we should try to do some
            defragmentation in a probably non thread-safe method.

            Something like:
                // Assumes that '_free' is some kind of concurrent set.
                while (_free.Remove(_last)) _last--;

            This implementation means that long living entities will limit batch instantiation. A more clever
            solution is needed.
            Perhaps use the same kind of chunk design as in 'Segment'? This would mean that the indices in '_free' do not represent
            a specific chunk item but would tag a chunk as 'not full'.
        */
        public void Create(Span<Entity> entities, Segment segment) => Create(entities, segment, default(Unit), (int _, int _, Segment.Chunk _, in Unit _) => { });
        public void Create<TState>(Span<Entity> entities, Segment segment, in TState state, Initialize<TState> initialize)
        {
            var created = 0;
            while (created < entities.Length)
            {
                Span<Datum> data;
                if (_free.TryTake(out var free)) data = DataAt(free, 1);
                else
                {
                    var count = entities.Length - created;
                    free = Interlocked.Add(ref _last, count) - count;
                    data = DataAt(free, count);
                    // If there was an overflow, add the missed indices to the '_free' bag. This can happen if the 'Data' chunk
                    // was too small to fit the whole 'count'.
                    for (int i = data.Length; i < count; i++) _free.Add(free + i);
                }

                var chunk = segment.Take(out var index);
                lock (chunk)
                {
                    var start = chunk.Count;
                    var count = Math.Min(Math.Min(chunk.Entities.Length - chunk.Count, entities.Length - created), data.Length);
                    for (int i = 0; i < count; i++, created++, chunk.Count++)
                    {
                        ref var datum = ref data[i];
                        datum.Segment = segment;
                        datum.Chunk = chunk;
                        datum.Index = chunk.Count;
                        var entity = entities[created] = new(free, datum.Generation);
                        chunk.Entities[chunk.Count] = entity;
                    }
                    if (count > 0) initialize(start, count, chunk, state);
                }
            }
        }

        public uint Destroy(Segment segment)
        {
            var destroyed = 0u;
            for (int i = 0; i < segment.Chunks.Length; i++)
            {
                var chunk = segment.Chunks[i];
                if (chunk.Count == 0) continue;

                for (int j = 0; j < chunk.Count; j++)
                {
                    var entity = chunk.Entities[j];
                    ref var datum = ref DatumAt(entity.Index);
                    if (entity.Generation == datum.Generation && Interlocked.Exchange(ref datum.Segment, null) == segment)
                    {
                        destroyed++;
                        datum.Generation++;
                        _free.Add(datum.Index);
                    }
                }
                foreach (var store in chunk.Stores) Array.Clear(store, 0, chunk.Count);
                chunk.Count = 0;
                segment.Put(i);
            }
            return destroyed;
        }

        public bool Destroy(Entity entity)
        {
            if (entity.Index >= Capacity) return false;
            ref var datum = ref DatumAt(entity.Index);
            if (entity.Generation == datum.Generation && Interlocked.Exchange(ref datum.Segment, null) is Segment segment)
            {
                datum.Generation++;
                var chunk = datum.Chunk;
                lock (chunk)
                {
                    var source = --chunk.Count;
                    var target = datum.Index;
                    if (source != target)
                    {
                        chunk.Entities[target] = chunk.Entities[source];
                        foreach (var store in chunk.Stores)
                        {
                            Array.Copy(store, source, store, target, 1);
                            Array.Clear(store, source, 1);
                        }
                        DatumAt(target).Index = datum.Index;
                    }
                }
                segment.Put(chunk.Index);
                _free.Add(entity.Index);
                return true;
            }
            return false;
        }

        ref Datum DatumAt(int index) => ref Data[index >> _shift][index & _mask];

        Span<Datum> DataAt(int index, int count)
        {
            var chunk = index >> _shift;
            var item = index & _mask;
            // Use a lock rather than 'CompareExchange' to prevent thread fighting.
            while (chunk >= Data.Length) lock (Data) Data = Data.Append(new Datum[_size]);
            return Data[chunk].AsSpan(item, count);
        }
    }

    public sealed record Meta : IComparable<Meta>
    {
        public readonly Type Type;
        public readonly uint Index;
        public Meta(Type type, uint index) { Type = type; Index = index; }

        public int CompareTo(Meta other)
        {
            var comparison = Type.MetadataToken.CompareTo(other.Type.MetadataToken);
            if (comparison == 0) comparison = Index.CompareTo(other.Index);
            return comparison;
        }
    }

    public static class Template
    {
        public static Template<Unit> Create() => Create<Unit>();
        public static Template<TState> Create<TState>() => new Template<TState>(Array.Empty<(Type, Template<TState>.Initialize)>());

        public static Template<TTarget> Adapt<TSource, TTarget>(this Template<TSource> template, Func<TTarget, TSource> adapt) =>
            new(template.Initializers.Select(pair => (pair.type,
                new Template<TTarget>.Initialize((int index, int count, Array store, in TTarget state) =>
                    pair.initialize(index, count, store, adapt(state))))));

        public static Template<TState> All<TState>(params Template<TState>[] templates) => templates.All();
        public static Template<TState> All<TState>(this IEnumerable<Template<TState>> templates) => new(templates
            .SelectMany(template => template.Initializers)
            .GroupBy(pair => pair.type)
            .Select(group => group.Last())
            .ToArray());

        public static Template<TState> Add<TState, TComponent>(this Template<TState> template, TComponent component) =>
            new(template.Remove<TComponent>().Initializers.Append((typeof(TComponent),
                new((int index, int count, Array store, in TState state) =>
                {
                    var casted = (TComponent[])store;
                    for (int i = 0; i < count; i++) casted[i + index] = component;
                }))));

        public static Template<TState> Add<TState, TComponent>(this Template<TState> template, Func<TState, TComponent> provide) =>
            new(template.Remove<TComponent>().Initializers.Append((typeof(TComponent),
                new((int index, int count, Array store, in TState state) =>
                {
                    var casted = (TComponent[])store;
                    for (int i = 0; i < count; i++) casted[i + index] = provide(state);
                }))));

        public static Template<TState> Remove<TState>(this Template<TState> template, params Type[] types) =>
            new(template.Initializers.Where(pair => !types.Contains(pair.type)).ToArray());
    }

    public readonly struct Template<TState>
    {
        public delegate void Initialize(int index, int count, Array store, in TState state);

        public readonly (Type type, Initialize initialize)[] Initializers;
        public Template(params (Type type, Initialize initialize)[] initializers) => Initializers = initializers;
        public Template<TState> Add<TComponent>() => this.Add(default(TComponent));
        public Template<TState> Remove<TComponent>() => this.Remove(typeof(TComponent));
    }

    public delegate void Initialize<T>(int index, int count, Segment.Chunk chunk, in T state);

    public sealed class Segment : IComparable<Segment>
    {
        public sealed class Chunk
        {
            // 'Entities' could technically be moved to the 'Segment' in 1 continuous array rather than being separated
            // chunks, but this setup will be practical to dispatch threads with only a chunk.
            public readonly Entity[] Entities;
            public readonly Array[] Stores;
            public readonly int Index;
            public byte Count;

            public Chunk(int index, Entity[] entities, Array[] stores)
            {
                Index = index;
                Entities = entities;
                Stores = stores;
            }
        }

        public readonly uint Index;
        public readonly Meta[] Metas;
        internal Chunk[] Chunks => _chunks;

        readonly int _size;
        readonly uint[] _indices;
        readonly ConcurrentBag<int> _free = new ConcurrentBag<int>();
        Chunk[] _chunks = { };

        public Segment(uint index, Meta[] metas, byte? size = default)
        {
            Index = index;
            Metas = metas;
            _size = size ?? 64;
            _indices = new uint[metas.Length == 0 ? 0 : metas.Max(meta => meta.Index + 1)];
            _indices.Fill(uint.MaxValue);
            for (var i = 0u; i < metas.Length; i++) _indices[metas[i].Index] = i;
        }

        public bool Has(Meta meta) => TryIndex(meta, out var index) && Metas[index] == meta;
        public bool TryIndex(Meta meta, out uint index) => _indices.TryAt(meta.Index, out index) && index < Metas.Length;

        public bool TryStore(Chunk chunk, Meta meta, out Array store)
        {
            if (TryIndex(meta, out var index))
            {
                store = chunk.Stores[index];
                return true;
            }
            store = default;
            return false;
        }

        public Chunk Take(out int index)
        {
            if (_free.TryTake(out index)) return _chunks[index];

            var chunks = _chunks;
            if (chunks.TryLast(out var chunk, out index) && chunk.Count < _size) return chunk;

            index = chunks.Length;
            chunk = new Chunk(index, new Entity[_size], Metas.Select(meta => Array.CreateInstance(meta.Type, _size)));
            // If the 'CompareExchange' fails, it means that another thread added a chunk before this one
            // finished. In this case, this thread's work will be discarded, which is fine.
            Interlocked.CompareExchange(ref _chunks, chunks.Append(chunk), chunks);
            // Read from 'Chunks' in case 'CompareExchange' fails.
            return _chunks[index];
        }

        public void Put(int index) => _free.Add(index);

        public int CompareTo(Segment other) => Index.CompareTo(other.Index);
    }

    public readonly struct Creator<TState>
    {
        static readonly Initialize<TState> _default = (int _, int _, Segment.Chunk _, in TState _) => { };

        internal Segment Segment => _segment;
        readonly World _world;
        readonly Segment _segment;
        readonly Initialize<TState> _initialize;

        public Creator(Template<TState> template, World world, byte? chunk = default)
        {
            var initializers = template.Initializers.Select(pair => (meta: world.Meta(pair.type), pair.initialize));
            var initialize = default(Initialize<TState>);
            var segment = world.Segment(initializers.Select(pair => pair.meta), chunk);
            foreach (var pair in initializers)
            {
                if (segment.TryIndex(pair.meta, out var store))
                {
                    initialize += (int index, int count, Segment.Chunk chunk, in TState state) =>
                        pair.initialize(index, count, chunk.Stores[store], state);
                }
            }

            _world = world;
            _segment = segment;
            _initialize = initialize ?? _default;
        }

        public Entity Create(in TState state) => _world.Create(_segment, state, _initialize);
        public void Create(Span<Entity> entities, in TState state) => _world.Create(entities, _segment, state, _initialize);
    }

    public static class Creator
    {
        public static Entity Create(this Creator<Unit> creator) => creator.Create(default);
    }

    public readonly struct Destroyer
    {
        internal Segment[] Segments => _segments();

        readonly World _world;
        readonly Func<Segment[]> _segments;

        public Destroyer(Matcher matcher, World world)
        {
            _world = world;
            _segments = world.Segments(matcher);
        }

        public bool Destroy(Entity entity) =>
            _world.TryDatum(entity, out var datum) &&
            Array.BinarySearch(_segments(), datum.Segment) >= 0 &&
            _world.Destroy(entity);

        public uint Destroy()
        {
            var count = 0u;
            foreach (var segment in _segments()) count += _world.Destroy(segment);
            return count;
        }
    }

    public readonly struct Entities : IEnumerable<Entity>
    {
        readonly World _world;

        public Entities(World world) { _world = world; }

        public bool Has(Entity entity) => _world.TryDatum(entity, out _);

        public IEnumerator<Entity> GetEnumerator() => _world.Data
            .Flatten()
            .Where(data => data.Segment is not null)
            .Select((data, index) => new Entity(index, data.Generation))
            .GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public readonly struct Resource<T>
    {
        static readonly Template<Unit> _template = Template.Create().Add(_ => DefaultUtility.Default<T>());

        public ref T Value => ref _store[0];
        readonly T[] _store;

        public Resource(World world)
        {
            var creator = new Creator<Unit>(_template, world, 1);
            if (creator.Segment.Chunks.Length == 0) creator.Create();
            _store = (T[])creator.Segment.Chunks[0].Stores[0];
        }
    }

    public readonly struct State<T>
    {
        static readonly Template<T> _template = Template.Create<T>().Add(state => state);

        public ref T Value => ref _store[_index];
        readonly T[] _store;
        readonly byte _index;

        public State(T value, World world, byte chunk = 1)
        {
            var creator = new Creator<T>(_template, world, chunk);
            var entity = creator.Create(value);
            world.TryDatum(entity, out var datum);
            _index = (byte)datum.Index;
            _store = (T[])datum.Chunk.Stores[0];
        }
    }

    struct Messages<T> { public ConcurrentQueue<T> Queue; public int Capacity; }

    public readonly struct Emitter<T>
    {
        readonly Segment _segment;
        public Emitter(World world) => _segment = world.Segment(new[] { world.Meta(typeof(Messages<T>)) }, 8);
        public void Emit(in T message)
        {
            foreach (var chunk in _segment.Chunks)
            {
                var store = (Messages<T>[])chunk.Stores[0];
                for (int i = 0; i < chunk.Count; i++)
                {
                    ref var messages = ref store[i];
                    if (messages.Capacity != 0) messages.Queue.Enqueue(message);
                    while (messages.Capacity >= 0 && messages.Queue.Count > messages.Capacity)
                        messages.Queue.TryDequeue(out _);
                }
            }
        }
    }

    public readonly struct Receiver<T>
    {
        readonly State<Messages<T>> _state;
        public Receiver(World world, int capacity = -1) => _state = new(new() { Queue = new(), Capacity = capacity }, world, 8);
        public bool TryReceive(out T message) => _state.Value.Queue.TryDequeue(out message);
    }

    namespace Nodes
    {
        public delegate Runner Prepare();
        public delegate Prepare Schedule(World world);

        public readonly struct Runner : IEquatable<Runner>
        {
            public static readonly Runner Empty = new Runner(Array.Empty<Action>(), Array.Empty<Dependency>());

            public static bool operator ==(Runner left, Runner right) => left.Equals(right);
            public static bool operator !=(Runner left, Runner right) => !(left == right);

            public static Runner All(params Runner[] runners) =>
                runners.Length == 0 ? Empty :
                runners.Length == 1 ? runners[0] :
                new(
                    runners.Select(runner => runner.Runs).Flatten(),
                    runners.Select(runner => runner.Dependencies).Flatten());

            public readonly Action[] Runs;
            public readonly Dependency[] Dependencies;

            public Runner(Action[] runs, Dependency[] dependencies)
            {
                Runs = runs;
                Dependencies = dependencies;
            }

            public Runner With(Action[] runs) => new Runner(runs, Dependencies);
            public Runner With(Dependency[] dependencies) => new Runner(Runs, dependencies);
            public bool Equals(Runner other) => Runs == other.Runs && Dependencies == other.Dependencies;
            public override bool Equals(object obj) => obj is Runner runner && Equals(runner);
            public override int GetHashCode() => HashCode.Combine(Runs, Dependencies);
            public void Deconstruct(out Action[] runs, out Dependency[] dependencies) => (runs, dependencies) = (Runs, Dependencies);
        }

        public interface INode { }

        readonly struct Lazy : INode
        {
            public readonly Func<World, INode> Provide;
            public Lazy(Func<World, INode> provide) { Provide = provide; }
        }

        readonly struct Mapper : INode
        {
            public readonly INode Node;
            public readonly Func<Runner, Runner> Map;
            public readonly Func<bool> Change;
            public Mapper(Nodes.INode node, Func<Runner, Runner> map, Func<bool> change) { Node = node; Map = map; Change = change; }
        }

        readonly struct Scheduler : INode
        {
            public readonly Schedule Schedule;
            public Scheduler(Schedule schedule) { Schedule = schedule; }
        }

        readonly struct All : INode
        {
            public readonly INode[] Nodes;
            public All(INode[] nodes) { Nodes = nodes; }
        }
    }

    public static class Node
    {
        public static readonly Nodes.INode Empty = Schedule(_ => () => Nodes.Runner.Empty);

        public static Nodes.INode Lazy(Func<World, Nodes.INode> provide) => new Nodes.Lazy(provide);
        public static Nodes.INode Schedule(Nodes.Schedule schedule) => new Nodes.Scheduler(schedule);
        public static Nodes.INode All(params Nodes.INode[] nodes) => new Nodes.All(nodes);
        public static Nodes.INode All(this IEnumerable<Nodes.INode> nodes) => All(nodes.ToArray());
        public static Nodes.INode Map(this Nodes.INode node, Func<Nodes.Runner, Nodes.Runner> map, Func<bool> change = null) => new Nodes.Mapper(node, map, change ?? new(() => false));
        public static Nodes.INode Depend(this Nodes.INode node, params Dependency[] dependencies) => node.Depend(() => dependencies);
        public static Nodes.INode Depend(this Nodes.INode node, Func<Dependency[]> provide, Func<bool> change = null) =>
            node.Map(runner => new(runner.Runs, runner.Dependencies.Append(provide())), change);
        public static Nodes.INode Synchronize() => Empty.Synchronous();
        public static Nodes.INode Synchronous(this Nodes.INode node) =>
            node.Map(runner => new(new[] { runner.Runs.Combine().Or(() => { }) }, runner.Dependencies.Append(Dependency.Unknown)));

        public delegate void RunR1EC1<TResource1, TComponent1>(ref TResource1 resource1, Entity entity, ref TComponent1 component1);
        public delegate void RunEC2<TComponent1, TComponent2>(ref TComponent1 component1, in TComponent2 component2);
        public delegate void RunEC1<TComponent1>(Entity entity, ref TComponent1 component1);

        public static Action Schedule(this Nodes.INode node, World world)
        {
            var prepares = Prepare(node, world);
            var runners = prepares.Select(prepare => prepare());
            var groups = Groups(runners);
            return () =>
            {
                var reschedule = false;
                for (int i = 0; i < groups.Length; i++)
                {
                    var (runner, begin, end) = groups[i];
                    var changed = (runs: false, dependencies: false);
                    for (int j = begin; j < end; j++)
                    {
                        ref var previous = ref runners[j];
                        var current = prepares[j]();
                        changed.runs |= previous.Runs != current.Runs;
                        changed.dependencies |= previous.Dependencies != current.Dependencies;
                        previous = current;
                    }

                    reschedule |= changed.dependencies;
                    // Since dependencies have changed, it is not safe to run the group runners in
                    // parallel until the reschedule happens.
                    if (changed.dependencies) for (int j = begin; j < end; j++) Run(runners[j]);
                    else if (changed.runs) Run(groups[i].runner = All(runners, begin, end));
                    else Run(runner);
                }
                if (reschedule) groups = Groups(runners);
            };

            static Nodes.Prepare[] Prepare(Nodes.INode node, World world)
            {
                return Interpret(Resolve(node));

                Nodes.INode Resolve(Nodes.INode node) => node switch
                {
                    Nodes.Lazy lazy => Resolve(lazy.Provide(world)),
                    Nodes.Mapper map => Resolve(map.Node) switch
                    {
                        Nodes.Mapper inner => inner.Node.Map(
                            runner => map.Map(inner.Map(runner)),
                            () => map.Change() || inner.Change()),
                        Nodes.INode outer => outer.Map(map.Map, map.Change)
                    },
                    Nodes.All all => all.Nodes
                        .Select(Resolve)
                        .Select(static node => node is Nodes.All inner ? inner.Nodes : new[] { node })
                        .Flatten()
                        .All(),
                    _ => node
                };

                Nodes.Prepare[] Interpret(Nodes.INode node) => node switch
                {
                    Nodes.Mapper map => Interpret(map.Node).Select(prepare => Map(prepare, map.Map, map.Change)),
                    Nodes.All all => all.Nodes.Select(Interpret).Flatten(),
                    Nodes.Scheduler system => new[] { system.Schedule(world) },
                    _ => Array.Empty<Nodes.Prepare>()
                };

                Nodes.Prepare Map(Nodes.Prepare prepare, Func<Nodes.Runner, Nodes.Runner> map, Func<bool> force)
                {
                    var runner = default(Nodes.Runner);
                    var cache = Nodes.Runner.Empty;
                    return () => runner.Change(prepare()) || force() ? cache = map(runner) : cache;
                }
            }

            static (Nodes.Runner runner, int begin, int end)[] Groups(Nodes.Runner[] runners)
            {
                var groups = new List<(Nodes.Runner runner, int, int)>();
                var conflicts = Conflicts(runners);
                var last = 0;
                // Groups must cover the full range of the runner array so empty groups must not
                // be filtered or this may cause problems when executing them.
                for (int i = 0; i < runners.Length; i++) if (conflicts[i]) Merge(last, last = i);
                Merge(last, runners.Length);
                return groups.ToArray();

                void Merge(int begin, int end) => groups.Add((All(runners, begin, end), begin, end));

                static bool[] Conflicts(Nodes.Runner[] runners)
                {
                    var dependencies = Array.Empty<Dependency>();
                    var conflicts = new bool[runners.Length];
                    for (int i = 0; i < runners.Length; i++)
                    {
                        var runner = runners[i];
                        if (conflicts[i] = Has(dependencies, runner.Dependencies))
                            dependencies = runner.Dependencies;
                        else
                            dependencies = dependencies.Append(runner.Dependencies);
                    }
                    return conflicts;
                }

                static bool Has(Dependency[] left, Dependency[] right)
                {
                    if (left.Length == 0 || right.Length == 0) return false;
                    return
                        left.Any(dependency => dependency.Kind == Dependency.Kinds.Unknown) ||
                        right.Any(dependency => dependency.Kind == Dependency.Kinds.Unknown) ||
                        left.Pairs(right)
                            .Where(pair => pair.Item1.Type == pair.Item2.Type && pair.Item1.Segment == pair.Item2.Segment)
                            .Any(pair => pair.Item1.Kind == Dependency.Kinds.Write || pair.Item2.Kind == Dependency.Kinds.Write);
                }
            }

            static Nodes.Runner All(Nodes.Runner[] runners, int begin, int end) =>
                Nodes.Runner.All(runners.Slice(begin, end - begin).ToArray());

            static void Run(Nodes.Runner runner)
            {
                if (runner.Runs.Length <= 8) foreach (var run in runner.Runs) run();
                else runner.Runs.Select(Task.Run).Iterate(task => task.Wait());
            }
        }

        public static Nodes.INode Create<T>(Template<T> template, Func<Creator<T>, Nodes.INode> provide) => Node.Lazy(world =>
        {
            var creator = new Creator<T>(template, world);
            var dependencies = creator.Segment.Metas
                .Select(meta => new Dependency(Dependency.Kinds.Write, meta.Type, creator.Segment))
                .Prepend(new Dependency(Dependency.Kinds.Write, typeof(Entity), creator.Segment));
            return provide(creator).Depend(dependencies);
        });

        public static Nodes.INode Destroy(Matcher matcher) => Node.Schedule(world =>
        {
            var provide = world.Segments(matcher);
            var segments = provide();
            var runner = Runner();
            return () => segments == (segments = provide()) ? runner : runner = Runner();

            Nodes.Runner Runner() => new(
                segments.Select(world, static (segment, world) => new Action(() => world.Destroy(segment))),
                segments.Select(static segment => new Dependency(Dependency.Kinds.Write, typeof(Entity), segment)));
        });

        public static Nodes.INode Destroy(Func<Destroyer, Nodes.INode> provide, Matcher? matcher = null) => Node.Lazy(world =>
        {
            var destroyer = new Destroyer(matcher ?? Matcher.True, world);
            var segments = destroyer.Segments;
            return provide(destroyer).Depend(() =>
                (segments = destroyer.Segments).Select(segment => new Dependency(Dependency.Kinds.Write, typeof(Entity), segment)),
                () => segments != destroyer.Segments);
        });

        public static Nodes.INode Run(params Action[] runs) => Node.Schedule(_ => () => new(runs, Array.Empty<Dependency>()));

        public static Nodes.INode Run<TComponent1>(RunEC1<TComponent1> run, Matcher? matcher = null) => Node.Schedule(world =>
        {
            var meta1 = world.Meta(typeof(TComponent1));
            var match = (matcher ?? Matcher.True).Match;
            var runner = Nodes.Runner.Empty;
            var segments = Array.Empty<(Segment segment, Action[] runs, uint store1)>();
            var index = 0u;
            return () =>
            {
                var changed = false;
                while (index < world.Segments.Length)
                {
                    var segment = world.Segments[index++];
                    if (segment.TryIndex(meta1, out var store1) && match(segment, world))
                    {
                        segments = segments.Append((segment, Array.Empty<Action>(), store1));
                        changed = true;
                    }
                }

                foreach (ref var pair in segments.AsSpan())
                {
                    if (pair.runs.Length == pair.segment.Chunks.Length) continue;

                    changed = true;
                    var start = pair.runs.Length;
                    // Ensures the 'runs' array is always of the proper size.
                    // If a segment is shrunk, excess runs will be thrown out.
                    Array.Resize(ref pair.runs, pair.segment.Chunks.Length);
                    for (int j = start; j < pair.runs.Length; j++)
                    {
                        var chunk = pair.segment.Chunks[j];
                        var entities = chunk.Entities;
                        var store1 = (TComponent1[])chunk.Stores[pair.store1];
                        pair.runs[j] = () => { for (int i = 0; i < chunk.Count; i++) run(entities[i], ref store1[i]); };
                    }
                }

                if (changed) runner = new(
                    segments.Select(pair => pair.runs).Flatten(),
                    segments.Select(pair => new[] { new Dependency(Dependency.Kinds.Read, typeof(Entity), pair.segment), new Dependency(Dependency.Kinds.Write, typeof(TComponent1), pair.segment) }).Flatten());

                return runner;
            };
        });

        internal static Func<Segment[]> Segments(this World world, Matcher matcher)
        {
            var index = 0u;
            var segments = Array.Empty<Segment>();
            return () =>
            {
                while (index < world.Segments.Length)
                {
                    var segment = world.Segments[index++];
                    if (matcher.Match(segment, world)) segments = segments.Append(segment);
                }
                return segments;
            };
        }
    }

    public readonly struct Dependency
    {
        public static readonly Dependency Unknown = new Dependency(Kinds.Unknown, default, default);

        public enum Kinds { Unknown, Read, Write }
        public readonly Kinds Kind;
        public readonly Type Type;
        public readonly Segment Segment;

        public Dependency(Kinds kind, Type type, Segment segment)
        {
            Kind = kind;
            Type = type;
            Segment = segment;
        }
    }

    // public readonly struct Plan
    // {
    //     public static readonly Plan Empty = new Plan(() => (Array.Empty<Action>(), Array.Empty<Dependency>()));

    //     public readonly Func<Action[]> Prepare;
    //     public Plan(Func<(Action[] runs, Dependency[] dependencies)> prepare)
    //     {
    //         Prepare = prepare;
    //     }
    //     public Plan With(Dependency[] dependencies) => new Plan(Prepare, dependencies);
    // }

    public readonly struct Matcher
    {
        public static readonly Matcher True = new((_, _) => true);
        public static readonly Matcher False = new((_, _) => false);

        public static implicit operator Matcher(Type type) => Has(type);

        public static Matcher Has(Type type) => new((segment, world) => world.TryMeta(type, out var meta) && segment.Has(meta));
        public static Matcher Has<T>() => Has(typeof(T));
        public static Matcher Not(Matcher matcher) => new((segment, world) => !matcher.Match(segment, world));

        public static Matcher All(params Matcher[] matchers) =>
            matchers.Length == 0 ? True :
            matchers.Length == 1 ? matchers[0] :
            new((segment, world) => matchers.All(matcher => matcher.Match(segment, world)));
        public static Matcher Any(params Matcher[] matchers) =>
            matchers.Length == 0 ? False :
            matchers.Length == 1 ? matchers[0] :
            new((segment, world) => matchers.Any(matcher => matcher.Match(segment, world)));
        public static Matcher None(params Matcher[] matchers) =>
            matchers.Length == 0 ? True :
            matchers.Length == 1 ? matchers[0] :
            new((segment, world) => matchers.None(matcher => matcher.Match(segment, world)));

        public readonly Func<Segment, World, bool> Match;
        public Matcher(Func<Segment, World, bool> match) { Match = match; }
    }

    public static class Defer
    {
        public readonly struct Next
        {
            public bool Destroy(Entity entity) => throw null;
        }

        public readonly struct Previous
        {
            public bool Destroy(Entity entity) => throw null;
        }

        public readonly struct End
        {
            public bool Destroy(Entity entity) => throw null;
        }
    }
}