using System;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Destroyer
    {
        internal Segment[] Segments => _segments();

        readonly World _world;
        readonly Func<Segment[]> _segments;

        public Destroyer(Matcher matcher, World world)
        {
            _world = world;
            _segments = world.Segments(matcher);
        }

        public bool Destroy(Entity entity) =>
            _world.TryDatum(entity, out var datum) &&
            Array.BinarySearch(_segments(), datum.Segment) >= 0 &&
            _world.Destroy(entity);
    }

    public static partial class Node
    {
        public static Nodes.INode Destroy(Matcher? matcher = null) => Node.Schedule(world =>
        {
            var destroyer = world.Destroyer(matcher);
            var segments = destroyer.Segments;
            var runner = Runner();
            return () => segments == (segments = destroyer.Segments) ? runner : runner = Runner();

            Nodes.Runner Runner() => new(
                segments.Select(world, static (segment, world) => new Action(() => world.Destroy(segment))),
                segments.Select(static segment => new Dependency(Dependency.Kinds.Write, typeof(Entity), segment)));
        });

        public static Nodes.INode Destroy(Action<Destroyer> run, Matcher? matcher = null) =>
            Destroy(destroyer => Run(() => run(destroyer)));
        public static Nodes.INode Destroy(Func<Destroyer, Nodes.INode> provide, Matcher? matcher = null) => Node.Lazy(world =>
        {
            var destroyer = world.Destroyer(matcher);
            var segments = destroyer.Segments;
            return provide(destroyer).Map(
                runner =>
                {
                    segments = destroyer.Segments;
                    var dependencies = segments.Select(segment => new Dependency(Dependency.Kinds.Write, typeof(Entity), segment));
                    return Dependency.Conflicts(runner.Dependencies, dependencies) ?
                        new(new[] { runner.Runs.Combine().Or(() => { }) }, runner.Dependencies.Append(dependencies)) :
                        new(runner.Runs, runner.Dependencies.Append(dependencies));
                },
                () => segments != destroyer.Segments);
        });
    }

    public static partial class Extensions
    {
        public static Destroyer Destroyer(this World world, Matcher? matcher = null) => new(matcher ?? Matcher.True, world);
    }
}