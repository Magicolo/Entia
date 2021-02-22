using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public sealed record Meta : IComparable<Meta>
    {
        public readonly uint Index;
        public readonly Type Type;
        public readonly bool IsPlain;
        public readonly bool IsBlittable;
        public Meta(uint index, Type type)
        {
            Index = index;
            Type = type;
            IsPlain = type.IsPlain();
            IsBlittable = type.IsBlittable();
        }

        public int CompareTo(Meta other) => Index.CompareTo(other.Index);
    }

    public delegate void Initialize<T>(in Context context, in T state);

    public sealed class World
    {
        public struct Datum
        {
            public int Index;
            public Segment.Chunk Chunk;
            public Segment Segment;
            public Datum(int index, Segment.Chunk chunk, Segment segment) { Index = index; Chunk = chunk; Segment = segment; }
            public void Deconstruct(out int index, out Segment.Chunk chunk, out Segment segment) => (index, chunk, segment) = (Index, Chunk, Segment);
        }

        struct BufferKey { }
        struct ReleaseBuffers
        {
            public static readonly ReleaseBuffers Empty = new()
            {
                Entities = Array.Empty<Entity>(),
                Data = Array.Empty<Datum>(),
            };

            public Entity[] Entities;
            public Datum[] Data;
            public int Count;
            public int Capacity;

            public void Ensure()
            {
                if (Count <= Capacity) return;
                Capacity = Math.Max(Capacity * 2, 8);
                Buffer.Ensure<BufferKey, Entity>(ref Entities, Capacity);
                Buffer.Ensure<BufferKey, Datum>(ref Data, Capacity);
            }
        }

        const int Shift = 8;
        const int Size = 1 << Shift;
        const int Mask = Size - 1;

        public int Capacity => _data.Length * Size;
        public int Count => _last - _free.count;
        internal Segment[] Segments => _segments;

        (Entity[] items, object @lock, int count) _free = (new Entity[Size], new(), 1);
        Dictionary<Type, Meta> _metas = new();
        Datum[][] _data = { new Datum[Size] };
        Segment[] _segments = { };
        int _last = 1; // Skip 'Entity(0, 0)' to prevent bugs when mistakenly using 'default(Entity)' or 'Entity.Zero'.

        public Segment Segment(Meta[] metas, int? size = default) =>
            Concurrent.MutateUntilGet(ref _segments, (metas: metas.Sorted(), size),
                static (segments, pair) =>
                {
                    for (int i = 0; segments.TryAt(i, out var segment); i++)
                        if (ArrayUtility.Equals(segment.Metas, pair.metas)) return Option.From(segment);
                    return Option.None();
                },
                static (segments, pair) => segments.Append(new Segment((uint)segments.Length, pair.metas, pair.size)));

        public bool TryDatum(Entity entity, out Datum datum) =>
            TryDatumAt(entity.Index, out datum) && datum.Chunk?.Entities[datum.Index] == entity;

        public Meta Meta(Type type) =>
            Concurrent.MutateUntilGet(ref _metas, type,
                static (metas, type) => metas.TryGetValue(type, out var meta) ? Option.From(meta) : Option.None(),
                static (metas, type) => new(metas) { { type, new((uint)metas.Count, type) } });

        public bool TryMeta(Type type, out Meta meta) => _metas.TryGetValue(type, out meta);

        public void Reserve(Span<Entity> entities)
        {
            // Favor using all of the allocated capacity before using free indices. There is a possibility of race
            // condition on the read to '_last' and 'Add' but since worst outcome of the race is to simply
            // pre-emptively allocated the next chunk of data, it is not worth trying to synchronize.
            var reserved = 0;
            if (_last + entities.Length > Capacity)
                foreach (var entity in Reserve(entities.Length))
                    entities[reserved++] = new(entity.Index, entity.Generation + 1);

            if (reserved == entities.Length) return;
            var remaining = entities.Length - reserved;
            var last = Interlocked.Add(ref _last, remaining);
            Concurrent.Extend(ref _data, ((last - 1) >> Shift) + 1, static _ => new Datum[Size]);
            for (int i = last - remaining; i < last; i++) entities[reserved++] = new(i, 0);
        }

        public void Initialize<T>(ReadOnlySpan<Entity> entities, Slice<Entity>.Read parents, Slice<Entity>.Read children, Segment segment, in T state, Initialize<T> initialize)
        {
            var initialized = 0;
            var batch = entities.Length;
            while (initialized < batch)
            {
                var chunk = segment.Next();
                // Try to prevent the lock if the chunk is full.
                if (chunk.Count == segment.Size) continue;
                var remaining = batch - initialized;

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
                        DatumAt(entity.Index) = new(target, chunk, segment);
                    }

                    // Initialize before incrementing 'chunk.Count' to ensure that no reader can
                    // observe a chunk item uninitialized.
                    initialize(new Context(index, count, chunk), state);
                    chunk.Count += count;
                    initialized += count;
                }
            }
        }

        public uint Release(ReadOnlySpan<Entity> entities)
        {
            if (entities.Length == 0) return 0;
            var roots = (items: Buffer.Get<BufferKey, (Entity child, Entity parent)>(entities.Length), count: 0);
            var buffers = ReleaseBuffers.Empty;
            foreach (var entity in entities) if (entity.Index < _last) TakeData(entity, ref roots, ref buffers, true);
            if (buffers.Count == 0) return 0;

            // Remove the root entities from their parent's children.
            for (int i = 0; i < roots.count; i++)
            {
                var (child, parent) = roots.items[i];
                if (parent == Entity.Zero) continue;
                ref var datum = ref DatumAt(parent.Index);
                var chunk = datum.Chunk;
                if (chunk == null) continue;
                lock (chunk)
                {
                    var index = datum.Index;
                    // If 'datum.Chunk' was taken while waiting on the lock, it is unnecessary
                    // to remove the child.
                    if (datum.Chunk == chunk && chunk.Entities[index] == parent)
                        chunk.Children[index].Remove(child);
                }
            }

            for (int i = 0; i < buffers.Count; i++)
            {
                var (index, chunk, segment) = buffers.Data[i];
                // Try to prevent the lock if possible.
                if (index >= chunk.Count) continue;

                int count, source;
                lock (chunk)
                {
                    // If this is true, it means that another thread cleared this index.
                    if (index >= chunk.Count) continue;

                    // To make the most of the lock and reduce contention, this thread tries to clear the 'index'
                    // and if it crosses greater indices that must also be cleared, it clears those as well.
                    count = chunk.Count;
                    source = --chunk.Count;
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
                }

                // Clear the stores in case there is garbage to be collected in them.
                // This doesn't need to happen within the lock since the indices '[source, count[' are
                // exclusive to this thread.
                for (int j = 0; j < segment.Metas.Length; j++)
                {
                    if (segment.Metas[j].IsPlain) continue;
                    Array.Clear(chunk.Stores[j], source, count - source);
                }
                if (count == segment.Size) segment.Put(chunk);
            }

            // Free the entities lastly to ensure that they cannot be reserved while the release
            // operation is ongoing.
            Concurrent.Push(ref _free.items, ref _free.count, buffers.Entities.AsSpan(0, buffers.Count));
            return (uint)buffers.Count;
        }

        ReadOnlySpan<Entity> Reserve(int count)
        {
            int index;
            lock (_free.@lock)
            {
                count = Math.Min(_free.count, count);
                index = _free.count -= count;
            }
            return _free.items.AsSpan(index, count);
        }

        bool TryTakeDatum(Entity entity, out Datum datum)
        {
            ref var current = ref DatumAt(entity.Index);
            var chunk = current.Chunk;
            if (chunk == null) { datum = default; return false; }

            lock (chunk)
            {
                datum = current;
                if (current.Chunk == chunk && chunk.Entities[datum.Index] == entity)
                {
                    current = default;
                    return true;
                }
            }
            return false;
        }

        void TakeData(Entity entity, ref ((Entity child, Entity parent)[] items, int count) roots, ref ReleaseBuffers buffers, bool root)
        {
            if (TryTakeDatum(entity, out var datum))
            {
                var count = buffers.Count++;
                buffers.Ensure();
                buffers.Entities[count] = entity;
                buffers.Data[count] = datum;
                if (root) roots.items[roots.count++] = (entity, datum.Chunk.Parents[datum.Index]);

                var children = datum.Chunk.Children[datum.Index];
                for (int i = 0; i < children.count; i++) TakeData(children.items[i], ref roots, ref buffers, false);
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