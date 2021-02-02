using System;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Creator<T>
    {
        static readonly Initialize<T> _default = (int _, int _, Segment.Chunk _, in T _) => { };

        internal Segment Segment => _segment;
        readonly World _world;
        readonly Segment _segment;
        readonly Initialize<T> _initialize;

        public Creator(Template<T> template, World world, int? size = default)
        {
            var initializers = template.Initializers.Select(pair => (meta: world.Meta(pair.type), pair.initialize));
            var initialize = default(Initialize<T>);
            var segment = world.Segment(initializers.Select(pair => pair.meta), size);
            foreach (var pair in initializers)
            {
                segment.TryIndex(pair.meta, out var store);
                initialize += (int index, int count, Segment.Chunk chunk, in T state) =>
                    pair.initialize(index, count, chunk.Stores[store], state);
            }

            _world = world;
            _segment = segment;
            _initialize = initialize ?? _default;
        }

        public Entity Create(in T state) => _world.Create(_segment, state, _initialize);
        public void Create(Span<Entity> entities, in T state) => _world.Create(entities, _segment, state, _initialize);
    }

    public static partial class Node
    {
        public static Nodes.INode Create<T>(Template<T> template, Action<Creator<T>> run) =>
            Create(template, creator => Run(() => run(creator)));
        public static Nodes.INode Create<T>(Template<T> template, Func<Creator<T>, Nodes.INode> provide) => Node.Lazy(world =>
        {
            var creator = world.Creator(template);
            var segment = creator.Segment;
            var dependencies = segment.Metas
                .Select(meta => new Dependency(Dependency.Kinds.Write, meta.Type, segment))
                .Prepend(new Dependency(Dependency.Kinds.Write, typeof(Entity), segment));
            return provide(creator).Map(runner => Dependency.Conflicts(runner.Dependencies, dependencies) ?
                new(new[] { runner.Runs.Combine().Or(() => { }) }, runner.Dependencies.Append(dependencies)) :
                new(runner.Runs, runner.Dependencies.Append(dependencies)));
        });
    }

    public static partial class Extensions
    {
        public static Creator<T> Creator<T>(this World world, Template<T> template, int? size = null) => new(template, world, size);
        public static Entity Create(this Creator<Unit> creator) => creator.Create(default);
        public static void Create(this Creator<Unit> creator, Span<Entity> entities) => creator.Create(entities, default);
    }
}