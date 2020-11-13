using Entia.Core;
using Entia.Core.Documentation;
using Entia.Modules.Component;
using Entia.Queryables;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Entia.Modules.Group
{
    /// <summary>
    /// Stores the entities and items that satisfy the query of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The query type.</typeparam>
    [ThreadSafe]
    public readonly struct Segment<T> : IEnumerable<Slice<T>.Read.Enumerator, T> where T : struct, IQueryable
    {
        /// <summary>
        /// Gets the entity count.
        /// </summary>
        /// <value>
        /// The count.
        /// </value>
        public int Count => _segment.Entities.count;

        /// <summary>
        /// Gets the selection of component types that are stored in this segment.
        /// </summary>
        /// <value>
        /// The types.
        /// </value>
        public Metadata[] Types => _segment.Types;
        /// <inheritdoc cref="Component.Segment.Entities"/>
        public Entity[] Entities => _segment.Entities.items;
        /// <summary>
        /// The items.
        /// </summary>
        public readonly T[] Items;

        readonly Segment _segment;

        /// <summary>
        /// Initializes a new instance of the <see cref="Segment{T}"/> struct.
        /// </summary>
        /// <param name="segment">The segment.</param>
        /// <param name="items">The items.</param>
        public Segment(Segment segment, T[] items)
        {
            _segment = segment;
            Items = items;
        }

        /// <inheritdoc cref="Segment.Store(in Metadata)"/>
        public TComponent[] Store<TComponent>() where TComponent : struct, IComponent =>
            ComponentUtility.Abstract<TComponent>.TryConcrete(out var metadata) ?
            _segment.Store(metadata) as TComponent[] : default;

        /// <inheritdoc cref="Segment.TryStore(in Metadata, out Array)"/>
        public bool TryStore<TComponent>(out TComponent[] store) where TComponent : struct, IComponent
        {
            if (ComponentUtility.Abstract<TComponent>.TryConcrete(out var metadata) && _segment.TryStore(metadata, out var array))
            {
                store = array as TComponent[];
                return store != null;
            }

            store = default;
            return false;
        }

        /// <inheritdoc cref="IEnumerable{T}.GetEnumerator"/>
        public Slice<T>.Read.Enumerator GetEnumerator() => Items.Slice(Count).GetEnumerator();
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}