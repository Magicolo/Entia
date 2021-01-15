using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Entia.Core;

namespace Entia.Experiment.V3
{
    /*
        In V3, we remove the ability for entities to change their structure dynamically post-creation.
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

    // The only way to obtain an 'Entity<T>' where 'T' represents some requirements is through the 'Components'
    // module which has to validate that an 'Entity' does satisfy 'T'. If entities cannot change their structure
    // post-creation, the 'Entity' will always satisfy 'T' as long as it lives so the only check that remains
    // is the check for life since the checks for component existence are unnecessary.
    public struct Entity<T>
    {
        public static implicit operator Entity(Entity<T> entity) => entity._entity;

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
        public Entity<(Has<Targetable>, Not<IsInvincible>)> Entity;
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

    public sealed class Meta
    {
        public readonly Type Type;
        public readonly int Index;
        public readonly bool Plain;

        public Meta(Type type, int index)
        {
            Type = type;
            Index = index;
        }
    }

    public sealed class Segment
    {
        internal sealed class Chunk
        {
            public readonly Array[] Stores;
            public readonly Entity[] Entities = new Entity[Size];
            public readonly int[] States = new int[Size];
            public int Count;
            public int Last;
            public Chunk(Array[] stores) => Stores = stores;
        }

        public struct Data
        {
            public uint Segment;
            public uint Chunk;
            public uint Index;
            public int State;
        }

        const int Shift = 4;
        const int Size = 1 << Shift;
        const int Mask = Size - 1;

        public int Capacity => _chunks.Length * Size;

        public readonly uint Index;
        public readonly Meta[] Metas;

        readonly int[] _indices;
        readonly ConcurrentBag<Chunk> _free = new();
        Chunk[] _chunks = { };
        // int _last;

        public Segment(uint index, Meta[] metas)
        {
            Index = index;
            Metas = metas;
            _indices = new int[metas.Length == 0 ? 0 : metas.Max(meta => meta.Index + 1)];
            _indices.Fill(int.MaxValue);
            for (int i = 0; i < metas.Length; i++) _indices[metas[i].Index] = i;
        }

        public void Create(Span<Entity> entities/*, initialize TODO: Add something to initialize components. */)
        {
            var created = 0;
            while (created < entities.Length)
            {
                var chunk = _free.TryTake(out var free) ? free : NextChunk();
                var reserved = entities.Length - created;
                var last = Interlocked.Add(ref chunk.Last, reserved);
                var overflow = Math.Max(Size - last, 0);
                var index = (uint)(last - reserved);
                for (; index < Size; index++, created++)
                {
                    ref var entity = ref chunk.Entities[index];
                    entities[created] = entity = new(entity.Index, entity.Generation + 1);
                    chunk.States[index] = 1;
                }
                if (overflow < 0) Interlocked.Add(ref chunk.Last, overflow);
                Interlocked.Add(ref chunk.Count, reserved + overflow);
            }
        }

        public int Destroy(ReadOnlySpan<Entity> entities)
        {
            var destroyed = 0;
            // foreach (var entity in entities)
            // {
            //     var data = Decompose(entity.Identifier);
            //     if (_chunks.TryAt(data.Chunk, out var chunk))
            //     {
            //         ref var slot = ref chunk.Entities[data.Index];
            //         if (entity == slot && Interlocked.Exchange(ref chunk.States[data.Index], -1) == 1)
            //         {
            //             var index = Interlocked.Decrement(ref chunk.Count);
            //             if (index > data.Index)
            //             {
            //                 slot = chunk.Entities[index];
            //             }
            //             destroyed++;
            //             _free.Add(chunk);
            //         }
            //     }
            // }
            return destroyed;
        }

        // public bool Has(Entity entity)
        // {
        //     var data = Decompose(entity.Identifier);
        //     return _chunks.TryAt(data.Chunk, out var chunk) &&
        //         chunk.States[data.Index] == 1 &&
        //         chunk.Entities[data.Index] == entity;
        // }

        public int Store(Meta meta) => _indices[meta.Index];

        Chunk NextChunk()
        {
            var chunks = _chunks;
            if (chunks.TryLast(out var chunk) && chunk.Count < Size) return chunk;

            var index = (uint)chunks.Length;
            chunk = new Chunk(Metas.Select(meta => Array.CreateInstance(meta.Type, Size)));
            // If the 'CompareExchange' fails, it means that another thread added a chunk before this one
            // finished. In this case, this thread's work will be discarded, which is fine.
            chunks = Interlocked.CompareExchange(ref _chunks, chunks.Append(chunk), chunks);
            // Read again from the chunks array in case the 'CompareExchange' failed.
            return chunks[index];
        }
    }

    // public readonly struct Entities
    // {
    //     internal struct Data
    //     {
    //         public uint Generation;
    //         public int Alive;
    //         public int Index;
    //         public int Segment;
    //     }

    //     internal class State
    //     {
    //         const int Shift = 5;
    //         const int Size = 1 << Shift;
    //         const int Mask = Size - 1;

    //         public int Capacity => _data.Length * Size;
    //         public int Count => _last - _free.Count;

    //         readonly ConcurrentBag<int> _free = new ConcurrentBag<int>();
    //         Data[][] _data = { };
    //         int _last;

    //         public bool Has(Entity entity)
    //         {
    //             ref var data = ref DataAt(entity.Index);
    //             return data.Generation == entity.Generation && data.Alive > 0;
    //         }

    //         public void Create(Span<Entity> entities)
    //         {
    //             var created = 0;
    //             while (created < entities.Length)
    //             {
    //                 if (_free.TryTake(out var index))
    //                 {
    //                     ref var slot = ref DataAt(index);
    //                     slot = new Data { Generation = slot.Generation + 1, Alive = 1 };
    //                     entities[created++] = new Entity(index, slot.Generation);
    //                 }
    //                 else
    //                 {
    //                     var count = entities.Length - created;
    //                     index = Interlocked.Add(ref _last, count) - count;
    //                     var slots = DataAt(index, count);
    //                     for (int i = 0; i < slots.Length; i++)
    //                     {
    //                         ref var slot = ref slots[i];
    //                         slot = new Data { Generation = 1, Alive = 1 };
    //                         entities[created++] = new Entity(index + i, slot.Generation);
    //                     }
    //                 }
    //             }
    //         }

    //         // TODO: Add batch destruction: bool Destroy(Span<Entity> buffer)
    //         public bool Destroy(Entity entity, out Data destroyed)
    //         {
    //             ref var data = ref DataAt(entity.Index);
    //             if (entity.Generation == data.Generation && Interlocked.Decrement(ref data.Alive) == 0)
    //             {
    //                 destroyed = data;
    //                 _free.Add(entity.Index);
    //                 return true;
    //             }
    //             destroyed = default;
    //             return false;
    //         }

    //         ref Data DataAt(int index) => ref _data[index >> Shift][index & Mask];

    //         Span<Data> DataAt(int index, int count)
    //         {
    //             var chunk = index >> Shift;
    //             var item = index & Mask;
    //             // Use a lock rather than 'CompareExchange' to prevent thread fighting.
    //             while (chunk >= _data.Length) lock (_data) Interlocked.Exchange(ref _data, _data.Append(new Data[Size]));
    //             return _data[chunk].AsSpan(item, count);
    //         }
    //     }

    //     readonly State _entities;

    //     public Entities(World world)
    //     {
    //         _entities = world.Get<State>();
    //     }

    //     public bool Has(Entity entity) => _entities.Has(entity);

    //     public Entity Create()
    //     {
    //         var entities = (Span<Entity>)stackalloc Entity[1];
    //         Create(entities);
    //         return entities[0];
    //     }

    //     public void Create(Span<Entity> entities)
    //     {
    //         _entities.Create(entities);
    //     }

    //     public bool Destroy(Entity entity) => _entities.Destroy(entity, out _);
    // }
}