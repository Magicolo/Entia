using System;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Destroyer
    {
        readonly World _world;
        public Destroyer(World world) { _world = world; }

        public uint Destroy(Entity entity)
        {
            Span<Entity> entities = stackalloc Entity[1];
            entities[0] = entity;
            return Destroy(entities);
        }

        public uint Destroy(ReadOnlySpan<Entity> entities) => _world.Release(entities);
    }

    public static partial class Node
    {
        public static Nodes.INode Destroy(Matcher matcher) => Schedule(world =>
        {
            var runs = world.Segments(matcher).Change(Array.Empty<(Segment segment, Action[] runs)>(), (segments, pairs) =>
            {
                var changed = segments.Length > pairs.Length;
                pairs = pairs.Append(segments.Skip(pairs.Length).Select(static segment => (segment, Array.Empty<Action>())));
                foreach (ref var pair in pairs.AsSpan())
                {
                    changed |= pair.segment.Chunks.Length > pair.runs.Length;
                    pair.runs = pair.runs.Append(pair.segment.Chunks
                        .Skip(pair.runs.Length)
                        .Select(world, static (chunk, world) => new Action(() => world.Release(chunk.Entities.AsSpan(0, chunk.Count)))));
                }
                return changed ? pairs : Option.None();
            }).Map(static pairs => pairs.Select(pair => pair.runs).Flatten());
            var dependencies = Cache.Change(() => world.Segments)
                .Map(Array.Empty<Dependency>(), static (segments, dependencies) => dependencies.Append(
                    segments.Skip(dependencies.Length).Select(segment => segment.Write<Entity>())));
            return new(runs, dependencies);
        });

        public static Nodes.INode Destroy(Action<Destroyer> run) => Destroy(destroyer => Run(() => run(destroyer)));
        public static Nodes.INode Destroy(Func<Destroyer, Nodes.INode> provide) => Lazy(world =>
        {
            var destroyer = world.Destroyer();
            var segments = Cache.Change(() => world.Segments);
            return provide(destroyer).Map(plan =>
            {
                var dependencies = plan.Dependencies.Or(segments).Change().Map(static pair =>
                    pair.Item1.Append(pair.Item2.Select(static segment => segment.Write<Entity>())));
                var runs = plan.Runs.Or(dependencies).Change().Map(pair =>
                    pair.Item2.Conflicts() ? new[] { pair.Item1.Combine().Or(() => { }) } : pair.Item1);
                return new(runs, dependencies);
            });
        });
    }

    public static partial class Extensions
    {
        public static Destroyer Destroyer(this World world) => new(world);
    }
}