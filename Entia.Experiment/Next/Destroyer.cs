using System;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Destroyer
    {
        public readonly Cache<Segment[]> Segments;
        readonly World _world;
        public Destroyer(Matcher matcher, World world) { Segments = world.Segments(matcher); _world = world; }

        public bool Destroy(Entity entity) =>
            _world.TryDatum(entity, out var datum) &&
            Array.BinarySearch(Segments.Get(), datum.Segment) >= 0 &&
            _world.Release(entity);
    }

    public static partial class Node
    {
        public static Nodes.INode Destroy(Matcher? matcher = null) => Schedule(world =>
        {
            var segments = world.Segments(matcher ?? Matcher.True);
            return new Plan(
                segments.Change(segments => segments.Select(segment => new Action(() => world.Release(segment)))),
                segments.Change(segments => segments.Select(segment => segment.Write<Entity>())));
        });

        public static Nodes.INode Destroy(Action<Destroyer> run, Matcher? matcher = null) =>
            Destroy(destroyer => Run(() => run(destroyer)), matcher);
        public static Nodes.INode Destroy(Func<Destroyer, Nodes.INode> provide, Matcher? matcher = null) => Lazy(world =>
        {
            var destroyer = world.Destroyer(matcher);
            return provide(destroyer).Map(plan =>
            {
                var dependencies = plan.Dependencies.Or(destroyer.Segments).Change().Map(pair =>
                    pair.Item1.Append(pair.Item2.Select(segment => segment.Write<Entity>())));
                var runs = plan.Runs.Or(dependencies).Change().Map(pair =>
                    pair.Item2.Conflicts() ? new[] { pair.Item1.Combine().Or(() => { }) } : pair.Item1);
                return new(runs, dependencies);
            });
        });
    }

    public static partial class Extensions
    {
        public static Destroyer Destroyer(this World world, Matcher? matcher = null) => new(matcher ?? Matcher.True, world);
    }
}