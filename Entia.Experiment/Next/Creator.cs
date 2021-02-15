using System;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Creator<T>
    {
        static readonly World.Initializer<T> _default = (in World.Context _, in T _) => { };

        public readonly Cache<Segment[]> Segments;
        readonly World _world;
        readonly (int parent, Segment segment, World.Initializer<T> initialize)[] _parts;

        public Creator(Template<T> template, World world)
        {
            _world = world;
            _parts = Parts(template, 0, -1, world).ToArray();
            Segments = Cache.Constant(_parts.Select(template => template.segment));

            static IEnumerable<(int, Segment, World.Initializer<T>)> Parts(Template<T> template, int self, int parent, World world)
            {
                yield return Part(template, parent, world);
                parent = self++;

                foreach (var child in template.Children)
                {
                    foreach (var part in Parts(child, self, parent, world))
                    {
                        yield return part;
                        self++;
                    }
                }
            }

            static (int, Segment, World.Initializer<T>) Part(Template<T> template, int parent, World world)
            {
                var initializers = template.Initializers.Select(pair => (meta: world.Meta(pair.Type), pair.Initialize));
                var initialize = default(World.Initializer<T>);
                var segment = world.Segment(initializers.Select(pair => pair.meta), template.Size);
                foreach (var pair in initializers)
                {
                    segment.TryIndex(pair.meta, out var store);
                    initialize += (in World.Context context, in T state) =>
                        pair.Initialize(new(
                            context.Index,
                            context.Count,
                            context.Chunk.Entities,
                            context.Chunk.Stores[store],
                            context.Parents), state);
                }
                return (parent, segment, initialize ?? _default);
            }
        }

        public Entity Create(in T state)
        {
            Span<Entity> entities = stackalloc Entity[1];
            Create(entities, state);
            return entities[0];
        }

        public void Create(Span<Entity> entities, in T state)
        {
            if (_parts.Length == 1)
            {
                var (parent, segment, initialize) = _parts[0];
                _world.Reserve(entities);
                _world.Initialize(entities, Array.Empty<Entity>(), segment, state, initialize);
            }

            var batch = entities.Length;
            var count = batch * _parts.Length;
            var buffer = Buffer<Creator<Unit>, Entity>.Get(count);
            _world.Reserve(buffer.AsSpan(0, count));
            for (int i = 0; i < _parts.Length; i++)
            {
                var (parent, segment, initialize) = _parts[i];
                var parents = parent >= 0 ? buffer.AsSpan(parent * batch, batch) : Array.Empty<Entity>();
                _world.Initialize(buffer.AsSpan(i * batch, batch), parents, segment, state, initialize);
            }
            buffer.CopyTo(entities);
        }
    }

    public static partial class Node
    {
        public static Nodes.INode Create<T>(Template<T> template, Action<Creator<T>> run) =>
            Create(template, creator => Run(() => run(creator)));
        public static Nodes.INode Create<T>(Template<T> template, Func<Creator<T>, Nodes.INode> provide) => Lazy(world =>
        {
            var creator = world.Creator(template);
            return provide(creator).Map(plan =>
            {
                var dependencies = plan.Dependencies.Or(creator.Segments).Change().Map(pair =>
                    pair.Item1.Append(pair.Item2.Select(segment => segment.Write<Entity>())));
                var runs = plan.Runs.Or(dependencies).Change().Map(pair =>
                    pair.Item2.Conflicts() ? new[] { pair.Item1.Combine().Or(() => { }) } : pair.Item1);
                return new(runs, dependencies);
            });
        });
    }

    public static partial class Extensions
    {
        public static Creator<T> Creator<T>(this World world, Template<T> template) => new(template, world);
        public static Entity Create(this Creator<Unit> creator) => creator.Create(default);
        public static void Create(this Creator<Unit> creator, Span<Entity> entities) => creator.Create(entities, default);
    }
}