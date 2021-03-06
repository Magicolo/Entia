using Entia.Core;
using Entia.Core.Documentation;
using Entia.Modules.Component;
using System;

namespace Entia.Modules
{
    public sealed partial class Components
    {
        [ThreadSafe]
        static Array GetTagStore(in Metadata metadata)
        {
            // NOTE: while this is not strictly thread safe, the worst case senario is the generation of a bit of garbage
            ArrayUtility.Ensure(ref _tags, metadata.Index + 1);
            return _tags[metadata.Index] ?? (_tags[metadata.Index] = Array.CreateInstance(metadata.Type, 1));
        }

        /// <summary>
        /// Tries to get the component store of type <typeparamref name="T"/> associated with the <paramref name="entity"/>.
        /// </summary>
        /// <typeparam name="T">The concrete component type.</typeparam>
        /// <param name="entity">The entity.</param>
        /// <param name="store">The store.</param>
        /// <param name="index">The index in the store where the component is.</param>
        /// <param name="include">A filter that includes only the components that correspond to the provided states.</param>
        /// <returns>Returns <c>true</c> if the store was found; otherwise, <c>false</c>.</returns>
        [ThreadSafe]
        public bool TryStore<T>(Entity entity, out T[] store, out int index, States include = States.All) where T : struct, IComponent
        {
            ref readonly var data = ref GetData(entity, out var success);
            if (success) return TryGetStore(data, ComponentUtility.Concrete<T>.Data, include, out store, out index);
            store = default;
            index = default;
            return false;
        }

        /// <summary>
        /// Tries to get the component store of provided <paramref name="type"/> associated with the <paramref name="entity"/>.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="type">The concrete component type.</param>
        /// <param name="store">The store.</param>
        /// <param name="index">The index in the store where the component is.</param>
        /// <param name="include">A filter that includes only the components that correspond to the provided states.</param>
        /// <returns>Returns <c>true</c> if the store was found; otherwise, <c>false</c>.</returns>
        [ThreadSafe]
        public bool TryStore(Entity entity, Type type, out Array store, out int index, States include = States.All)
        {
            ref readonly var data = ref GetData(entity, out var success);
            if (success && ComponentUtility.TryGetMetadata(type, false, out var metadata))
                return TryGetStore(data, metadata, include, out store, out index);

            store = default;
            index = default;
            return false;
        }

        [ThreadSafe]
        bool TryGetStore<T>(in Data data, in Metadata metadata, States include, out T[] store, out int adjusted) where T : struct, IComponent
        {
            if (TryGetStore(data, metadata, include, out var array, out adjusted))
            {
                store = array as T[];
                return store != null;
            }

            store = default;
            return false;
        }

        [ThreadSafe]
        bool TryGetStore(in Data data, in Metadata metadata, States include, out Array store, out int adjusted)
        {
            if (metadata.Kind == Metadata.Kinds.Tag)
            {
                adjusted = 0;
                store = GetTagStore(metadata);
                return Has(data, metadata, include);
            }
            else if (data.Segment.TryStore(metadata, out store))
            {
                adjusted = data.Index;
                return Has(data, metadata, include);
            }
            else if (data.Transient is int transient)
            {
                // NOTE: prioritize the segment store
                // NOTE: if the slot has the component, then the store must not be null
                return
                    TryGetTransientStore(transient, metadata, out store, out adjusted) &&
                    Has(_slots.items[transient], metadata, include);
            }

            adjusted = default;
            return false;
        }

        Array GetStore(Entity entity, ref Data data, in Metadata metadata, out int adjusted)
        {
            if (metadata.Kind == Metadata.Kinds.Tag)
            {
                adjusted = 0;
                return GetTagStore(metadata);
            }
            else if (data.Segment.TryStore(metadata, out var store))
            {
                adjusted = data.Index;
                return store;
            }
            else if (data.Transient is int transient) return GetTransientStore(transient, metadata, out adjusted);
            else
            {
                data.Transient = transient = ReserveTransient(entity, Resolutions.None, data.Segment.Mask);
                return GetTransientStore(transient, metadata, out adjusted);
            }
        }

        T[] GetStore<T>(Entity entity, ref Data data, in Metadata metadata, out int adjusted) where T : struct, IComponent =>
            GetStore(entity, ref data, metadata, out adjusted) as T[];
    }
}