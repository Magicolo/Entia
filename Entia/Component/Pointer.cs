using System;
using Entia.Core;
using Entia.Core.Documentation;
using Entia.Dependencies;
using Entia.Dependers;
using Entia.Queriers;
using Entia.Queryables;

namespace Entia.Modules.Component
{
    [Preserve, ThreadSafe]
    public readonly struct Pointer<T> : IQueryable where T : struct, IComponent
    {
        unsafe sealed class Querier : IQuerier
        {
            public bool TryQuery(in Context context, out Query.Query query)
            {
                if (ComponentUtility.Abstract<T>.TryConcrete(out var metadata))
                {
                    var segment = context.Segment;
                    var state = context.World.Components().State(segment.Mask, metadata);
                    if (context.Include.HasAny(state))
                    {
                        query = metadata.Kind == Metadata.Kinds.Tag ?
                            new Query.Query((pointer, _) =>
                            {
                                var target = (IntPtr*)pointer;
                                *target = UnsafeUtility.AsPointer(ref Dummy<T>.Value);
                            }, metadata) :
                            new Query.Query((pointer, index) =>
                            {
                                var store = segment.Fixed(metadata).store as T[];
                                var target = (IntPtr*)pointer;
                                *target = UnsafeUtility.AsPointer(ref store[index]);
                            }, metadata);
                        return true;
                    }
                }

                query = default;
                return false;
            }
        }

        [Implementation]
        static readonly Querier _querier = new Querier();
        [Implementation]
        static readonly IDepender _depender = Depender.From<T>(new Write(typeof(T)));
    }
}
