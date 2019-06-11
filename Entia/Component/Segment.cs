using Entia.Components;
using Entia.Core;
using Entia.Core.Documentation;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Entia.Modules.Component
{
    /// <summary>
    /// Stores the entities and components for a specific component profile represented by the <see cref="Mask"/>.
    /// </summary>
    public sealed class Segment
    {
        static (GCHandle handle, IntPtr address) Fix(Array store)
        {
            var handle = GCHandle.Alloc(store, GCHandleType.Pinned);
            var address = handle.AddrOfPinnedObject();
            return (handle, address);
        }

        /// <summary>
        /// The index of the segment.
        /// </summary>
        public readonly int Index;
        /// <summary>
        /// The mask that represents the component profile of the segment.
        /// </summary>
        public readonly BitMask Mask;
        /// <summary>
        /// The selection of component types that are stored in this segment with the minimum and maximum indices of those types.
        /// </summary>
        public readonly Metadata[] Types;
        public readonly Metadata[] Components;
        public readonly Metadata[] Tags;
        /// <summary>
        /// The entities.
        /// </summary>
        public (Entity[] items, int count) Entities;

        readonly int _minimum;
        readonly int _maximum;
        Array[] _stores;
        (GCHandle handle, IntPtr address)[] _handles;

        /// <summary>
        /// Initializes a new instance of the <see cref="Segment"/> class.
        /// </summary>
        public Segment(int index, BitMask mask, int capacity = 4) : this(index, mask, ComponentUtility.ToMetadata(mask), capacity) { }

        Segment(int index, BitMask mask, Metadata[] types, int capacity = 4) : this(
            index,
            mask,
            types,
            types.Where(type => type.Kind == Metadata.Kinds.Data).ToArray(),
            types.Where(type => type.Kind == Metadata.Kinds.Tag).ToArray(),
            (new Entity[capacity], 0))
        { }

        Segment(int index, BitMask mask, Metadata[] types, Metadata[] components, Metadata[] tags, in (Entity[] items, int count) entities)
        {
            Index = index;
            Mask = mask;

            Types = types;
            Components = components;
            Tags = tags;
            Entities = entities;

            _minimum = Components.Select(type => type.Index).FirstOrDefault();
            _maximum = Components.Select(type => type.Index + 1).LastOrDefault();
            _stores = new Array[_maximum - _minimum];
            _handles = new (GCHandle handle, IntPtr address)[_maximum - _minimum];
            foreach (var type in Components) _stores[GetStoreIndex(type)] = Array.CreateInstance(type.Type, entities.items.Length);
        }

        ~Segment()
        {
            for (int i = 0; i < Components.Length; i++)
            {
                var index = GetStoreIndex(Components[i]);
                ref var pair = ref _handles[index];
                if (pair.handle.IsAllocated) pair.handle.Free();
            }
        }

        public Segment Clone()
        {
            var clone = new Segment(Index, Mask, Types, Components, Tags, Entities.Clone());
            if (Entities.count > 0)
            {
                for (int j = 0; j < Components.Length; j++)
                {
                    ref readonly var type = ref Components[j];
                    var index = GetStoreIndex(type);
                    var source = _stores[index];
                    var target = clone._stores[index];
                    Array.Copy(source, target, Entities.count);
                }
            }
            return clone;
        }

        /// <summary>
        /// Tries the get the component store of provided component <paramref name="type"/>.
        /// </summary>
        /// <param name="type">The component type.</param>
        /// <param name="store">The component store.</param>
        /// <returns>Returns <c>true</c> if a component store was found; otherwise, <c>false</c>.</returns>
        [ThreadSafe]
        public bool TryStore(in Metadata type, out Array store)
        {
            var index = GetStoreIndex(type);
            if (index >= 0 && index < _stores.Length && _stores[index] is Array array)
            {
                store = array;
                return true;
            }

            store = default;
            return false;
        }
        /// <summary>
        /// Gets the component store of provided component <paramref name="type"/>.
        /// If the store doesn't exist, an <see cref="IndexOutOfRangeException"/> may be thrown or a <c>null</c> will be returned.
        /// Use <see cref="TryStore(in Metadata, out Array)"/> if you are unsure if the store exists.
        /// </summary>
        /// <param name="type">The component type.</param>
        /// <returns>The component store.</returns>
        [ThreadSafe]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Array Store(in Metadata type) => _stores[GetStoreIndex(type)];
        /// <summary>
        /// Ensures that all component stores are at least of the same size as the <see cref="Entities"/> array.
        /// If a component store not large enough, it is resized.
        /// </summary>
        /// <returns>Returns <c>true</c> if a component store was resized; otherwise, <c>false</c>.</returns>
        public bool Ensure()
        {
            var resized = false;
            for (int i = 0; i < Components.Length; i++)
            {
                ref readonly var type = ref Components[i];
                var index = GetStoreIndex(type);
                ref var store = ref _stores[index];
                if (ArrayUtility.Ensure(ref store, type.Type, Entities.count))
                {
                    resized = true;
                    ref var pair = ref _handles[index];
                    if (pair.handle.IsAllocated)
                    {
                        pair.handle.Free();
                        pair = Fix(store);
                    }
                }
            }
            return resized;
        }

        public (Array store, IntPtr address) Fixed(in Metadata type)
        {
            var index = GetStoreIndex(type);
            var store = _stores[index];
            ref var pair = ref _handles[index];
            if (pair.handle.IsAllocated) return (store, pair.address);
            pair = Fix(store);
            return (store, pair.address);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [ThreadSafe]
        int GetStoreIndex(in Metadata metadata) => metadata.Index - _minimum;
    }
}
