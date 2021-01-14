using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
    */

    // public readonly struct Entity : IEquatable<Entity>
    // {
    //     public static readonly Entity Zero = default;

    //     public static bool operator ==(Entity left, long right) => left.Identifier == right;
    //     public static bool operator !=(Entity left, long right) => left.Identifier != right;
    //     public static bool operator ==(long left, Entity right) => left == right.Identifier;
    //     public static bool operator !=(long left, Entity right) => left != right.Identifier;
    //     public static bool operator ==(Entity left, Entity right) => left.Identifier == right.Identifier;
    //     public static bool operator !=(Entity left, Entity right) => left.Identifier != right.Identifier;

    //     public readonly long Identifier;
    //     public Entity(long identifier) => Identifier = identifier;

    //     public bool Equals(Entity other) => this == other;
    //     public override bool Equals(object obj) =>
    //         obj is Entity entity ? this == entity :
    //         obj is long identifier && this == identifier;
    //     public override int GetHashCode() => Identifier.GetHashCode();
    // }

    [StructLayout(LayoutKind.Explicit)]
    public readonly struct Entity : IEquatable<Entity>, IComparable<Entity>, Queryables.IQueryable
    {
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
    public readonly struct Entity<T>
    {
        public static implicit operator Entity(Entity<T> entity) => entity._entity;

        readonly Entity _entity;
    }

    struct Or<T1, T2> { }
    struct And<T1, T2> { }
    struct Not<T> { }
    struct Has<T> { }

    struct Target
    {
        // Express requirements explicitly.
        public Entity<(Targetable, Not<IsInvincible>)> Entity;
    }

    public sealed class World
    {
        readonly ConcurrentDictionary<Type, object> _state = new();

        public bool TryGet<T>(out T state)
        {
            if (_state.TryGetValue(typeof(T), out var value))
            {
                state = (T)value;
                return true;
            }
            state = default;
            return false;
        }

        public T Get<T>() where T : new() => Get(() => new T());
        public T Get<T>(Func<T> initialize) => (T)_state.GetOrAdd(typeof(T), _ => initialize());
    }

    public record Meta
    {
        public readonly Type Type;
        public readonly uint Index;
        public Meta(Type type, uint index) { Type = type; Index = index; }
    }

    public readonly struct Template
    {
        public readonly Initialize Initialize;
        public readonly Segment Segment;
    }

    public delegate void Initialize(int index, Segment.Chunk chunk, Segment segment);

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

        readonly int _size;
        readonly uint[] _indices;
        readonly ConcurrentBag<int> _free = new ConcurrentBag<int>();
        Chunk[] _chunks = { };

        public Segment(Meta[] metas, byte size = 32)
        {
            Metas = metas;
            _size = size;
            _indices = new uint[metas.Length == 0 ? 0 : metas.Max(meta => meta.Index + 1)];
            _indices.Fill(uint.MaxValue);
            for (var i = 0u; i < metas.Length; i++) _indices[metas[i].Index] = i;
        }

        public bool Has(Meta meta) => _indices.TryAt(meta.Index, out var index) && Metas.TryAt(index, out _);

        public bool TryGet(Chunk chunk, Meta meta, out Array store)
        {
            if (_indices.TryAt(meta.Index, out var index)) return chunk.Stores.TryAt(index, out store);
            store = default;
            return false;
        }

        public Chunk Take(out int index)
        {
            if (_free.TryTake(out index)) return _chunks[index];

            var chunks = _chunks;
            if (chunks.TryLast(out var chunk, out index) && chunk.Count < _size) return chunk;

            index = chunks.Length;
            chunk = new Chunk(new Entity[_size], Metas.Select(meta => Array.CreateInstance(meta.Type, _size)));
            // If the 'CompareExchange' fails, it means that another thread added a chunk before this one
            // finished. In this case, this thread's work will be discarded, which is fine.
            return Interlocked.CompareExchange(ref _chunks, chunks.Append(chunk), chunks)[index];
        }

        public void Put(int index) => _free.Add(index);
    }


    public sealed class Entities
    {
        struct Data
        {
            public uint Generation;
            public int Index;
            public Segment.Chunk Chunk;
            public Segment Segment;
        }

        public int Capacity => _data.Length * _size;
        public int Count => _last - _free.Count;

        readonly int _shift;
        readonly int _size;
        readonly int _mask;
        readonly ConcurrentBag<int> _free = new();
        readonly object _lock = new();
        Data[][] _data = { };
        Segment[] _segments = { };
        int _last;

        public Entities(int grow = 6)
        {
            _shift = grow;
            _size = 1 << _shift;
            _mask = _size - 1;
            // TODO: should 'Entity(0, 0)' be skipped?
            DataAt(0, 1)[0].Generation++;
        }

        public Segment Segment(Meta[] metas)
        {
            if (TrySegment(metas, out var segment)) return segment;
            lock (_lock)
            {
                if (TrySegment(metas, out segment)) return segment;
                segment = new(metas);
                _segments = _segments.Append(segment);
                return segment;
            }
        }

        public bool TrySegment(Meta[] metas, out Segment segment)
        {
            for (int i = 0; i < _segments.Length; i++)
            {
                segment = _segments[i];
                if (metas.All(segment, (meta, state) => state.Has(meta))) return true;
            }
            segment = default;
            return false;
        }

        public bool Has(Entity entity) =>
            entity.Index < Capacity && DatumAt(entity.Index).Generation == entity.Generation;

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
        public void Create(Span<Entity> entities, Template template)
        {
            var created = 0;
            var segment = template.Segment;
            while (created < entities.Length)
            {
                Span<Data> data;
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
                    for (int i = 0; i < data.Length && chunk.Count < chunk.Entities.Length && created < entities.Length; i++, created++, chunk.Count++)
                    {
                        ref var datum = ref data[i];
                        datum.Segment = segment;
                        datum.Chunk = chunk;
                        datum.Index = (index << 24) | chunk.Count;
                        var entity = entities[created] = new(free, datum.Generation);
                        chunk.Entities[chunk.Count] = entity;
                        // Initialize stores here.
                    }
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
                        foreach (var store in chunk.Stores) Array.Copy(store, source, store, target, 1);
                        DatumAt(target).Index = index;
                    }
                    segment.Put(index >> 24);
                }
                _free.Add(entity.Index);
                return true;
            }
            return false;
        }

        ref Data DatumAt(int index) => ref _data[index >> _shift][index & _mask];

        Span<Data> DataAt(int index, int count)
        {
            var chunk = index >> _shift;
            var item = index & _mask;
            // Use a lock rather than 'CompareExchange' to prevent thread fighting.
            while (chunk >= _data.Length) lock (_data) _data = _data.Append(new Data[_size]);
            return _data[chunk].AsSpan(item, count);
        }
    }
}