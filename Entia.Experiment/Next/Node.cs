using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Plan
    {
        public static readonly Plan Empty = new(Cache.Empty<Action>(), Cache.Empty<Dependency>());
        public readonly Cache<Action[]> Runs;
        public readonly Cache<Dependency[]> Dependencies;
        public Plan(Cache<Action[]> runs, Cache<Dependency[]> dependencies) { Runs = runs; Dependencies = dependencies; }
    }

    public static partial class Node
    {
        public static readonly Nodes.INode Empty = All();

        public static Nodes.INode Lazy(Func<World, Nodes.INode> provide) => new Nodes.Lazy(provide);
        public static Nodes.INode Schedule(Func<World, Plan> schedule) => new Nodes.Scheduler(schedule);
        public static Nodes.INode All(params Nodes.INode[] nodes) => new Nodes.All(nodes);
        public static Nodes.INode All(this IEnumerable<Nodes.INode> nodes) => All(nodes.ToArray());
        public static Nodes.INode Synchronize() => Empty.Synchronous();
        public static Nodes.INode Synchronous(this Nodes.INode node) => node.Map(plan => new(
            plan.Runs.Change(runs => runs.Combine().Map(run => new[] { run }).OrEmpty()),
            plan.Dependencies.Change(dependencies => dependencies.Append(Dependency.Unknown))));

        public static Nodes.INode Map(this Nodes.INode node, Func<Plan, Plan> map) => new Nodes.Mapper(node, map);
        public static Nodes.INode Depend(this Nodes.INode node, params Dependency[] dependencies) =>
            node.Map(plan => new(plan.Runs, plan.Dependencies.Change(dependencies.Prepend)));
        public static Nodes.INode If(this Nodes.INode node, Func<bool> condition) =>
            node.Map(plan => new(plan.Runs.Update((runs, _) => condition() ? runs : Array.Empty<Action>()), plan.Dependencies));

        public delegate void RunR1M1EC1<TResource1, TMessage1, TComponent1>(ref TResource1 resource1, in TMessage1 message1, Entity entity, ref TComponent1 component1);
        public delegate void RunR1EC1<TResource1, TComponent1>(ref TResource1 resource1, Entity entity, ref TComponent1 component1);
        public delegate void RunEC2<TComponent1, TComponent2>(ref TComponent1 component1, in TComponent2 component2);
        public delegate void RunEC1<TComponent1>(Entity entity, ref TComponent1 component1);
        public delegate void RunE(Entity entity);

        public static Nodes.INode Run(params Action[] runs) =>
            Schedule(_ => new(Cache.Constant(runs), Cache.Empty<Dependency>()));

        // public static Nodes.INode Run<TResource1, TMessage1, TComponent1>(RunR1M1EC1<TResource1, TMessage1, TComponent1> run, Matcher? matcher = null) => Schedule(world =>
        // {
        //     var states = world.Segments((matcher ?? Matcher.True), world.Meta(typeof(TComponent1)));
        //     var runs = states.Runs((chunk, stores) =>
        //     {
        //         var entities = chunk.Entities;
        //         var store1 = (TComponent1[])chunk.Stores[stores[0]];
        //         return () => { for (int i = 0; i < chunk.Count; i++) run(entities[i], ref store1[i]); };
        //     });
        //     var dependencies = states.Dependencies(segment => new[]
        //     {
        //         new Dependency(Dependency.Kinds.Read, typeof(Entity), segment),
        //         new Dependency(Dependency.Kinds.Write, typeof(TComponent1), segment),
        //     });
        //     return new Plan(runs, dependencies);
        // });

        public static Nodes.INode Run(RunE run, Matcher? matcher = null) => Schedule(world =>
        {
            var segments = world.Segments(matcher ?? Matcher.True, Array.Empty<Meta>());
            var runs = segments.Runs((chunk, stores) =>
                () => { for (int i = 0; i < chunk.Count; i++) run(chunk.Entities[i]); });
            var dependencies = segments.Dependencies(segment => new[] { segment.Read<Entity>() });
            return new Plan(runs, dependencies);
        });

        public static Nodes.INode Run<TComponent1>(RunEC1<TComponent1> run, Matcher? matcher = null) => Schedule(world =>
        {
            var segments = world.Segments(matcher ?? Matcher.True, world.Meta(typeof(TComponent1)));
            var runs = segments.Runs((chunk, stores) =>
            {
                var entities = chunk.Entities;
                var store1 = (TComponent1[])chunk.Stores[stores[0]];
                return () => { for (int i = 0; i < chunk.Count; i++) run(entities[i], ref store1[i]); };
            });
            var dependencies = segments.Dependencies(segment => new[] { segment.Read<Entity>(), segment.Write<TComponent1>() });
            return new Plan(runs, dependencies);
        });

        public static Action Schedule(this Nodes.INode node, World world)
        {
            var plans = Plans(node, world);
            var shrinks = Shrink(plans);
            return () =>
            {
                var reschedule = false;
                foreach (var plan in shrinks)
                {
                    plan.Dependencies.Get(out var changed);
                    reschedule |= changed;
                    // When dependencies change, it is not safe to run in parallel until
                    // the reschedule happens.
                    Run(plan.Runs.Get(), changed);
                }
                if (reschedule) shrinks = Shrink(plans);
            };

            static void Run(Action[] runs, bool sequential)
            {
                if (sequential || runs.Length <= 8) foreach (var run in runs) run();
                else
                {
                    var buffer = Buffer.Get<BufferKey, Task>(runs.Length);
                    for (int i = 0; i < runs.Length; i++) buffer[i] = Task.Run(runs[i]);
                    for (int i = 0; i < runs.Length; i++) buffer[i].Wait();
                }
            }

            static IEnumerable<(int begin, int end)> Groups(Plan[] plans)
            {
                var conflicts = Conflicts(plans);
                var last = 0;
                // Groups must cover the full range of the runner array so empty groups must not
                // be filtered or this may cause problems when executing them.
                for (int i = 0; i < conflicts.Length; i++) if (conflicts[i]) yield return (last, last = i);
                yield return (last, conflicts.Length);
            }

            static Plan[] Shrink(Plan[] plans) => Groups(plans)
                .Select(pair => plans.Slice(pair.begin, pair.end - pair.begin))
                .Where(slice => slice.Count > 0)
                .Select(Merge)
                .ToArray();

            static Plan Merge(Slice<Plan> plans)
            {
                var runs = plans.Select(plan => plan.Runs.Change()).Any().Map(runs => runs.Flatten());
                var dependencies = plans.Select(plan => plan.Dependencies.Change()).Any().Map(dependencies => dependencies.Flatten());
                return new(runs, dependencies);
            }

            static bool[] Conflicts(Plan[] plans)
            {
                var previous = Array.Empty<Dependency>();
                var conflicts = new bool[plans.Length];
                for (int i = 0; i < plans.Length; i++)
                {
                    var plan = plans[i];
                    var current = plan.Dependencies.Get();
                    if (conflicts[i] = previous.Conflicts(current)) previous = current;
                    else previous = previous.Append(current);
                }
                return conflicts;
            }

            static Plan[] Plans(Nodes.INode node, World world) => node switch
            {
                Nodes.Lazy lazy => Plans(lazy.Provide(world), world),
                Nodes.Mapper mapper => Plans(mapper.Node, world).Select(mapper.Map),
                Nodes.All all => all.Nodes.Select(world, Plans).Flatten(),
                Nodes.Scheduler scheduler => new[] { scheduler.Schedule(world) },
                _ => Array.Empty<Plan>()
            };
        }

        public static Cache<Segment[]> Segments(this World world, Matcher matcher)
        {
            var index = 0u;
            return Cache.Create(Array.Empty<Segment>(), segments =>
            {
                var changed = false;
                while (index < world.Segments.Length)
                {
                    var segment = world.Segments[index++];
                    if (matcher.Match(segment, world))
                    {
                        changed = true;
                        segments = segments.Append(segment);
                    }
                }
                return changed ? segments : Option.None();
            });
        }

        static Cache<(Segment segment, int[] stores)[]> Segments(this World world, Matcher matcher, params Meta[] metas)
        {
            var index = 0u;
            return Cache.Create(Array.Empty<(Segment segment, int[] stores)>(), segments =>
            {
                var changed = false;
                while (index < world.Segments.Length)
                {
                    var segment = world.Segments[index++];
                    var stores = new int[metas.Length];
                    var all = true;
                    for (int i = 0; i < metas.Length; i++) all &= segment.TryIndex(metas[i], out stores[i]);
                    if (all && matcher.Match(segment, world))
                    {
                        changed = true;
                        segments = segments.Append((segment, stores));
                    }
                }
                return changed ? segments : Option.None();
            });
        }

        static Cache<Action[]> Runs(this Cache<(Segment segment, int[] stores)[]> segments, Func<Segment.Chunk, int[], Action> provide) =>
            segments.Update(Array.Empty<Action[]>(), (segments, runs) =>
            {
                var changed = false;
                if (segments.Length > runs.Length)
                {
                    changed = true;
                    runs = runs.Append(segments.Skip(runs.Length).Select(_ => Array.Empty<Action>()));
                }

                for (int i = 0; i < segments.Length; i++)
                {
                    var (segment, stores) = segments[i];
                    ref var run = ref runs[i];
                    if (segment.Chunks.Length > run.Length)
                    {
                        changed = true;
                        run = run.Append(segment.Chunks.Skip(run.Length).Select(stores, provide));
                    }
                }

                return changed ? runs : Option.None();
            }).Map(runs => runs.Flatten());

        static Cache<Dependency[]> Dependencies(this Cache<(Segment segment, int[] stores)[]> segments, Func<Segment, Dependency[]> provide) =>
            segments.Update(Array.Empty<Dependency[]>(), (pairs, dependencies) =>
            {
                var changed = false;
                if (pairs.Length > dependencies.Length)
                {
                    changed = true;
                    dependencies = pairs.Skip(dependencies.Length).Select(pair => provide(pair.segment));
                }
                return changed ? dependencies : Option.None();
            }).Map(dependencies => dependencies.Flatten());
    }

    namespace Nodes
    {
        public interface INode { }

        readonly struct Lazy : INode
        {
            public readonly Func<World, INode> Provide;
            public Lazy(Func<World, INode> provide) { Provide = provide; }
        }

        readonly struct All : INode
        {
            public readonly INode[] Nodes;
            public All(INode[] nodes) { Nodes = nodes; }
        }

        readonly struct Mapper : INode
        {
            public readonly INode Node;
            public readonly Func<Plan, Plan> Map;
            public Mapper(INode node, Func<Plan, Plan> map) { Node = node; Map = map; }
        }

        readonly struct Scheduler : INode
        {
            public readonly Func<World, Plan> Schedule;
            public Scheduler(Func<World, Plan> schedule) { Schedule = schedule; }
        }
    }
}