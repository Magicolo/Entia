using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public sealed class Segment : IComparable<Segment>
    {
        public sealed class Chunk
        {
            public readonly uint Index;
            public readonly Entity[] Entities;
            public readonly Entity[] Parents;
            public readonly (Entity[] items, int count)[] Children;
            public readonly Array[] Stores;
            public int Count;

            public Chunk(uint index, Entity[] entities, Entity[] parents, (Entity[] items, int count)[] children, Array[] stores)
            {
                Index = index;
                Entities = entities;
                Parents = parents;
                Children = children;
                Stores = stores;
            }
        }

        public readonly uint Index;
        public readonly Meta[] Metas;
        public readonly int Size;
        internal Chunk[] Chunks = { };
        readonly ConcurrentQueue<Chunk> _free = new();

        public Segment(uint index, Meta[] metas, int? size = default)
        {
            Index = index;
            Metas = metas;
            Size = size ?? 64;
        }

        public bool TryIndex(Meta meta, out int index) => (index = Array.BinarySearch(Metas, meta)) >= 0;

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

        public Chunk Next()
        {
            if (TryTake(out var chunk)) return chunk;
            var chunks = Chunks;
            var stores = Metas.Select(Size, (meta, size) => Array.CreateInstance(meta.Type, size));
            var children = ArrayUtility.Filled(Size, (Array.Empty<Entity>(), 0));
            var index = chunks.Length;
            chunk = new((uint)index, new Entity[Size], new Entity[Size], children, stores);
            // If the 'CompareExchange' fails, it means that another thread added a chunk before this one
            // finished. In this case, this thread's work will be discarded, which is fine.
            if (Interlocked.CompareExchange(ref Chunks, chunks.Append(chunk), chunks) == chunks)
            {
                Put(chunk);
                return chunk;
            }
            // Another thread created the chunk so just use it.
            else return Chunks[index];
        }

        public bool TryTake(out Chunk chunk)
        {
            while (_free.TryDequeue(out chunk)) if (Put(chunk)) return true;
            return false;
        }

        public bool Put(Chunk chunk)
        {
            if (chunk.Count == Size) return false;
            _free.Enqueue(chunk);
            return true;
        }

        public int CompareTo(Segment other) => Index.CompareTo(other.Index);
    }
}