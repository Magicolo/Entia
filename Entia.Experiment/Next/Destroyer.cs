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
            var runs = world.Segments(matcher).Update(Array.Empty<Action[]>(), (segments, runs) =>
            {
                var changed = false;
                if (segments.Length > runs.Length)
                {
                    changed = true;
                    runs = runs.Append(segments.Skip(runs.Length).Select(_ => Array.Empty<Action>()));
                }

                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    ref var run = ref runs[i];
                    if (segment.Chunks.Length > run.Length)
                    {
                        changed = true;
                        run = run.Append(segment.Chunks.Skip(run.Length).Select(world, (chunk, world) =>
                            new Action(() => world.Release(chunk.Entities.AsSpan(0, chunk.Count)))));
                    }
                }

                return changed ? runs : Option.None();
            }).Map(runs => runs.Flatten());
            var dependencies = Cache.Create(Array.Empty<Dependency>(), dependencies =>
            {
                var changed = false;
                var segments = world.Segments;
                if (segments.Length > dependencies.Length)
                {
                    changed = true;
                    dependencies = dependencies.Append(segments.Skip(dependencies.Length).Select(segment => segment.Write<Entity>()));
                }
                return changed ? dependencies : Option.None();
            });
            return new(runs, dependencies);
        });

        public static Nodes.INode Destroy(Action<Destroyer> run) => Destroy(destroyer => Run(() => run(destroyer)));
        public static Nodes.INode Destroy(Func<Destroyer, Nodes.INode> provide) => Lazy(world =>
        {
            var destroyer = world.Destroyer();
            return provide(destroyer).Map(plan =>
            {
                var dependencies = Cache.Change(() => world.Segments).Or(plan.Dependencies.Change()).Map(pair =>
                    pair.Item2.Append(pair.Item1.Select(segment => segment.Write<Entity>())));
                var runs = plan.Runs.Change().Or(dependencies.Change()).Map(pair =>
                    pair.Item2.Conflicts() ? pair.Item1.Combine().Map(run => new[] { run }).OrEmpty() : pair.Item1);
                return new(runs, dependencies);
            });
        });
    }

    public static partial class Extensions
    {
        public static Destroyer Destroyer(this World world) => new(world);
    }
}