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
            // 'Entities' could technically be moved to the 'Segment' in 1 continuous array rather than being separated
            // chunks, but this setup will be practical to dispatch threads with only a chunk.
            public readonly Entity[] Entities;
            public readonly Array[] Stores;
            public readonly uint Index;
            public int Count;

            public Chunk(uint index, Entity[] entities, Array[] stores)
            {
                Index = index;
                Entities = entities;
                Stores = stores;
            }
        }

        public readonly uint Index;
        public readonly Meta[] Metas;
        public readonly int Size;
        internal Chunk[] Chunks => _chunks;

        readonly ConcurrentBag<uint> _free = new();
        Chunk[] _chunks = { };

        public Segment(uint index, Meta[] metas, int? size = default)
        {
            Index = index;
            Metas = metas.Sorted();
            Size = size ?? 256;
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

        public Chunk Take()
        {
            var chunks = _chunks;
            if (chunks.TryLast(out var chunk, out var index) && chunk.Count < Size) return chunk;
            if (_free.TryTake(out var free)) return _chunks[free];

            index = chunks.Length;
            chunk = new((uint)index, new Entity[Size], Metas.Select(meta => Array.CreateInstance(meta.Type, Size)));
            // If the 'CompareExchange' fails, it means that another thread added a chunk before this one
            // finished. In this case, this thread's work will be discarded, which is fine.
            Interlocked.CompareExchange(ref _chunks, chunks.Append(chunk), chunks);
            // Read from 'Chunks' in case 'CompareExchange' fails.
            return _chunks[index];
        }

        public void Put(Chunk chunk) => _free.Add(chunk.Index);
        public int CompareTo(Segment other) => Index.CompareTo(other.Index);
    }
}