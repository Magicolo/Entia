using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public sealed record Meta : IComparable<Meta>
    {
        public readonly Type Type;
        public readonly uint Index;
        public Meta(Type type, uint index) { Type = type; Index = index; }
        public int CompareTo(Meta other) => Index.CompareTo(other.Index);
    }

    public delegate void Initialize<T>(in Context context, in T state);

    public sealed class World : IEnumerable<World.Enumerator, Entity>
    {
        public struct Datum
        {
            public int Index;
            public Entity Parent;
            public Segment.Chunk Chunk;
            public Segment Segment;
        }

        public struct Enumerator : IEnumerator<Entity>
        {
            public Entity Current => _chunk.Entities[_indices.entity];
            object IEnumerator.Current => Current;

            readonly World _world;
            (int segment, int chunk, int entity) _indices;
            Segment _segment;
            Segment.Chunk _chunk;

            public Enumerator(World world)
            {
                _world = world;
                _indices = (-1, -1, -1);
                _segment = default;
                _chunk = default;
            }

            public bool MoveNext()
            {
                while (true)
                {
                    if (_segment == null && !_world._segments.TryAt(++_indices.segment, out _segment)) return false;
                    else if (_chunk == null && !_segment.Chunks.TryAt(++_indices.chunk, out _chunk))
                    {
                        _indices.chunk = -1;
                        _segment = null;
                    }
                    else if (++_indices.entity < _chunk.Count) return true;
                    else
                    {
                        _indices.entity = -1;
                        _chunk = null;
                    }
                }
            }

            public void Reset() { _indices = (-1, -1, -1); _segment = default; _chunk = default; }
            public void Dispose() => this = default;
        }

        const int Shift = 8;
        const int Size = 1 << Shift;
        const int Mask = Size - 1;

        public int Capacity => _data.Length * Size;
        public int Count => _last - _free.Count;
        internal Segment[] Segments => _segments;

        readonly ConcurrentStack<Entity> _free = new(new[] { Entity.Zero });
        Dictionary<Type, Meta> _metas = new();
        Datum[][] _data = { new Datum[Size] };
        Segment[] _segments = { };
        int _last = 1; // Skip 'Entity(0, 0)' to prevent bugs when mistakenly using 'default(Entity)' or 'Entity.Zero'.

        public Segment Segment(Meta[] metas, int? size = default)
        {
            var segments = _segments;
            metas.Sort();
            while (true)
            {
                if (Find(segments, metas, out var segment)) return segment;
                segment = new((uint)_segments.Length, metas, size);
                if (Interlocked.CompareExchange(ref _segments, segments.Append(segment), segments) == segments) return segment;
                segments = _segments;
            }

            static bool Find(Segment[] segments, Meta[] metas, out Segment segment)
            {
                for (int i = 0; i < segments.Length; i++)
                    if (ArrayUtility.Equals((segment = segments[i]).Metas, metas)) return true;
                segment = default;
                return false;
            }
        }

        public bool TryDatum(Entity entity, out Datum datum) =>
            TryDatumAt(entity.Index, out datum) && datum.Chunk?.Entities[datum.Index] == entity;

        public Meta Meta(Type type)
        {
            var metas = _metas;
            while (true)
            {
                if (metas.TryGetValue(type, out var meta)) return meta;
                meta = new(type, (uint)metas.Count);
                if (Interlocked.CompareExchange(ref _metas, new(_metas) { { meta.Type, meta } }, metas) == metas) return meta;
                metas = _metas;
            }
        }

        public bool TryMeta(Type type, out Meta meta) => _metas.TryGetValue(type, out meta);

        public void Reserve(Span<Entity> entities)
        {
            // Favor using all of the allocated capacity before using free indices. There is a possibility of race
            // condition on the read to '_last' and 'Add' but since worst outcome of the race is to simply
            // pre-emptively allocated the next chunk of data, it is not worth trying to synchronize.
            var reserved = 0;
            if (_last + entities.Length > Capacity)
            {
                var buffer = Buffer<World, Entity>.Get(entities.Length);
                var popped = _free.TryPopRange(buffer, 0, entities.Length);
                for (int i = 0; i < popped; i++) entities[i] = new(buffer[i].Index, buffer[i].Generation + 1);
                reserved += popped;
            }

            if (reserved == entities.Length) return;
            var count = entities.Length - reserved;
            var last = Interlocked.Add(ref _last, count);
            var chunk = (last - 1) >> Shift;
            var data = _data;
            while (chunk >= data.Length)
            {
                Interlocked.CompareExchange(ref _data, data.Append(new Datum[Size]), data);
                data = _data;
            }
            for (int i = last - count; i < last; i++) entities[reserved++] = new(i, 0);
        }

        public void Initialize<T>(Span<Entity> entities, Slice<Entity>.Read parents, Slice<Entity>.Read children, Segment segment, in T state, Initialize<T> initialize)
        {
            var initialized = 0;
            var batch = entities.Length;
            while (initialized < batch)
            {
                var remaining = batch - initialized;
                var chunk = segment.Next();
                if (chunk.Count == segment.Size) continue;

                lock (chunk)
                {
                    if (chunk.Count == segment.Size) continue;
                    var index = chunk.Count;
                    var count = Math.Min(segment.Size - index, remaining);

                    for (int i = 0; i < count; i++)
                    {
                        var source = initialized + i;
                        var target = index + i;
                        var entity = chunk.Entities[target] = entities[source];
                        var parent = source < parents.Count ? parents[source] : Entity.Zero;
                        DatumAt(entity.Index) = new()
                        {
                            Index = target,
                            Parent = parent,
                            Chunk = chunk,
                            Segment = segment,
                        };
                    }

                    // Initialize before incrementing 'chunk.Count' to ensure that no reader can
                    // observe a chunk item uninitialized.
                    var context = new Context(
                        index, count, batch, chunk,
                        parents.Slice(initialized, count),
                        children.Slice(initialized, count * batch));
                    initialize(context, state);
                    chunk.Count += count;
                    initialized += count;
                }

                if (chunk.Count == segment.Size) continue;
                segment.Free.Push(chunk);
            }
        }

        public int Release(Segment segment)
        {
            var destroyed = 0;
            foreach (var chunk in segment.Chunks)
            {
                if (chunk.Count == 0) continue;
                for (int i = 0; i < chunk.Count; i++) DatumAt(chunk.Entities[i].Index).Segment = null;
                foreach (var store in chunk.Stores) Array.Clear(store, 0, chunk.Count);
                _free.PushRange(chunk.Entities, 0, chunk.Count);
                destroyed += chunk.Count;
                chunk.Count = 0;
            }
            segment.Free.Clear();
            segment.Free.PushRange(segment.Chunks);
            return destroyed;
        }

        public bool Release(Entity entity)
        {
            if (entity.Index >= _last) return false;

            ref var datum = ref DatumAt(entity.Index);
            if (Interlocked.Exchange(ref datum.Segment, null) is Segment segment &&
                entity == datum.Chunk.Entities[datum.Index])
            {
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
                segment.Free.Push(chunk);
                _free.Push(entity);
                return true;
            }
            return false;
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

        public Enumerator GetEnumerator() => new(this);
        IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}