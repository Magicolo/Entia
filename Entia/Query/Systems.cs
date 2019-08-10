/* DO NOT MODIFY: The content of this file has been generated by the script 'Systems.csx'. */

using Entia.Schedulables;
using Entia.Core;
using Entia.Systems;
using System;
using Entia.Modules.Schedule;
using Entia.Modules.Query;
using Entia.Queryables;
using System.Collections.Generic;
using Entia.Dependencies;
using System.Linq;
using Entia.Dependency;

namespace Entia.Systems
{
    public interface IRunEach : ISystem, ISchedulable<Schedulers.RunEach>, IImplementation<Dependers.RunEach>
    {
        void Run(Entity entity);
    }
    public interface IRunEach<T> : ISystem, ISchedulable<Schedulers.RunEach<T>>, IImplementation<Dependers.RunEach<T>> where T : struct, IComponent
    {
        void Run(Entity entity, ref T component1);
    }
    public interface IRunEach<T1, T2> : ISystem, ISchedulable<Schedulers.RunEach<T1, T2>>, IImplementation<Dependers.RunEach<T1, T2>> where T1 : struct, IComponent where T2 : struct, IComponent
    {
        void Run(Entity entity, ref T1 component1, ref T2 component2);
    }
    public interface IRunEach<T1, T2, T3> : ISystem, ISchedulable<Schedulers.RunEach<T1, T2, T3>>, IImplementation<Dependers.RunEach<T1, T2, T3>> where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent
    {
        void Run(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3);
    }
    public interface IRunEach<T1, T2, T3, T4> : ISystem, ISchedulable<Schedulers.RunEach<T1, T2, T3, T4>>, IImplementation<Dependers.RunEach<T1, T2, T3, T4>> where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent
    {
        void Run(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4);
    }
    public interface IRunEach<T1, T2, T3, T4, T5> : ISystem, ISchedulable<Schedulers.RunEach<T1, T2, T3, T4, T5>>, IImplementation<Dependers.RunEach<T1, T2, T3, T4, T5>> where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent
    {
        void Run(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4, ref T5 component5);
    }
    public interface IRunEach<T1, T2, T3, T4, T5, T6> : ISystem, ISchedulable<Schedulers.RunEach<T1, T2, T3, T4, T5, T6>>, IImplementation<Dependers.RunEach<T1, T2, T3, T4, T5, T6>> where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent where T6 : struct, IComponent
    {
        void Run(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4, ref T5 component5, ref T6 component6);
    }
    public interface IRunEach<T1, T2, T3, T4, T5, T6, T7> : ISystem, ISchedulable<Schedulers.RunEach<T1, T2, T3, T4, T5, T6, T7>>, IImplementation<Dependers.RunEach<T1, T2, T3, T4, T5, T6, T7>> where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent where T6 : struct, IComponent where T7 : struct, IComponent
    {
        void Run(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4, ref T5 component5, ref T6 component6, ref T7 component7);
    }
}

namespace Entia.Schedulers
{
    public sealed class RunEach : Scheduler<IRunEach>
    {
        delegate void Run(Entity entity);

        public override Type[] Phases { get; } = new[] { typeof(Phases.Run) };

        public override Phase[] Schedule(IRunEach instance, Controller controller)
        {
            var world = controller.World;
            var run = new Run(instance.Run);
            var box = world.Segments<Entity>(run.Method);
            return new[]
            {
                Phase.From((in Phases.Run _) =>
                {
                    var segments = box.Value.Segments;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        var segment = segments[i];
                        var (entities, count) = segment.Entities;

                        for (int j = 0; j < count; j++) run(entities[j]);
                    }
                })
            };
        }
    }
    public sealed class RunEach<T> : Scheduler<IRunEach<T>> where T : struct, IComponent
    {
        delegate void Run(Entity entity, ref T component1);

        public override Type[] Phases { get; } = new[] { typeof(Phases.Run) };

        public override Phase[] Schedule(IRunEach<T> instance, Controller controller)
        {
            var world = controller.World;
            var run = new Run(instance.Run);
            var box = world.Segments<Write<T>>(run.Method);
            return new[]
            {
                Phase.From((in Phases.Run _) =>
                {
                    var segments = box.Value.Segments;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        var segment = segments[i];
                        var (entities, count) = segment.Entities;
                        var store1 = segment.Store<T>();
                        for (int j = 0; j < count; j++) run(entities[j], ref store1[j]);
                    }
                })
            };
        }
    }
    public sealed class RunEach<T1, T2> : Scheduler<IRunEach<T1, T2>> where T1 : struct, IComponent where T2 : struct, IComponent
    {
        delegate void Run(Entity entity, ref T1 component1, ref T2 component2);

        public override Type[] Phases { get; } = new[] { typeof(Phases.Run) };

        public override Phase[] Schedule(IRunEach<T1, T2> instance, Controller controller)
        {
            var world = controller.World;
            var run = new Run(instance.Run);
            var box = world.Segments<All<Write<T1>, Write<T2>>>(run.Method);
            return new[]
            {
                Phase.From((in Phases.Run _) =>
                {
                    var segments = box.Value.Segments;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        var segment = segments[i];
                        var (entities, count) = segment.Entities;
                        var store1 = segment.Store<T1>(); var store2 = segment.Store<T2>();
                        for (int j = 0; j < count; j++) run(entities[j], ref store1[j], ref store2[j]);
                    }
                })
            };
        }
    }
    public sealed class RunEach<T1, T2, T3> : Scheduler<IRunEach<T1, T2, T3>> where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent
    {
        delegate void Run(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3);

        public override Type[] Phases { get; } = new[] { typeof(Phases.Run) };

        public override Phase[] Schedule(IRunEach<T1, T2, T3> instance, Controller controller)
        {
            var world = controller.World;
            var run = new Run(instance.Run);
            var box = world.Segments<All<Write<T1>, Write<T2>, Write<T3>>>(run.Method);
            return new[]
            {
                Phase.From((in Phases.Run _) =>
                {
                    var segments = box.Value.Segments;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        var segment = segments[i];
                        var (entities, count) = segment.Entities;
                        var store1 = segment.Store<T1>(); var store2 = segment.Store<T2>(); var store3 = segment.Store<T3>();
                        for (int j = 0; j < count; j++) run(entities[j], ref store1[j], ref store2[j], ref store3[j]);
                    }
                })
            };
        }
    }
    public sealed class RunEach<T1, T2, T3, T4> : Scheduler<IRunEach<T1, T2, T3, T4>> where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent
    {
        delegate void Run(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4);

        public override Type[] Phases { get; } = new[] { typeof(Phases.Run) };

        public override Phase[] Schedule(IRunEach<T1, T2, T3, T4> instance, Controller controller)
        {
            var world = controller.World;
            var run = new Run(instance.Run);
            var box = world.Segments<All<Write<T1>, Write<T2>, Write<T3>, Write<T4>>>(run.Method);
            return new[]
            {
                Phase.From((in Phases.Run _) =>
                {
                    var segments = box.Value.Segments;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        var segment = segments[i];
                        var (entities, count) = segment.Entities;
                        var store1 = segment.Store<T1>(); var store2 = segment.Store<T2>(); var store3 = segment.Store<T3>(); var store4 = segment.Store<T4>();
                        for (int j = 0; j < count; j++) run(entities[j], ref store1[j], ref store2[j], ref store3[j], ref store4[j]);
                    }
                })
            };
        }
    }
    public sealed class RunEach<T1, T2, T3, T4, T5> : Scheduler<IRunEach<T1, T2, T3, T4, T5>> where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent
    {
        delegate void Run(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4, ref T5 component5);

        public override Type[] Phases { get; } = new[] { typeof(Phases.Run) };

        public override Phase[] Schedule(IRunEach<T1, T2, T3, T4, T5> instance, Controller controller)
        {
            var world = controller.World;
            var run = new Run(instance.Run);
            var box = world.Segments<All<Write<T1>, Write<T2>, Write<T3>, Write<T4>, Write<T5>>>(run.Method);
            return new[]
            {
                Phase.From((in Phases.Run _) =>
                {
                    var segments = box.Value.Segments;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        var segment = segments[i];
                        var (entities, count) = segment.Entities;
                        var store1 = segment.Store<T1>(); var store2 = segment.Store<T2>(); var store3 = segment.Store<T3>(); var store4 = segment.Store<T4>(); var store5 = segment.Store<T5>();
                        for (int j = 0; j < count; j++) run(entities[j], ref store1[j], ref store2[j], ref store3[j], ref store4[j], ref store5[j]);
                    }
                })
            };
        }
    }
    public sealed class RunEach<T1, T2, T3, T4, T5, T6> : Scheduler<IRunEach<T1, T2, T3, T4, T5, T6>> where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent where T6 : struct, IComponent
    {
        delegate void Run(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4, ref T5 component5, ref T6 component6);

        public override Type[] Phases { get; } = new[] { typeof(Phases.Run) };

        public override Phase[] Schedule(IRunEach<T1, T2, T3, T4, T5, T6> instance, Controller controller)
        {
            var world = controller.World;
            var run = new Run(instance.Run);
            var box = world.Segments<All<Write<T1>, Write<T2>, Write<T3>, Write<T4>, Write<T5>, Write<T6>>>(run.Method);
            return new[]
            {
                Phase.From((in Phases.Run _) =>
                {
                    var segments = box.Value.Segments;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        var segment = segments[i];
                        var (entities, count) = segment.Entities;
                        var store1 = segment.Store<T1>(); var store2 = segment.Store<T2>(); var store3 = segment.Store<T3>(); var store4 = segment.Store<T4>(); var store5 = segment.Store<T5>(); var store6 = segment.Store<T6>();
                        for (int j = 0; j < count; j++) run(entities[j], ref store1[j], ref store2[j], ref store3[j], ref store4[j], ref store5[j], ref store6[j]);
                    }
                })
            };
        }
    }
    public sealed class RunEach<T1, T2, T3, T4, T5, T6, T7> : Scheduler<IRunEach<T1, T2, T3, T4, T5, T6, T7>> where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent where T6 : struct, IComponent where T7 : struct, IComponent
    {
        delegate void Run(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4, ref T5 component5, ref T6 component6, ref T7 component7);

        public override Type[] Phases { get; } = new[] { typeof(Phases.Run) };

        public override Phase[] Schedule(IRunEach<T1, T2, T3, T4, T5, T6, T7> instance, Controller controller)
        {
            var world = controller.World;
            var run = new Run(instance.Run);
            var box = world.Segments<All<Write<T1>, Write<T2>, Write<T3>, Write<T4>, Write<T5>, Write<T6>, Write<T7>>>(run.Method);
            return new[]
            {
                Phase.From((in Phases.Run _) =>
                {
                    var segments = box.Value.Segments;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        var segment = segments[i];
                        var (entities, count) = segment.Entities;
                        var store1 = segment.Store<T1>(); var store2 = segment.Store<T2>(); var store3 = segment.Store<T3>(); var store4 = segment.Store<T4>(); var store5 = segment.Store<T5>(); var store6 = segment.Store<T6>(); var store7 = segment.Store<T7>();
                        for (int j = 0; j < count; j++) run(entities[j], ref store1[j], ref store2[j], ref store3[j], ref store4[j], ref store5[j], ref store6[j], ref store7[j]);
                    }
                })
            };
        }
    }
}

namespace Entia.Dependers
{
    public sealed class RunEach : IDepender
    {
        public IEnumerable<IDependency> Depend(in Context context) => Enumerable.Empty<IDependency>()

            .Prepend(new Read(typeof(Entity)));
    }
    public sealed class RunEach<T> : IDepender where T : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Context context) => Enumerable.Empty<IDependency>()
            .Concat(context.Dependencies<Write<T>>())
            .Prepend(new Read(typeof(Entity)));
    }
    public sealed class RunEach<T1, T2> : IDepender where T1 : struct, IComponent where T2 : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Context context) => Enumerable.Empty<IDependency>()
            .Concat(context.Dependencies<Write<T1>>())
            .Concat(context.Dependencies<Write<T2>>())
            .Prepend(new Read(typeof(Entity)));
    }
    public sealed class RunEach<T1, T2, T3> : IDepender where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Context context) => Enumerable.Empty<IDependency>()
            .Concat(context.Dependencies<Write<T1>>())
            .Concat(context.Dependencies<Write<T2>>())
            .Concat(context.Dependencies<Write<T3>>())
            .Prepend(new Read(typeof(Entity)));
    }
    public sealed class RunEach<T1, T2, T3, T4> : IDepender where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Context context) => Enumerable.Empty<IDependency>()
            .Concat(context.Dependencies<Write<T1>>())
            .Concat(context.Dependencies<Write<T2>>())
            .Concat(context.Dependencies<Write<T3>>())
            .Concat(context.Dependencies<Write<T4>>())
            .Prepend(new Read(typeof(Entity)));
    }
    public sealed class RunEach<T1, T2, T3, T4, T5> : IDepender where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Context context) => Enumerable.Empty<IDependency>()
            .Concat(context.Dependencies<Write<T1>>())
            .Concat(context.Dependencies<Write<T2>>())
            .Concat(context.Dependencies<Write<T3>>())
            .Concat(context.Dependencies<Write<T4>>())
            .Concat(context.Dependencies<Write<T5>>())
            .Prepend(new Read(typeof(Entity)));
    }
    public sealed class RunEach<T1, T2, T3, T4, T5, T6> : IDepender where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent where T6 : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Context context) => Enumerable.Empty<IDependency>()
            .Concat(context.Dependencies<Write<T1>>())
            .Concat(context.Dependencies<Write<T2>>())
            .Concat(context.Dependencies<Write<T3>>())
            .Concat(context.Dependencies<Write<T4>>())
            .Concat(context.Dependencies<Write<T5>>())
            .Concat(context.Dependencies<Write<T6>>())
            .Prepend(new Read(typeof(Entity)));
    }
    public sealed class RunEach<T1, T2, T3, T4, T5, T6, T7> : IDepender where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent where T4 : struct, IComponent where T5 : struct, IComponent where T6 : struct, IComponent where T7 : struct, IComponent
    {
        public IEnumerable<IDependency> Depend(in Context context) => Enumerable.Empty<IDependency>()
            .Concat(context.Dependencies<Write<T1>>())
            .Concat(context.Dependencies<Write<T2>>())
            .Concat(context.Dependencies<Write<T3>>())
            .Concat(context.Dependencies<Write<T4>>())
            .Concat(context.Dependencies<Write<T5>>())
            .Concat(context.Dependencies<Write<T6>>())
            .Concat(context.Dependencies<Write<T7>>())
            .Prepend(new Read(typeof(Entity)));
    }
}