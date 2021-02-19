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
            plan.Runs.Map(runs => new[] { runs.Combine().Or(() => { }) }),
            plan.Dependencies.Map(dependencies => dependencies.Append(Dependency.Unknown))));

        public static Nodes.INode Map(this Nodes.INode node, Func<Plan, Plan> map) => new Nodes.Mapper(node, map);
        public static Nodes.INode Depend(this Nodes.INode node, params Dependency[] dependencies) =>
            node.Map(plan => new(plan.Runs, plan.Dependencies.Map(dependencies.Prepend)));
        public static Nodes.INode If(this Nodes.INode node, Func<bool> condition) =>
            node.Map(plan => new(plan.Runs.Change(runs => condition() ? runs : Array.Empty<Action>()), plan.Dependencies));

        public delegate void RunR1M1EC1<TResource1, TMessage1, TComponent1>(ref TResource1 resource1, in TMessage1 message1, Entity entity, ref TComponent1 component1);
        public delegate void RunR1EC1<TResource1, TComponent1>(ref TResource1 resource1, Entity entity, ref TComponent1 component1);
        public delegate void RunEC2<TComponent1, TComponent2>(ref TComponent1 component1, in TComponent2 component2);
        public delegate void RunEC1<TComponent1>(Entity entity, ref TComponent1 component1);

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
                else runs.Select(Task.Run).Iterate(static task => task.Wait());
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
                var runs = plans
                    .Select(static plan => plan.Runs.Change())
                    .Any()
                    .Map(static runs => runs.Flatten());
                var dependencies = plans
                    .Select(static plan => plan.Dependencies.Change())
                    .Any()
                    .Map(static dependencies => dependencies.Flatten());
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

        public static Cache<Segment[]> Segments(this World world, Matcher matcher) =>
            Cache.Change((index: 0u, segments: Array.Empty<Segment>()), state =>
            {
                while (state.index < world.Segments.Length)
                {
                    var segment = world.Segments[state.index++];
                    if (matcher.Match(segment, world)) state.segments = state.segments.Append(segment);
                }
                return state;
            }).Map(state => state.segments);

        static Cache<(Segment segment, int[] stores)[]> Segments(this World world, Matcher matcher, params Meta[] metas)
        {
            var index = 0u;
            return Cache.Change(Array.Empty<(Segment segment, int[] stores)>(), segments =>
            {
                while (index < world.Segments.Length)
                {
                    var segment = world.Segments[index++];
                    var stores = new int[metas.Length];
                    var all = true;
                    for (int i = 0; i < metas.Length; i++) all &= segment.TryIndex(metas[i], out stores[i]);
                    if (all && matcher.Match(segment, world)) segments = segments.Append((segment, stores));
                }
                return segments;
            });
        }

        static Cache<Action[]> Runs(this Cache<(Segment segment, int[] stores)[]> segments, Func<Segment.Chunk, int[], Action> provide) =>
            segments.Change(Array.Empty<Action[]>(), (segments, runs) =>
            {
                var changed = segments.Length > runs.Length;
                runs = runs.Append(segments.Skip(runs.Length).Select(_ => Array.Empty<Action>()));
                for (int i = 0; i < segments.Length; i++)
                {
                    var (segment, stores) = segments[i];
                    ref var run = ref runs[i];
                    changed |= segment.Chunks.Length > run.Length;
                    run = run.Append(segment.Chunks.Skip(run.Length).Select(stores, provide));
                }
                return changed ? runs : Option.None();
            }).Map(runs => runs.Flatten());

        static Cache<Dependency[]> Dependencies(this Cache<(Segment segment, int[] stores)[]> segments, Func<Segment, Dependency[]> dependencies) =>
            segments.Change(segments => segments.Select(segment => dependencies(segment.segment)).Flatten());
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

    // public static partial class Node
    // {
    // public static readonly Nodes.INode Empty = Schedule(_ => () => Nodes.Runner.Empty);

    // public static Nodes.INode Lazy(Func<World, Nodes.INode> provide) => new Nodes.Lazy(provide);
    // public static Nodes.INode Schedule(Nodes.Schedule schedule) => new Nodes.Scheduler(schedule);
    // public static Nodes.INode All(params Nodes.INode[] nodes) => new Nodes.All(nodes);
    // public static Nodes.INode All(this IEnumerable<Nodes.INode> nodes) => All(nodes.ToArray());
    // public static Nodes.INode Map(this Nodes.INode node, Func<Nodes.Runner, Nodes.Runner> map, Func<bool> change = null) => new Nodes.Mapper(node, map, change ?? new(() => false));
    // public static Nodes.INode Synchronize() => Empty.Synchronous();
    // public static Nodes.INode Synchronous(this Nodes.INode node) =>
    //     node.Map(runner => new(new[] { runner.Runs.Combine().Or(() => { }) }, runner.Dependencies.Append(Dependency.Unknown)));
    // public static Nodes.INode If(this Nodes.INode node, Func<bool> condition) =>
    //     node.Map(runner => runner.With(runner.Runs.Select(run => new Action(() => { if (condition()) run(); }))));

    // public delegate void RunR1EC1<TResource1, TComponent1>(ref TResource1 resource1, Entity entity, ref TComponent1 component1);
    // public delegate void RunEC2<TComponent1, TComponent2>(ref TComponent1 component1, in TComponent2 component2);
    // public delegate void RunEC1<TComponent1>(Entity entity, ref TComponent1 component1);

    // public static Action Schedule(this Nodes.INode node, World world)
    // {
    //     var prepares = Prepare(node, world);
    //     var runners = prepares.Select(prepare => prepare());
    //     var groups = Groups(runners);
    //     return () =>
    //     {
    //         var reschedule = false;
    //         for (int i = 0; i < groups.Length; i++)
    //         {
    //             var (runner, begin, end) = groups[i];
    //             var changed = (runs: false, dependencies: false);
    //             for (int j = begin; j < end; j++)
    //             {
    //                 ref var previous = ref runners[j];
    //                 var current = prepares[j]();
    //                 changed.runs |= previous.Runs != current.Runs;
    //                 changed.dependencies |= previous.Dependencies != current.Dependencies;
    //                 previous = current;
    //             }

    //             reschedule |= changed.dependencies;
    //             // Since dependencies have changed, it is not safe to run the group runners in
    //             // parallel until the reschedule happens.
    //             if (changed.dependencies) for (int j = begin; j < end; j++) runners[j].Run();
    //             else if (changed.runs) (groups[i].runner = All(runners, begin, end)).Run();
    //             else runner.Run();
    //         }
    //         if (reschedule) groups = Groups(runners);
    //     };

    //     static Nodes.Prepare[] Prepare(Nodes.INode node, World world)
    //     {
    //         return Interpret(Resolve(node));

    //         Nodes.INode Resolve(Nodes.INode node) => node switch
    //         {
    //             Nodes.Lazy lazy => Resolve(lazy.Provide(world)),
    //             Nodes.Mapper map => Resolve(map.Node) switch
    //             {
    //                 Nodes.Mapper inner => inner.Node.Map(
    //                     runner => map.Map(inner.Map(runner)),
    //                     () => map.Change() || inner.Change()),
    //                 Nodes.INode outer => outer.Map(map.Map, map.Change)
    //             },
    //             Nodes.All all => all.Nodes
    //                 .Select(Resolve)
    //                 .Select(static node => node is Nodes.All inner ? inner.Nodes : new[] { node })
    //                 .Flatten()
    //                 .All(),
    //             _ => node
    //         };

    //         Nodes.Prepare[] Interpret(Nodes.INode node) => node switch
    //         {
    //             Nodes.Mapper map => Interpret(map.Node).Select(prepare => Map(prepare, map.Map, map.Change)),
    //             Nodes.All all => all.Nodes.Select(Interpret).Flatten(),
    //             Nodes.Scheduler system => new[] { system.Schedule(world) },
    //             _ => Array.Empty<Nodes.Prepare>()
    //         };

    //         Nodes.Prepare Map(Nodes.Prepare prepare, Func<Nodes.Runner, Nodes.Runner> map, Func<bool> force)
    //         {
    //             var runner = default(Nodes.Runner);
    //             var cache = Nodes.Runner.Empty;
    //             return () => runner.Change(prepare()) || force() ? cache = map(runner) : cache;
    //         }
    //     }

    //     static (Nodes.Runner runner, int begin, int end)[] Groups(Nodes.Runner[] runners)
    //     {
    //         var groups = new List<(Nodes.Runner runner, int, int)>();
    //         var conflicts = Conflicts(runners);
    //         var last = 0;
    //         // Groups must cover the full range of the runner array so empty groups must not
    //         // be filtered or this may cause problems when executing them.
    //         for (int i = 0; i < runners.Length; i++) if (conflicts[i]) Merge(last, last = i);
    //         Merge(last, runners.Length);
    //         return groups.ToArray();

    //         void Merge(int begin, int end) => groups.Add((All(runners, begin, end), begin, end));

    //         static bool[] Conflicts(Nodes.Runner[] runners)
    //         {
    //             var dependencies = Array.Empty<Dependency>();
    //             var conflicts = new bool[runners.Length];
    //             for (int i = 0; i < runners.Length; i++)
    //             {
    //                 var runner = runners[i];
    //                 if (conflicts[i] = Dependency.Conflicts(dependencies, runner.Dependencies))
    //                     dependencies = runner.Dependencies;
    //                 else
    //                     dependencies = dependencies.Append(runner.Dependencies);
    //             }
    //             return conflicts;
    //         }
    //     }

    //     static Nodes.Runner All(Nodes.Runner[] runners, int begin, int end) =>
    //         Nodes.Runner.All(runners.Slice(begin, end - begin).ToArray());
    // }

    // public static Nodes.INode Run(params Action[] runs) => Node.Schedule(_ => () => new(runs, Array.Empty<Dependency>()));

    // public static Nodes.INode Run<TComponent1>(RunEC1<TComponent1> run, Matcher? matcher = null) => Node.Schedule(world =>
    // {
    //     var meta1 = world.Meta(typeof(TComponent1));
    //     var match = (matcher ?? Matcher.True).Match;
    //     var runner = Nodes.Runner.Empty;
    //     var segments = Array.Empty<(Segment segment, Action[] runs, int store1)>();
    //     var index = 0u;
    //     return () =>
    //     {
    //         var changed = false;
    //         while (index < world.Segments.Length)
    //         {
    //             var segment = world.Segments[index++];
    //             if (segment.TryIndex(meta1, out var store1) && match(segment, world))
    //             {
    //                 segments = segments.Append((segment, Array.Empty<Action>(), store1));
    //                 changed = true;
    //             }
    //         }

    //         foreach (ref var pair in segments.AsSpan())
    //         {
    //             if (pair.runs.Length == pair.segment.Chunks.Length) continue;

    //             changed = true;
    //             var start = pair.runs.Length;
    //             // Ensures the 'runs' array is always of the proper size.
    //             // If a segment is shrunk, excess runs will be thrown out.
    //             Array.Resize(ref pair.runs, pair.segment.Chunks.Length);
    //             for (int j = start; j < pair.runs.Length; j++)
    //             {
    //                 var chunk = pair.segment.Chunks[j];
    //                 var entities = chunk.Entities;
    //                 var store1 = (TComponent1[])chunk.Stores[pair.store1];
    //                 pair.runs[j] = () => { for (int i = 0; i < chunk.Count; i++) run(entities[i], ref store1[i]); };
    //             }
    //         }

    //         if (changed) runner = new(
    //             segments.Select(pair => pair.runs).Flatten(),
    //             segments.Select(pair => new[] { new Dependency(Dependency.Kinds.Read, typeof(Entity), pair.segment), new Dependency(Dependency.Kinds.Write, typeof(TComponent1), pair.segment) }).Flatten());

    //         return runner;
    //     };
    // });

    // internal static Func<Segment[]> Segments(this World world, Matcher matcher)
    // {
    //     var index = 0u;
    //     var segments = Array.Empty<Segment>();
    //     return () =>
    //     {
    //         while (index < world.Segments.Length)
    //         {
    //             var segment = world.Segments[index++];
    //             if (matcher.Match(segment, world)) segments = segments.Append(segment);
    //         }
    //         return segments;
    //     };
    // }
    // }
}