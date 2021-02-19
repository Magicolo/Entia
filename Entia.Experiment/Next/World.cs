using System;
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
    struct WorldBufferKey { }

    public sealed class World
    {
        public struct Datum
        {
            public int Index;
            public Segment.Chunk Chunk;
            public Segment Segment;
        }

        struct ReleaseBuffers
        {
            public static readonly ReleaseBuffers Empty = new()
            {
                Entities = Array.Empty<Entity>(),
                Indices = Array.Empty<int>(),
                Chunks = Array.Empty<Segment.Chunk>(),
                Segments = Array.Empty<Segment>(),
            };

            public Entity[] Entities;
            public int[] Indices;
            public Segment.Chunk[] Chunks;
            public Segment[] Segments;
            public int Count;
            public int Capacity;

            public void Ensure()
            {
                if (Count <= Capacity) return;
                Capacity = Math.Max(Capacity * 2, 8);
                Buffer.Ensure<ReleaseBuffers, Entity>(ref Entities, Capacity);
                Buffer.Ensure<ReleaseBuffers, int>(ref Indices, Capacity);
                Buffer.Ensure<ReleaseBuffers, Segment.Chunk>(ref Chunks, Capacity);
                Buffer.Ensure<ReleaseBuffers, Segment>(ref Segments, Capacity);
            }
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
                var buffer = Buffer.Get<WorldBufferKey, Entity>(entities.Length);
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

        public void Initialize<T>(ReadOnlySpan<Entity> entities, Slice<Entity>.Read parents, Slice<Entity>.Read children, Segment segment, in T state, Initialize<T> initialize)
        {
            var initialized = 0;
            var batch = entities.Length;
            while (initialized < batch)
            {
                var remaining = batch - initialized;
                var chunk = segment.Next();
                // Try to prevent the lock if the chunk is full.
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
                        var entity = entities[source];
                        chunk.Entities[target] = entity;
                        chunk.Parents[target] = parents.Get(source).Or(Entity.Zero);
                        chunk.Children[target].Reset(children.Slice(source, step: batch));
                        DatumAt(entity.Index) = new() { Index = target, Chunk = chunk, Segment = segment };
                    }

                    // Initialize before incrementing 'chunk.Count' to ensure that no reader can
                    // observe a chunk item uninitialized.
                    var context = new Context(index, count, chunk);
                    initialize(context, state);
                    chunk.Count += count;
                    initialized += count;
                }

                if (chunk.Count == segment.Size) continue;
                segment.Free.Push(chunk);
            }
        }

        public uint Release(ReadOnlySpan<Entity> entities)
        {
            if (entities.Length == 0) return 0;
            var roots = (items: Buffer.Get<WorldBufferKey, int>(entities.Length), count: 0);
            var buffers = ReleaseBuffers.Empty;
            foreach (var entity in entities) if (entity.Index < _last) Collect(entity, ref roots, ref buffers);
            if (buffers.Count == 0) return 0;

            for (int i = 0; i < buffers.Count; i++)
            {
                var index = buffers.Indices[i];
                var chunk = buffers.Chunks[i];
                var segment = buffers.Segments[i];
                // Try to prevent the lock if possible.
                if (index >= chunk.Count) continue;

                lock (chunk)
                {
                    // If this is true, it means that another thread cleared this index.
                    if (index >= chunk.Count) continue;

                    // To make the most of the lock and reduce contention, this thread tries to clear the 'index'
                    // and if it crosses greater indices that must also be cleared, it clears those as well.
                    var count = chunk.Count;
                    var source = --chunk.Count;
                    while (index < source)
                    {
                        var last = chunk.Entities[source];
                        ref var datum = ref DatumAt(last.Index);
                        if (datum.Chunk == chunk)
                        {
                            chunk.Entities[index] = last;
                            chunk.Parents[index] = chunk.Parents[source];
                            chunk.Children.Swap(source, index);
                            foreach (var store in chunk.Stores) Array.Copy(store, source, store, index, 1);
                            datum.Index = index;
                            break;
                        }
                        else source = --chunk.Count;
                    }
                    // Clear the stores in case there is garbage to be collected in them.
                    foreach (var store in chunk.Stores) Array.Clear(store, source, count - source);
                }
                segment.Free.Push(chunk);
            }

            // Remove the root entities from their parent's children.
            for (int i = 0; i < roots.count; i++)
            {
                var root = roots.items[i];
                var child = buffers.Entities[root];
                var parent = buffers.Chunks[root].Parents[buffers.Indices[root]];
                ref var datum = ref DatumAt(parent.Index);
                var chunk = datum.Chunk;
                if (chunk == null) continue;
                lock (chunk)
                {
                    // If 'datum.Chunk' was taken while waiting on the lock, it is unnecessary
                    // to remove the child.
                    if (datum.Chunk == chunk) chunk.Children[datum.Index].Remove(child);
                }
            }

            // Free the entities lastly to ensure that they cannot be reserved while the release
            // operation is ongoing.
            _free.PushRange(buffers.Entities, 0, buffers.Count);
            return (uint)buffers.Count;
        }

        bool TryTake(Entity entity, out int index, out Segment.Chunk chunk, out Segment segment)
        {
            ref var datum = ref DatumAt(entity.Index);
            chunk = datum.Chunk;
            if (chunk == null)
            {
                index = default;
                segment = default;
                return false;
            }

            lock (chunk)
            {
                index = datum.Index;
                segment = datum.Segment;
                if (datum.Chunk == chunk && chunk.Entities[index] == entity)
                {
                    datum = default;
                    return true;
                }
                return false;
            }
        }

        void Collect(Entity entity, ref (int[] items, int count) roots, ref ReleaseBuffers buffers, bool root = true)
        {
            if (TryTake(entity, out var index, out var chunk, out var segment))
            {
                var count = buffers.Count++;
                buffers.Ensure();
                buffers.Entities[count] = entity;
                buffers.Indices[count] = index;
                buffers.Chunks[count] = chunk;
                buffers.Segments[count] = segment;
                if (root) roots.items[roots.count++] = count;

                var children = chunk.Children[index];
                for (int i = 0; i < children.count; i++) Collect(children.items[i], ref roots, ref buffers, false);
            }
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
    }
}