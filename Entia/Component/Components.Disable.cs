using Entia.Modules.Component;
using System;

namespace Entia.Modules
{
    public sealed partial class Components
    {
        public bool Disable<T>(Entity entity) where T : IComponent =>
            ComponentUtility.Abstract<T>.TryConcrete(out var metadata) ? Disable(entity, metadata) :
            ComponentUtility.TryGetConcreteTypes<T>(out var types) && Disable(entity, types);

        public bool Disable(Entity entity, Type type) =>
            ComponentUtility.TryGetMetadata(type, false, out var metadata) ? Disable(entity, metadata) :
            ComponentUtility.TryGetConcreteTypes(type, out var types) && Disable(entity, types);

        public bool Disable(Entity entity)
        {
            ref var data = ref GetData(entity, out var success);
            return success && Disable(entity, ref data, GetTargetTypes(data));
        }

        bool Disable(Entity entity, in Metadata metadata)
        {
            ref var data = ref GetData(entity, out var success);
            return success && Disable(entity, ref data, metadata);
        }

        bool Disable(Entity entity, ref Data data, in Metadata metadata)
        {
            ref var slot = ref GetTransientSlot(entity, ref data, Resolutions.None);
            return slot.Resolution < Resolutions.Dispose && Disable(ref slot, metadata);
        }

        bool Disable(ref Slot slot, in Metadata metadata) =>
            TryGetDelegates(metadata, out var delegates) && Disable(ref slot, metadata, delegates);

        bool Disable(ref Slot slot, in Metadata metadata, in Delegates delegates)
        {
            if (Has(slot.Mask, metadata, delegates, States.All) && SetDisabled(ref slot, delegates))
            {
                delegates.OnDisable(slot.Entity);
                return true;
            }
            return false;
        }

        bool Disable(Entity entity, Metadata[] types)
        {
            ref var data = ref GetData(entity, out var success);
            return success && Disable(entity, ref data, types);
        }

        bool Disable(Entity entity, ref Data data, Metadata[] types)
        {
            ref var slot = ref GetTransientSlot(entity, ref data, Resolutions.None);
            var disabled = false;
            for (var i = 0; i < types.Length; i++) disabled |= Disable(ref slot, types[i]);
            return disabled;
        }
    }
}