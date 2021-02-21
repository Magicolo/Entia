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
                var changed = ArrayUtility.Extend(ref runs, segments.Length, _ => Array.Empty<Action>());
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    changed |= ArrayUtility.Extend(ref runs[i], segment.Chunks.Length, (chunks: segment.Chunks, world), (index, state) =>
                    {
                        var chunk = state.chunks[index];
                        return new Action(() => world.Release(chunk.Entities.AsSpan(0, chunk.Count)));
                    });
                }
                return changed ? runs : Option.None();
            }).Map(runs => runs.Flatten());
            return new(runs, Cache.Constant(new[] { Dependency.Destroy }));
        });

        public static Nodes.INode Destroy(Action<Destroyer> run) => Destroy(destroyer => Run(() => run(destroyer)));
        public static Nodes.INode Destroy(Func<Destroyer, Nodes.INode> provide) => Lazy(world =>
        {
            var destroyer = world.Destroyer();
            return provide(destroyer).Map(plan =>
            {
                var dependencies = plan.Dependencies.Change(dependencies => dependencies.Prepend(Dependency.Destroy));
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