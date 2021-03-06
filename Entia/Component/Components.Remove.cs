using Entia.Core;
using Entia.Modules.Component;
using System;

namespace Entia.Modules
{
    public sealed partial class Components
    {
        /// <summary>
        /// Removes components of type <typeparamref name="T"/> associated with the <paramref name="entity"/>.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="entity">The entity.</param>
        /// <param name="include">A filter that includes only the components that correspond to the provided states.</param>
        /// <returns>Returns <c>true</c> if the component was removed; otherwise, <c>false</c>.</returns>
        public bool Remove<T>(Entity entity, States include = States.All) where T : IComponent =>
            ComponentUtility.Abstract<T>.TryConcrete(out var metadata) ? Remove(entity, metadata, include) :
            ComponentUtility.TryGetConcrete<T>(out var mask, out var types) && Remove(entity, (mask, types), include);

        /// <summary>
        /// Removes components of provided <paramref name="type"/> associated with the <paramref name="entity"/>.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="type">The component type.</param>
        /// <param name="include">A filter that includes only the components that correspond to the provided states.</param>
        /// <returns>Returns <c>true</c> if the component was removed; otherwise, <c>false</c>.</returns>
        public bool Remove(Entity entity, Type type, States include = States.All) =>
            ComponentUtility.TryGetMetadata(type, false, out var metadata) ? Remove(entity, metadata, include) :
            ComponentUtility.TryGetConcrete(type, out var mask, out var types) && Remove(entity, (mask, types), include);

        bool Remove(Entity entity, in Metadata metadata, States include)
        {
            ref var data = ref GetData(entity, out var success);
            return success && Remove(entity, ref data, metadata, include);
        }

        bool Remove(Entity entity, ref Data data, in Metadata metadata, States include) =>
            TryGetDelegates(metadata, out var delegates) && Remove(entity, ref data, metadata, delegates, include);

        bool Remove(Entity entity, ref Data data, in Metadata metadata, in Delegates delegates, States include)
        {
            ref var slot = ref GetTransientSlot(entity, ref data, Resolutions.None);
            return Remove(ref slot, metadata, delegates, include);
        }

        bool Remove(Entity entity, in (BitMask mask, Metadata[] types) components, States include)
        {
            ref var data = ref GetData(entity, out var success);
            return success && Remove(entity, ref data, components, include);
        }

        bool Remove(Entity entity, ref Data data, in (BitMask mask, Metadata[] types) components, States include)
        {
            ref var slot = ref GetTransientSlot(entity, ref data, Resolutions.None);
            return Remove(ref slot, components, include);
        }

        bool Remove(ref Slot slot, in (BitMask mask, Metadata[] types) components, States include)
        {
            var removed = false;
            for (int i = 0; i < components.types.Length; i++)
            {
                ref readonly var metadata = ref components.types[i];
                removed |= TryGetDelegates(metadata, out var delegates) && Remove(ref slot, metadata, delegates, include);
            }
            return removed;
        }

        bool Remove(ref Slot slot, in Metadata metadata, States include) =>
            TryGetDelegates(metadata, out var delegates) && Remove(ref slot, metadata, delegates, include);

        bool Remove(ref Slot slot, in Metadata metadata, in Delegates delegates, States include)
        {
            if (Has(slot, metadata, delegates, include) && slot.Lock.Add(metadata.Index))
            {
                delegates.OnRemove(slot.Entity);
                RemoveDisabled(ref slot, delegates);
                SetResolution(ref slot.Resolution, Resolutions.Move);
                slot.Mask.Remove(metadata.Index);
                slot.Lock.Remove(metadata.Index);
                return true;
            }
            return false;
        }

        bool RemoveDisabled(ref Slot slot, in Delegates delegates)
        {
            if (delegates.IsDisabled.IsValueCreated &&
                delegates.IsDisabled.Value.IsValid &&
                slot.Mask.Remove(delegates.IsDisabled.Value.Index))
            {
                SetResolution(ref slot.Resolution, Resolutions.Move);
                return true;
            }
            return false;
        }
    }
}