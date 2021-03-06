using Entia.Core;
using Entia.Core.Documentation;
using Entia.Modules.Component;
using System;

namespace Entia.Modules
{
    public sealed partial class Components
    {
        [ThreadSafe]
        public States State<T>(Entity entity) where T : IComponent =>
            ComponentUtility.Abstract<T>.TryConcrete(out var metadata) ? State(entity, metadata) :
            ComponentUtility.TryGetConcreteTypes<T>(out var types) ? State(entity, types) :
            States.None;

        [ThreadSafe]
        public States State(Entity entity, Type type) =>
            ComponentUtility.TryGetMetadata(type, false, out var metadata) ? State(entity, metadata) :
            ComponentUtility.TryGetConcreteTypes(type, out var types) ? State(entity, types) :
            States.None;

        [ThreadSafe]
        public States State(Entity entity)
        {
            ref readonly var data = ref GetData(entity, out var success);
            return success ? State(data, GetTargetTypes(data)) : States.None;
        }

        [ThreadSafe]
        States State(Entity entity, in Metadata metadata)
        {
            ref readonly var data = ref GetData(entity, out var success);
            return success ? State(data, metadata) : States.None;
        }

        [ThreadSafe]
        States State(Entity entity, Metadata[] types)
        {
            ref readonly var data = ref GetData(entity, out var success);
            return success ? State(data, types) : States.None;
        }

        [ThreadSafe]
        States State(in Data data, Metadata[] types)
        {
            var state = States.None;
            for (int i = 0; i < types.Length; i++) state |= State(data, types[i]);
            return state;
        }

        [ThreadSafe]
        States State(in Data data, in Metadata metadata) => data.Transient is int transient ?
            State(_slots.items[transient], metadata) :
            State(data.Segment.Mask, metadata);

        [ThreadSafe]
        States State(in Slot slot, in Metadata metadata) =>
            slot.Resolution == Resolutions.Dispose ? States.None : State(slot.Mask, metadata);

        [ThreadSafe]
        internal States State(BitMask mask, in Metadata metadata) =>
            TryGetDelegates(metadata, out var delegates) ? State(mask, metadata, delegates) : States.None;

        [ThreadSafe]
        States State(BitMask mask, in Metadata metadata, in Delegates delegates) => mask.Has(metadata.Index) ?
            IsDisabled(mask, delegates) ? States.Disabled : States.Enabled :
            States.None;

        [ThreadSafe]
        bool IsDisabled(BitMask mask, in Delegates delegates) =>
            !delegates.Enabled && delegates.IsDisabled.IsValueCreated && IsDisabled(mask, delegates.IsDisabled.Value);

        [ThreadSafe]
        bool IsDisabled(BitMask mask, in Metadata disabled) => disabled.IsValid && mask.Has(disabled.Index);
    }
}