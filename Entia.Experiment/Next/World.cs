using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public delegate void Initialize<T>(int index, int count, Segment.Chunk chunk, in T state);

    public sealed record Meta : IComparable<Meta>
    {
        public readonly Type Type;
        public readonly uint Index;
        public Meta(Type type, uint index) { Type = type; Index = index; }
        public int CompareTo(Meta other) => Index.CompareTo(other.Index);
    }

    public sealed class World : IEnumerable<World.Enumerator, Entity>
    {
        public struct Datum
        {
            public uint Generation;
            public int Index;
            public Segment.Chunk Chunk;
            public Segment Segment;
        }

        public struct Enumerator : IEnumerator<Entity>
        {
            public Entity Current { get; private set; }
            object IEnumerator.Current => Current;

            readonly World _world;
            int _index;

            public Enumerator(World world)
            {
                _world = world;
                _index = -1;
                Current = default;
            }

            public bool MoveNext()
            {
                while (_world.TryDatumAt(++_index, out var datum))
                {
                    if (datum.Segment == null) continue;
                    Current = new(_index, datum.Generation);
                    return true;
                }
                return false;
            }

            public void Reset() => _index = -1;
            public void Dispose() => this = default;
        }

        const int Shift = 8;
        const int Size = 1 << Shift;
        const int Mask = Size - 1;

        public int Capacity => _data.Length * Size;
        public int Count => _last - _free.Count;
        internal Segment[] Segments => _segments;

        readonly ConcurrentBag<int> _free = new();
        readonly object _lock = new();
        Datum[][] _data;
        Segment[] _segments = { };
        Dictionary<Type, Meta> _metas = new();
        int _last;

        public World()
        {
            _data = new[] { new Datum[Size] };
            // Skip 'Entity(0, 0)' to prevent bugs when mistakenly using 'default(Entity)' or 'Entity.Zero'.
            _data[0][0].Generation++;
        }

        public Segment Segment(Meta[] metas, int? size = default)
        {
            if (TrySegment(metas, out var segment)) return segment;
            lock (_lock)
            {
                if (TrySegment(metas, out segment)) return segment;
                segment = new((uint)_segments.Length, metas, size);
                _segments = _segments.Append(segment);
                return segment;
            }
        }

        public bool TrySegment(Meta[] metas, out Segment segment)
        {
            for (int i = 0; i < _segments.Length; i++)
            {
                segment = _segments[i];
                if (metas.All(segment, (meta, segment) => segment.TryIndex(meta, out _)) &&
                    segment.Metas.All(metas, (meta, metas) => metas.Contains(meta)))
                    return true;
            }
            segment = default;
            return false;
        }

        public bool TryDatum(Entity entity, out Datum datum) => TryDatumAt(entity.Index, out datum) && datum.Generation == entity.Generation;

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
        public Entity Create<T>(Segment segment, in T state, Initialize<T> initialize)
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
        public void Create<T>(Span<Entity> entities, Segment segment, in T state, Initialize<T> initialize)
        {
            var created = 0;
            while (created < entities.Length)
            {
                var remaining = entities.Length - created;
                // Favor using all of the allocated capacity before using free indices. There is a possibility of race
                // condition on the read to '_last' and its increment in 'Reserve' but since worst outcome of the race
                // is to simply pre-emptively allocated the next chunk of data, it is not worth trying to synchronize.
                var data = Reserve(Math.Min(Capacity - _last, remaining), out var reserved);
                if (data.Length == 0) data = _free.TryTake(out reserved) ? DataAt(reserved, 1) : Reserve(remaining, out reserved);

                var end = created + data.Length;
                while (created < end)
                {
                    var chunk = segment.Take();
                    if (chunk.Count == segment.Size) continue;

                    lock (chunk)
                    {
                        var index = chunk.Count;
                        var count = Math.Min(segment.Size - index, end - created);
                        if (count == 0) continue;

                        for (int i = 0; i < count; i++)
                        {
                            ref var datum = ref data[i];
                            var entity = new Entity(reserved + i, datum.Generation);
                            datum.Segment = segment;
                            datum.Chunk = chunk;
                            datum.Index = index + i;
                            chunk.Entities[index + i] = entity;
                            entities[created + i] = entity;
                        }
                        chunk.Count += count;
                        created += count;
                        reserved += count;
                        initialize(index, count, chunk, state);
                    }
                }
            }
        }

        public uint Destroy(Segment segment)
        {
            var destroyed = 0u;
            foreach (var chunk in segment.Chunks)
            {
                if (chunk.Count == 0) continue;
                for (int i = 0; i < chunk.Count; i++)
                {
                    var entity = chunk.Entities[i];
                    ref var datum = ref DatumAt(entity.Index);
                    if (Interlocked.Exchange(ref datum.Segment, null) == segment)
                    {
                        destroyed++;
                        datum.Generation++;
                        _free.Add(entity.Index);
                    }
                }
                foreach (var store in chunk.Stores) Array.Clear(store, 0, chunk.Count);
                chunk.Count = 0;
                segment.Put(chunk.Index);
            }
            return destroyed;
        }

        public bool Destroy(Entity entity)
        {
            if (entity.Index >= _last) return false;
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

        Span<Datum> Reserve(int count, out int index)
        {
            if (count == 0) { index = default; return Span<Datum>.Empty; }

            index = Interlocked.Add(ref _last, count) - count;
            var data = DataAt(index, count);
            // If there was an overflow, add the missed indices to the '_free' bag. This can happen if the 'Datum' chunk
            // was too small to fit the whole 'count'.
            for (int i = data.Length; i < count; i++) _free.Add(index + i);
            return data;
        }

        ref Datum DatumAt(int index) => ref _data[index >> Shift][index & Mask];

        bool TryDatumAt(int index, out Datum datum)
        {
            if (index < _last)
            {
                datum = DatumAt(index);
                return true;
            }
            datum = default;
            return false;
        }

        Span<Datum> DataAt(int index, int count)
        {
            var chunk = index >> Shift;
            var item = index & Mask;
            // Use a lock rather than 'CompareExchange' to prevent thread fighting.
            while (chunk >= _data.Length) lock (_data) _data = _data.Append(new Datum[Size]);
            return _data[chunk].AsSpan(item, count);
        }

        public Enumerator GetEnumerator() => new(this);
        IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}