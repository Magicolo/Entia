using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entia.Core;

namespace Entia.Experiment.V4
{
    namespace Nodes
    {
        public delegate Runner Prepare();
        public delegate Prepare Schedule(World world);

        public readonly struct Runner : IEquatable<Runner>
        {
            public static readonly Runner Empty = new Runner(Array.Empty<Action>(), Array.Empty<Dependency>());

            public static bool operator ==(Runner left, Runner right) => left.Equals(right);
            public static bool operator !=(Runner left, Runner right) => !(left == right);

            public static Runner All(params Runner[] runners) =>
                runners.Length == 0 ? Empty :
                runners.Length == 1 ? runners[0] :
                new(
                    runners.Select(runner => runner.Runs).Flatten(),
                    runners.Select(runner => runner.Dependencies).Flatten());

            public readonly Action[] Runs;
            public readonly Dependency[] Dependencies;

            public Runner(Action[] runs, Dependency[] dependencies) { Runs = runs; Dependencies = dependencies; }
            public Runner With(Action[] runs) => new Runner(runs, Dependencies);
            public Runner With(Dependency[] dependencies) => new Runner(Runs, dependencies);
            public bool Equals(Runner other) => Runs == other.Runs && Dependencies == other.Dependencies;
            public override bool Equals(object obj) => obj is Runner runner && Equals(runner);
            public override int GetHashCode() => HashCode.Combine(Runs, Dependencies);
            public void Deconstruct(out Action[] runs, out Dependency[] dependencies) => (runs, dependencies) = (Runs, Dependencies);
        }

        public interface INode { }

        readonly struct Lazy : INode
        {
            public readonly Func<World, INode> Provide;
            public Lazy(Func<World, INode> provide) { Provide = provide; }
        }

        readonly struct Mapper : INode
        {
            public readonly INode Node;
            public readonly Func<Runner, Runner> Map;
            public readonly Func<bool> Change;
            public Mapper(Nodes.INode node, Func<Runner, Runner> map, Func<bool> change) { Node = node; Map = map; Change = change; }
        }

        readonly struct Scheduler : INode
        {
            public readonly Schedule Schedule;
            public Scheduler(Schedule schedule) { Schedule = schedule; }
        }

        readonly struct All : INode
        {
            public readonly INode[] Nodes;
            public All(INode[] nodes) { Nodes = nodes; }
        }
    }

    public static partial class Node
    {
        public static readonly Nodes.INode Empty = Schedule(_ => () => Nodes.Runner.Empty);

        public static Nodes.INode Lazy(Func<World, Nodes.INode> provide) => new Nodes.Lazy(provide);
        public static Nodes.INode Schedule(Nodes.Schedule schedule) => new Nodes.Scheduler(schedule);
        public static Nodes.INode All(params Nodes.INode[] nodes) => new Nodes.All(nodes);
        public static Nodes.INode All(this IEnumerable<Nodes.INode> nodes) => All(nodes.ToArray());
        public static Nodes.INode Map(this Nodes.INode node, Func<Nodes.Runner, Nodes.Runner> map, Func<bool> change = null) => new Nodes.Mapper(node, map, change ?? new(() => false));
        public static Nodes.INode Synchronize() => Empty.Synchronous();
        public static Nodes.INode Synchronous(this Nodes.INode node) =>
            node.Map(runner => new(new[] { runner.Runs.Combine().Or(() => { }) }, runner.Dependencies.Append(Dependency.Unknown)));
        public static Nodes.INode If(this Nodes.INode node, Func<bool> condition) =>
            node.Map(runner => runner.With(runner.Runs.Select(run => new Action(() => { if (condition()) run(); }))));

        public delegate void RunR1EC1<TResource1, TComponent1>(ref TResource1 resource1, Entity entity, ref TComponent1 component1);
        public delegate void RunEC2<TComponent1, TComponent2>(ref TComponent1 component1, in TComponent2 component2);
        public delegate void RunEC1<TComponent1>(Entity entity, ref TComponent1 component1);

        public static Action Schedule(this Nodes.INode node, World world)
        {
            var prepares = Prepare(node, world);
            var runners = prepares.Select(prepare => prepare());
            var groups = Groups(runners);
            return () =>
            {
                var reschedule = false;
                for (int i = 0; i < groups.Length; i++)
                {
                    var (runner, begin, end) = groups[i];
                    var changed = (runs: false, dependencies: false);
                    for (int j = begin; j < end; j++)
                    {
                        ref var previous = ref runners[j];
                        var current = prepares[j]();
                        changed.runs |= previous.Runs != current.Runs;
                        changed.dependencies |= previous.Dependencies != current.Dependencies;
                        previous = current;
                    }

                    reschedule |= changed.dependencies;
                    // Since dependencies have changed, it is not safe to run the group runners in
                    // parallel until the reschedule happens.
                    if (changed.dependencies) for (int j = begin; j < end; j++) Run(runners[j]);
                    else if (changed.runs) Run(groups[i].runner = All(runners, begin, end));
                    else Run(runner);
                }
                if (reschedule) groups = Groups(runners);
            };

            static Nodes.Prepare[] Prepare(Nodes.INode node, World world)
            {
                return Interpret(Resolve(node));

                Nodes.INode Resolve(Nodes.INode node) => node switch
                {
                    Nodes.Lazy lazy => Resolve(lazy.Provide(world)),
                    Nodes.Mapper map => Resolve(map.Node) switch
                    {
                        Nodes.Mapper inner => inner.Node.Map(
                            runner => map.Map(inner.Map(runner)),
                            () => map.Change() || inner.Change()),
                        Nodes.INode outer => outer.Map(map.Map, map.Change)
                    },
                    Nodes.All all => all.Nodes
                        .Select(Resolve)
                        .Select(static node => node is Nodes.All inner ? inner.Nodes : new[] { node })
                        .Flatten()
                        .All(),
                    _ => node
                };

                Nodes.Prepare[] Interpret(Nodes.INode node) => node switch
                {
                    Nodes.Mapper map => Interpret(map.Node).Select(prepare => Map(prepare, map.Map, map.Change)),
                    Nodes.All all => all.Nodes.Select(Interpret).Flatten(),
                    Nodes.Scheduler system => new[] { system.Schedule(world) },
                    _ => Array.Empty<Nodes.Prepare>()
                };

                Nodes.Prepare Map(Nodes.Prepare prepare, Func<Nodes.Runner, Nodes.Runner> map, Func<bool> force)
                {
                    var runner = default(Nodes.Runner);
                    var cache = Nodes.Runner.Empty;
                    return () => runner.Change(prepare()) || force() ? cache = map(runner) : cache;
                }
            }

            static (Nodes.Runner runner, int begin, int end)[] Groups(Nodes.Runner[] runners)
            {
                var groups = new List<(Nodes.Runner runner, int, int)>();
                var conflicts = Conflicts(runners);
                var last = 0;
                // Groups must cover the full range of the runner array so empty groups must not
                // be filtered or this may cause problems when executing them.
                for (int i = 0; i < runners.Length; i++) if (conflicts[i]) Merge(last, last = i);
                Merge(last, runners.Length);
                return groups.ToArray();

                void Merge(int begin, int end) => groups.Add((All(runners, begin, end), begin, end));

                static bool[] Conflicts(Nodes.Runner[] runners)
                {
                    var dependencies = Array.Empty<Dependency>();
                    var conflicts = new bool[runners.Length];
                    for (int i = 0; i < runners.Length; i++)
                    {
                        var runner = runners[i];
                        if (conflicts[i] = Dependency.Conflicts(dependencies, runner.Dependencies))
                            dependencies = runner.Dependencies;
                        else
                            dependencies = dependencies.Append(runner.Dependencies);
                    }
                    return conflicts;
                }
            }

            static Nodes.Runner All(Nodes.Runner[] runners, int begin, int end) =>
                Nodes.Runner.All(runners.Slice(begin, end - begin).ToArray());

            static void Run(Nodes.Runner runner)
            {
                if (runner.Runs.Length <= 8) foreach (var run in runner.Runs) run();
                else runner.Runs.Select(Task.Run).Iterate(task => task.Wait());
            }
        }

        public static Nodes.INode Run(params Action[] runs) => Node.Schedule(_ => () => new(runs, Array.Empty<Dependency>()));

        public static Nodes.INode Run<TComponent1>(RunEC1<TComponent1> run, Matcher? matcher = null) => Node.Schedule(world =>
        {
            var meta1 = world.Meta(typeof(TComponent1));
            var match = (matcher ?? Matcher.True).Match;
            var runner = Nodes.Runner.Empty;
            var segments = Array.Empty<(Segment segment, Action[] runs, int store1)>();
            var index = 0u;
            return () =>
            {
                var changed = false;
                while (index < world.Segments.Length)
                {
                    var segment = world.Segments[index++];
                    if (segment.TryIndex(meta1, out var store1) && match(segment, world))
                    {
                        segments = segments.Append((segment, Array.Empty<Action>(), store1));
                        changed = true;
                    }
                }

                foreach (ref var pair in segments.AsSpan())
                {
                    if (pair.runs.Length == pair.segment.Chunks.Length) continue;

                    changed = true;
                    var start = pair.runs.Length;
                    // Ensures the 'runs' array is always of the proper size.
                    // If a segment is shrunk, excess runs will be thrown out.
                    Array.Resize(ref pair.runs, pair.segment.Chunks.Length);
                    for (int j = start; j < pair.runs.Length; j++)
                    {
                        var chunk = pair.segment.Chunks[j];
                        var entities = chunk.Entities;
                        var store1 = (TComponent1[])chunk.Stores[pair.store1];
                        pair.runs[j] = () => { for (int i = 0; i < chunk.Count; i++) run(entities[i], ref store1[i]); };
                    }
                }

                if (changed) runner = new(
                    segments.Select(pair => pair.runs).Flatten(),
                    segments.Select(pair => new[] { new Dependency(Dependency.Kinds.Read, typeof(Entity), pair.segment), new Dependency(Dependency.Kinds.Write, typeof(TComponent1), pair.segment) }).Flatten());

                return runner;
            };
        });

        internal static Func<Segment[]> Segments(this World world, Matcher matcher)
        {
            var index = 0u;
            var segments = Array.Empty<Segment>();
            return () =>
            {
                while (index < world.Segments.Length)
                {
                    var segment = world.Segments[index++];
                    if (matcher.Match(segment, world)) segments = segments.Append(segment);
                }
                return segments;
            };
        }
    }
}