using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        - All resources would be associated with a single special entity.
        - The resource entity should be excluded from queries.
        - The segment holding the entity should be sized for 1 entity.

        Messages should be implemented as temporary entities:
        - 'Emitter<T>' will create a new entity with a single 'T' message component on emit which will be destroyed
        before the next execution of the emitting system (using 'Defer.Previous').
        - 'Receiver<T>' will query entities with the 'T' message component and enqueue the component in a queue.
        - 'Emitter<T>' will have a 'Write' dependency on the message segment.
        - 'Receiver<T>' will have a 'Read' dependency on the message segment.
        - Therefore, messages can not be emitted and received concurrently which is desired since even if it was
        thread-safe, the order would not be deterministic; a property that Entia tries to preserve.
        - Message entities should be excluded from queries.
        - For systems that consume all messages on each execution, the queue in not needed, the messages can read
        directly from the message segment store.
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

            var system = Node.All(
                Node.Inject(template, factory => Node.Run(() => factory.Create(new(1f, 2f, 3f)))),
                Node.Run((Entity entity, ref Position position) => position.Value.X++)
            // Node.Run((ref Position position, in Velocity velocity) => position.Value += velocity.Value)
            // Run((in Time time, ref Position position, in Velocity velocity) =>
            //     position.Value += velocity.Value * time.Delta)
            );

            var world = new World();
            var entities = new Entities(world);
            var run = system.Schedule(world);
            for (int i = 0; i < 5; i++) run();
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
            public int Index;
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

        public World(int grow = 6)
        {
            _shift = grow;
            _size = 1 << _shift;
            _mask = _size - 1;
            // Skip 'Entity(0, 0)' to prevent bugs when mistakenly using 'default(Entity)' or 'Entity.Zero'.
            DataAt(0, 1)[0].Generation++;
        }

        public Segment Segment(Meta[] metas)
        {
            if (TrySegment(metas, out var segment)) return segment;
            lock (_lock)
            {
                if (TrySegment(metas, out segment)) return segment;
                segment = new(metas);
                Segments = Segments.Append(segment);
                return segment;
            }
        }

        public bool TrySegment(Meta[] metas, out Segment segment)
        {
            for (int i = 0; i < Segments.Length; i++)
            {
                segment = Segments[i];
                if (metas.All(segment, (meta, state) => state.Has(meta))) return true;
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
            if (_metas.TryGetValue(type, out var meta)) return meta;
            lock (_lock)
            {
                if (_metas.TryGetValue(type, out meta)) return meta;
                meta = new(type, (uint)_metas.Count);
                _metas = new Dictionary<Type, Meta>(_metas) { { meta.Type, meta } };
                return meta;
            }
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
                        datum.Index = (index << 24) | chunk.Count;
                        var entity = entities[created] = new(free, datum.Generation);
                        chunk.Entities[chunk.Count] = entity;
                    }
                    if (count > 0) initialize(start, count, chunk, state);
                }
            }
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
                    var index = datum.Index;
                    var source = --chunk.Count;
                    var target = (byte)datum.Index;
                    if (source != target)
                    {
                        chunk.Entities[target] = chunk.Entities[source];
                        foreach (var store in chunk.Stores)
                        {
                            Array.Copy(store, source, store, target, 1);
                            Array.Clear(store, source, 1);
                        }
                        DatumAt(target).Index = index;
                    }
                    segment.Put(index >> 24);
                }
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

    public record Meta
    {
        public readonly Type Type;
        public readonly uint Index;
        public Meta(Type type, uint index) { Type = type; Index = index; }
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

    public sealed class Segment
    {
        public sealed class Chunk
        {
            // 'Entities' could technically be moved to the 'Segment' in 1 continuous array rather than being separated
            // chunks, but this setup will be practical to dispatch threads with only a chunk.
            public readonly Entity[] Entities;
            public readonly Array[] Stores;
            // Threads that iterate on this count should take a local copy of it such that they do not inconsistently
            // iterate over entities created during the iteration. The inconsistency appears if other threads create
            // entities which may complete before or after this thread has completed its iteration.
            public byte Count;

            public Chunk(Entity[] entities, Array[] stores)
            {
                Entities = entities;
                Stores = stores;
            }
        }

        public readonly Meta[] Metas;
        internal Chunk[] Chunks = { };

        readonly int _size;
        readonly uint[] _indices;
        readonly ConcurrentBag<int> _free = new ConcurrentBag<int>();

        public Segment(Meta[] metas, byte size = 32)
        {
            Metas = metas;
            _size = size;
            _indices = new uint[metas.Length == 0 ? 0 : metas.Max(meta => meta.Index + 1)];
            _indices.Fill(uint.MaxValue);
            for (var i = 0u; i < metas.Length; i++) _indices[metas[i].Index] = i;
        }

        public bool Has(Meta meta) =>
            _indices.TryAt(meta.Index, out var index) &&
            Metas.TryAt(index, out var other) &&
            meta == other;

        public bool TryIndex(Meta meta, out uint index) => _indices.TryAt(meta.Index, out index);

        public bool TryStore(Chunk chunk, Meta meta, out Array store)
        {
            if (_indices.TryAt(meta.Index, out var index)) return chunk.Stores.TryAt(index, out store);
            store = default;
            return false;
        }

        public Chunk Take(out int index)
        {
            if (_free.TryTake(out index)) return Chunks[index];

            var chunks = Chunks;
            if (chunks.TryLast(out var chunk, out index) && chunk.Count < _size) return chunk;

            index = chunks.Length;
            chunk = new Chunk(new Entity[_size], Metas.Select(meta => Array.CreateInstance(meta.Type, _size)));
            // If the 'CompareExchange' fails, it means that another thread added a chunk before this one
            // finished. In this case, this thread's work will be discarded, which is fine.
            Interlocked.CompareExchange(ref Chunks, chunks.Append(chunk), chunks);
            // Read from 'Chunks' in case 'CompareExchange' fails.
            return Chunks[index];
        }

        public void Put(int index) => _free.Add(index);
    }

    public readonly struct Factory<TState>
    {
        static readonly Initialize<TState> _default = (int _, int _, Segment.Chunk _, in TState _) => { };

        internal readonly Segment Segment;
        readonly World _world;
        readonly Initialize<TState> _initialize;

        public Factory(Template<TState> template, World world)
        {
            var initializers = template.Initializers.Select(pair => (meta: world.Meta(pair.type), pair.initialize));
            var initialize = default(Initialize<TState>);
            var segment = world.Segment(initializers.Select(pair => pair.meta));
            foreach (var pair in initializers)
            {
                if (segment.TryIndex(pair.meta, out var store))
                {
                    initialize += (int index, int count, Segment.Chunk chunk, in TState state) =>
                        pair.initialize(index, count, chunk.Stores[store], state);
                }
            }

            Segment = segment;
            _world = world;
            _initialize = initialize ?? _default;
        }

        public Entity Create(in TState state)
        {
            Span<Entity> entities = stackalloc Entity[1];
            _world.Create(entities, Segment, state, _initialize);
            return entities[0];
        }

        public void Create(Span<Entity> entities, in TState state) => _world.Create(entities, Segment, state, _initialize);
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

    namespace Nodes
    {
        public delegate Runner Prepare();
        public delegate Prepare Schedule(World world);

        public readonly struct Runner : IEquatable<Runner>
        {
            public static readonly Runner Empty = new Runner(Array.Empty<Action>(), Array.Empty<Dependency>());

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

        readonly struct Map : INode
        {
            public readonly INode Node;
            public readonly Func<Runner, Runner> Mapping;
            public Map(Nodes.INode node, Func<Runner, Runner> map) { Node = node; Mapping = map; }
        }

        readonly struct System : INode
        {
            public readonly Schedule Schedule;
            public System(Schedule schedule) { Schedule = schedule; }
        }

        readonly struct All : INode
        {
            public readonly INode[] Nodes;
            public All(INode[] nodes) { Nodes = nodes; }
        }
    }

    public static class Node
    {
        public static Nodes.INode Lazy(Func<World, Nodes.INode> provide) => new Nodes.Lazy(provide);
        public static Nodes.INode System(Nodes.Schedule schedule) => new Nodes.System(schedule);
        public static Nodes.INode All(params Nodes.INode[] nodes) => new Nodes.All(nodes);
        public static Nodes.INode All(this IEnumerable<Nodes.INode> nodes) => All(nodes.ToArray());
        public static Nodes.INode Map(this Nodes.INode node, Func<Nodes.Runner, Nodes.Runner> map) => new Nodes.Map(node, map);
        public static Nodes.INode Depend(this Nodes.INode node, params Dependency[] dependencies) =>
            node.Map(runner => new(runner.Runs, runner.Dependencies.Append(dependencies)));

        public delegate void RunR1EC1<TResource1, TComponent1>(ref TResource1 resource1, Entity entity, ref TComponent1 component1);
        public delegate void RunEC2<TComponent1, TComponent2>(ref TComponent1 component1, in TComponent2 component2);
        public delegate void RunEC1<TComponent1>(Entity entity, ref TComponent1 component1);

        public static Action Schedule(this Nodes.INode node, World world)
        {
            static bool[] Conflicts(Nodes.Runner[] runners)
            {
                var dependencies = Array.Empty<Dependency>();
                var conflicts = new bool[runners.Length];
                for (int i = 0; i < runners.Length; i++)
                {
                    var runner = runners[i];
                    if (conflicts[i] = Has(dependencies, runner.Dependencies))
                        dependencies = Array.Empty<Dependency>();
                    else
                        dependencies = dependencies.Append(runner.Dependencies);
                }
                return conflicts;

                static bool Has(Dependency[] left, Dependency[] right)
                {
                    if (left.Length == 0 || right.Length == 0) return false;
                    return
                        left.Any(dependency => dependency.Kind == Dependency.Kinds.Unknown) ||
                        right.Any(dependency => dependency.Kind == Dependency.Kinds.Unknown) ||
                        left.Pairs(right)
                            .Where(pair => pair.Item1.Segment == pair.Item2.Segment)
                            .Any(pair => pair.Item1.Kind == Dependency.Kinds.Write || pair.Item2.Kind == Dependency.Kinds.Write);
                }
            }

            static Nodes.Runner[] Reduce(Nodes.Runner[] runners, bool[] conflicts)
            {
                var reduced = new List<Nodes.Runner>(runners.Length);
                var begin = 0;
                // Allow runners that have only dependencies to allow for synchronization runners.
                // Those synchronization runners will be filtered before returning such that they
                // are only considered when reducing and not while executing.
                for (int i = 0; i < runners.Length; i++) if (conflicts[i]) Merge(begin = i);
                Merge(runners.Length);
                return reduced.Where(runner => runner.Runs.Length > 0).ToArray();

                void Merge(int end)
                {
                    var count = end - begin;
                    if (count == 0) return;
                    reduced.Add(Nodes.Runner.All(runners.Slice(begin, count).ToArray()));
                }
            }

            static Nodes.Prepare Map(Nodes.Prepare prepare, Func<Nodes.Runner, Nodes.Runner> map)
            {
                var runner = default(Nodes.Runner);
                var cache = Nodes.Runner.Empty;
                return () => runner.Change(prepare()) ? cache = map(runner) : cache;
            }

            static Nodes.INode Resolve(Nodes.INode node, World world) => node switch
            {
                Nodes.Lazy lazy => Resolve(lazy.Provide(world), world),
                Nodes.Map map => Resolve(map.Node, world) switch
                {
                    Nodes.Map inner => inner.Node.Map(runner => map.Mapping(inner.Mapping(runner))),
                    Nodes.INode outer => outer.Map(map.Mapping)
                },
                Nodes.All all => all.Nodes
                    .Select(world, Resolve)
                    .Select(static node => node is Nodes.All inner ? inner.Nodes : new[] { node })
                    .Flatten()
                    .All(),
                _ => node
            };

            static Nodes.Prepare[] Schedule(Nodes.INode node, World world) => node switch
            {
                Nodes.Map map => Schedule(map.Node, world).Select(prepare => Map(prepare, map.Mapping)),
                Nodes.All all => all.Nodes.Select(world, Schedule).Flatten(),
                Nodes.System system => new[] { system.Schedule(world) },
                _ => Array.Empty<Nodes.Prepare>()
            };

            var resolved = Resolve(node, world);
            var prepares = Schedule(resolved, world);
            var runners = new Nodes.Runner[prepares.Length];
            var conflicts = new bool[prepares.Length];
            var reduced = Array.Empty<Nodes.Runner>();
            return () =>
            {
                var changed = (runs: false, dependencies: false);
                for (int i = 0; i < prepares.Length; i++)
                {
                    var runner = prepares[i]();
                    changed.runs |= runners[i].Runs != runner.Runs;
                    changed.dependencies |= runners[i].Dependencies != runner.Dependencies;
                    runners[i] = runner;
                }

                if (changed.dependencies) conflicts = Conflicts(runners);
                if (changed.runs || changed.dependencies) reduced = Reduce(runners, conflicts);

                foreach (var runner in reduced)
                {
                    if (runner.Runs.Length <= 4) foreach (var run in runner.Runs) run();
                    else Parallel.Invoke(runner.Runs);
                }
            };
        }

        public static Nodes.INode Inject<T>(Template<T> template, Func<Factory<T>, Nodes.INode> provide) => Node.Lazy(world =>
        {
            var factory = new Factory<T>(template, world);
            return Node.Depend(provide(factory), new Dependency(Dependency.Kinds.Write, factory.Segment));
        });

        public static Nodes.INode Run(params Action[] runs) =>
            Node.System(_ => () => new(runs, Array.Empty<Dependency>()));

        // public static Nodes.INode Run<TComponent1, TComponent2>(RunEC2<TComponent1, TComponent2> run, Matcher? matcher = null) => Node.System(world =>
        // {
        //     var index = 0;
        //     var segments = Array.Empty<(Segment segment, uint store1, uint store2)>();
        //     var meta1 = world.Meta(typeof(TComponent1));
        //     var meta2 = world.Meta(typeof(TComponent2));
        //     var runs = Array.Empty<Action>();
        //     var match = (matcher ?? Matcher.True).Match;
        //     return new(() =>
        //     {
        //         while (index < world.Segments.Length)
        //         {
        //             var segment = world.Segments[index++];
        //             if (segment.TryIndex(meta1, out var store1) && segment.TryIndex(meta2, out var store2) && match(segment, world))
        //                 segments = segments.Append((segment, store1, store2));
        //         }

        //         for (int i = 0; i < segments.Length; i++)
        //         {
        //             var pair = segments[i];
        //             if (runs.Length == pair.segment.Chunks.Length) continue;

        //             var start = runs.Length;
        //             // Ensures the 'runs' array is always of the proper size.
        //             // If a segment is shrunk, excess runs will be thrown out.
        //             Array.Resize(ref runs, pair.segment.Chunks.Length);
        //             for (int j = start; j < runs.Length; j++)
        //             {
        //                 var chunk = pair.segment.Chunks[j];
        //                 var store1 = (TComponent1[])chunk.Stores[pair.store1];
        //                 var store2 = (TComponent2[])chunk.Stores[pair.store2];
        //                 runs[j] = () => { for (int i = 0; i < chunk.Count; i++) run(ref store1[i], in store2[i]); };
        //             }
        //         }

        //         return runs;
        //     });
        // });

        public static Nodes.INode Run<TComponent1>(RunEC1<TComponent1> run, Matcher? matcher = null) => Node.System(world =>
        {
            var index = 0;
            var segments = Array.Empty<(Segment segment, Action[] runs, uint store1)>();
            var meta1 = world.Meta(typeof(TComponent1));
            var match = (matcher ?? Matcher.True).Match;
            var runner = Nodes.Runner.Empty;
            return new(() =>
            {
                var changed = false;
                while (index < world.Segments.Length)
                {
                    var segment = world.Segments[index++];
                    if (segment.TryIndex(meta1, out var store1) && match(segment, world))
                    {
                        changed = true;
                        segments = segments.Append((segment, Array.Empty<Action>(), store1));
                    }
                }

                for (int i = 0; i < segments.Length; i++)
                {
                    ref var segment = ref segments[i];
                    if (segment.runs.Length == segment.segment.Chunks.Length) continue;

                    changed = true;
                    var start = segment.runs.Length;
                    // Ensures the 'runs' array is always of the proper size.
                    // If a segment is shrunk, excess runs will be thrown out.
                    Array.Resize(ref segment.runs, segment.segment.Chunks.Length);
                    for (int j = start; j < segment.runs.Length; j++)
                    {
                        var chunk = segment.segment.Chunks[j];
                        var entities = chunk.Entities;
                        var store1 = (TComponent1[])chunk.Stores[segment.store1];
                        segment.runs[j] = () => { for (int i = 0; i < chunk.Count; i++) run(entities[i], ref store1[i]); };
                    }
                }

                if (changed) runner = new(
                    segments.Select(static segment => segment.runs).Flatten(),
                    segments.Select(static segment => new Dependency(Dependency.Kinds.Write, segment.segment)));
                return runner;
            });
        });
    }

    public readonly struct Dependency
    {
        public enum Kinds { Unknown, Read, Write }
        public readonly Kinds Kind;
        public readonly Segment Segment;

        public Dependency(Kinds kind, Segment segment)
        {
            Kind = kind;
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

        public static Matcher Has<T>() => new((segment, world) => segment.Has(world.Meta(typeof(T))));
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