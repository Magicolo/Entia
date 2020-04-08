/* DO NOT MODIFY: The content of this file has been generated by the script 'System.ReactEach.csx'. */

using Entia.Core;
using Entia.Systems;
using System;
using Entia.Modules.Schedule;
using Entia.Modules.Query;
using Entia.Queryables;
using System.Collections.Generic;
using Entia.Dependencies;
using System.Linq;

namespace Entia.Systems
{
    public interface IReactEach<TMessage> : ISystem, IImplementation<Schedulers.ReactEach<TMessage>>, IImplementation<Dependers.ReactEach<TMessage>> where TMessage : struct, IMessage
    {
        void React(in TMessage message, Entity entity);
    }
    public interface IReactEach<TMessage, T> : ISystem, IImplementation<Schedulers.ReactEach<TMessage, T>>, IImplementation<Dependers.ReactEach<TMessage, T>> where TMessage : struct, IMessage where T : struct, IComponent
    {
        void React(in TMessage message, Entity entity, ref T component1);
    }
    public interface IReactEach<TMessage, T1, T2> : ISystem, IImplementation<Schedulers.ReactEach<TMessage, T1, T2>>, IImplementation<Dependers.ReactEach<TMessage, T1, T2>> where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent
    {
        void React(in TMessage message, Entity entity, ref T1 component1, ref T2 component2);
    }
    public interface IReactEach<TMessage, T1, T2, T3> : ISystem, IImplementation<Schedulers.ReactEach<TMessage, T1, T2, T3>>, IImplementation<Dependers.ReactEach<TMessage, T1, T2, T3>> where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent
    {
        void React(in TMessage message, Entity entity, ref T1 component1, ref T2 component2, ref T3 component3);
    }
    public interface IReactEach<TMessage, T1, T2, T3, T4> : ISystem, IImplementation<Schedulers.ReactEach<TMessage, T1, T2, T3, T4>>, IImplementation<Dependers.ReactEach<TMessage, T1, T2, T3, T4>> where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent
    {
        void React(in TMessage message, Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4);
    }
    public interface IReactEach<TMessage, T1, T2, T3, T4, T5> : ISystem, IImplementation<Schedulers.ReactEach<TMessage, T1, T2, T3, T4, T5>>, IImplementation<Dependers.ReactEach<TMessage, T1, T2, T3, T4, T5>> where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent
    {
        void React(in TMessage message, Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4, ref T5 component5);
    }
    public interface IReactEach<TMessage, T1, T2, T3, T4, T5, T6> : ISystem, IImplementation<Schedulers.ReactEach<TMessage, T1, T2, T3, T4, T5, T6>>, IImplementation<Dependers.ReactEach<TMessage, T1, T2, T3, T4, T5, T6>> where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent where T6 : struct, IComponent
    {
        void React(in TMessage message, Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4, ref T5 component5, ref T6 component6);
    }
}

namespace Entia.Schedulers
{
    public sealed class ReactEach<TMessage> : Scheduler<IReactEach<TMessage>> where TMessage : struct, IMessage
    {
        delegate void Run(in TMessage message, Entity entity);

        public override Type[] Phases => React.Phases<TMessage>();

        public override Phase[] Schedule(IReactEach<TMessage> instance, in Schedule.Context context)
        {
            var world = context.World;
            var react = new Run(instance.React);
            var box = world.Segments<Entity>(react.Method);
            return React.Schedule<TMessage>(Phase.From((in Phases.React<TMessage> phase) =>
            {
                var segments = box.Value.Segments;
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    var (entities, count) = segment.Entities;

                    for (int j = 0; j < count; j++) react(phase.Message, entities[j]);
                }
            }), context.Controller, context.World);
        }
    }
    public sealed class ReactEach<TMessage, T> : Scheduler<IReactEach<TMessage, T>> where TMessage : struct, IMessage where T : struct, IComponent
    {
        delegate void Run(in TMessage message, Entity entity, ref T component1);

        public override Type[] Phases => React.Phases<TMessage>();

        public override Phase[] Schedule(IReactEach<TMessage, T> instance, in Schedule.Context context)
        {
            var world = context.World;
            var react = new Run(instance.React);
            var box = world.Segments<Write<T>>(react.Method);
            return React.Schedule<TMessage>(Phase.From((in Phases.React<TMessage> phase) =>
            {
                var segments = box.Value.Segments;
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    var (entities, count) = segment.Entities;
                    var store1 = segment.Store<T>();
                    for (int j = 0; j < count; j++) react(phase.Message, entities[j], ref store1[j]);
                }
            }), context.Controller, context.World);
        }
    }
    public sealed class ReactEach<TMessage, T1, T2> : Scheduler<IReactEach<TMessage, T1, T2>> where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent
    {
        delegate void Run(in TMessage message, Entity entity, ref T1 component1, ref T2 component2);

        public override Type[] Phases => React.Phases<TMessage>();

        public override Phase[] Schedule(IReactEach<TMessage, T1, T2> instance, in Schedule.Context context)
        {
            var world = context.World;
            var react = new Run(instance.React);
            var box = world.Segments<All<Write<T1>, Write<T2>>>(react.Method);
            return React.Schedule<TMessage>(Phase.From((in Phases.React<TMessage> phase) =>
            {
                var segments = box.Value.Segments;
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    var (entities, count) = segment.Entities;
                    var store1 = segment.Store<T1>(); var store2 = segment.Store<T2>();
                    for (int j = 0; j < count; j++) react(phase.Message, entities[j], ref store1[j], ref store2[j]);
                }
            }), context.Controller, context.World);
        }
    }
    public sealed class ReactEach<TMessage, T1, T2, T3> : Scheduler<IReactEach<TMessage, T1, T2, T3>> where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent
    {
        delegate void Run(in TMessage message, Entity entity, ref T1 component1, ref T2 component2, ref T3 component3);

        public override Type[] Phases => React.Phases<TMessage>();

        public override Phase[] Schedule(IReactEach<TMessage, T1, T2, T3> instance, in Schedule.Context context)
        {
            var world = context.World;
            var react = new Run(instance.React);
            var box = world.Segments<All<Write<T1>, Write<T2>, Write<T3>>>(react.Method);
            return React.Schedule<TMessage>(Phase.From((in Phases.React<TMessage> phase) =>
            {
                var segments = box.Value.Segments;
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    var (entities, count) = segment.Entities;
                    var store1 = segment.Store<T1>(); var store2 = segment.Store<T2>(); var store3 = segment.Store<T3>();
                    for (int j = 0; j < count; j++) react(phase.Message, entities[j], ref store1[j], ref store2[j], ref store3[j]);
                }
            }), context.Controller, context.World);
        }
    }
    public sealed class ReactEach<TMessage, T1, T2, T3, T4> : Scheduler<IReactEach<TMessage, T1, T2, T3, T4>> where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent
    {
        delegate void Run(in TMessage message, Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4);

        public override Type[] Phases => React.Phases<TMessage>();

        public override Phase[] Schedule(IReactEach<TMessage, T1, T2, T3, T4> instance, in Schedule.Context context)
        {
            var world = context.World;
            var react = new Run(instance.React);
            var box = world.Segments<All<Write<T1>, Write<T2>, Write<T3>, Write<T4>>>(react.Method);
            return React.Schedule<TMessage>(Phase.From((in Phases.React<TMessage> phase) =>
            {
                var segments = box.Value.Segments;
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    var (entities, count) = segment.Entities;
                    var store1 = segment.Store<T1>(); var store2 = segment.Store<T2>(); var store3 = segment.Store<T3>(); var store4 = segment.Store<T4>();
                    for (int j = 0; j < count; j++) react(phase.Message, entities[j], ref store1[j], ref store2[j], ref store3[j], ref store4[j]);
                }
            }), context.Controller, context.World);
        }
    }
    public sealed class ReactEach<TMessage, T1, T2, T3, T4, T5> : Scheduler<IReactEach<TMessage, T1, T2, T3, T4, T5>> where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent
    {
        delegate void Run(in TMessage message, Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4, ref T5 component5);

        public override Type[] Phases => React.Phases<TMessage>();

        public override Phase[] Schedule(IReactEach<TMessage, T1, T2, T3, T4, T5> instance, in Schedule.Context context)
        {
            var world = context.World;
            var react = new Run(instance.React);
            var box = world.Segments<All<Write<T1>, Write<T2>, Write<T3>, Write<T4>, Write<T5>>>(react.Method);
            return React.Schedule<TMessage>(Phase.From((in Phases.React<TMessage> phase) =>
            {
                var segments = box.Value.Segments;
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    var (entities, count) = segment.Entities;
                    var store1 = segment.Store<T1>(); var store2 = segment.Store<T2>(); var store3 = segment.Store<T3>(); var store4 = segment.Store<T4>(); var store5 = segment.Store<T5>();
                    for (int j = 0; j < count; j++) react(phase.Message, entities[j], ref store1[j], ref store2[j], ref store3[j], ref store4[j], ref store5[j]);
                }
            }), context.Controller, context.World);
        }
    }
    public sealed class ReactEach<TMessage, T1, T2, T3, T4, T5, T6> : Scheduler<IReactEach<TMessage, T1, T2, T3, T4, T5, T6>> where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent where T6 : struct, IComponent
    {
        delegate void Run(in TMessage message, Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4, ref T5 component5, ref T6 component6);

        public override Type[] Phases => React.Phases<TMessage>();

        public override Phase[] Schedule(IReactEach<TMessage, T1, T2, T3, T4, T5, T6> instance, in Schedule.Context context)
        {
            var world = context.World;
            var react = new Run(instance.React);
            var box = world.Segments<All<Write<T1>, Write<T2>, Write<T3>, Write<T4>, Write<T5>, Write<T6>>>(react.Method);
            return React.Schedule<TMessage>(Phase.From((in Phases.React<TMessage> phase) =>
            {
                var segments = box.Value.Segments;
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    var (entities, count) = segment.Entities;
                    var store1 = segment.Store<T1>(); var store2 = segment.Store<T2>(); var store3 = segment.Store<T3>(); var store4 = segment.Store<T4>(); var store5 = segment.Store<T5>(); var store6 = segment.Store<T6>();
                    for (int j = 0; j < count; j++) react(phase.Message, entities[j], ref store1[j], ref store2[j], ref store3[j], ref store4[j], ref store5[j], ref store6[j]);
                }
            }), context.Controller, context.World);
        }
    }
}

namespace Entia.Dependers
{
    public sealed class ReactEach<TMessage> : IDepender where TMessage : struct, IMessage
    {
        public IEnumerable<IDependency> Depend(in Dependency.Context context) => new IDependency[] { new Read(typeof(Entity)), new React(typeof(TMessage)) }
            ;
    }
    public sealed class ReactEach<TMessage, T> : IDepender where TMessage : struct, IMessage where T : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Dependency.Context context) => new IDependency[] { new Read(typeof(Entity)), new React(typeof(TMessage)) }
            .Concat(context.Dependencies<Write<T>>());
    }
    public sealed class ReactEach<TMessage, T1, T2> : IDepender where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Dependency.Context context) => new IDependency[] { new Read(typeof(Entity)), new React(typeof(TMessage)) }
            .Concat(context.Dependencies<Write<T1>>())
            .Concat(context.Dependencies<Write<T2>>());
    }
    public sealed class ReactEach<TMessage, T1, T2, T3> : IDepender where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Dependency.Context context) => new IDependency[] { new Read(typeof(Entity)), new React(typeof(TMessage)) }
            .Concat(context.Dependencies<Write<T1>>())
            .Concat(context.Dependencies<Write<T2>>())
            .Concat(context.Dependencies<Write<T3>>());
    }
    public sealed class ReactEach<TMessage, T1, T2, T3, T4> : IDepender where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Dependency.Context context) => new IDependency[] { new Read(typeof(Entity)), new React(typeof(TMessage)) }
            .Concat(context.Dependencies<Write<T1>>())
            .Concat(context.Dependencies<Write<T2>>())
            .Concat(context.Dependencies<Write<T3>>())
            .Concat(context.Dependencies<Write<T4>>());
    }
    public sealed class ReactEach<TMessage, T1, T2, T3, T4, T5> : IDepender where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Dependency.Context context) => new IDependency[] { new Read(typeof(Entity)), new React(typeof(TMessage)) }
            .Concat(context.Dependencies<Write<T1>>())
            .Concat(context.Dependencies<Write<T2>>())
            .Concat(context.Dependencies<Write<T3>>())
            .Concat(context.Dependencies<Write<T4>>())
            .Concat(context.Dependencies<Write<T5>>());
    }
    public sealed class ReactEach<TMessage, T1, T2, T3, T4, T5, T6> : IDepender where TMessage : struct, IMessage where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent where T6 : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Dependency.Context context) => new IDependency[] { new Read(typeof(Entity)), new React(typeof(TMessage)) }
            .Concat(context.Dependencies<Write<T1>>())
            .Concat(context.Dependencies<Write<T2>>())
            .Concat(context.Dependencies<Write<T3>>())
            .Concat(context.Dependencies<Write<T4>>())
            .Concat(context.Dependencies<Write<T5>>())
            .Concat(context.Dependencies<Write<T6>>());
    }
}