using System;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    struct CreatorBufferKey { }
    public readonly struct Creator<T>
    {
        readonly struct Part
        {
            public readonly int Parent;
            public readonly (int index, int count) Children;
            public readonly Segment Segment;
            public readonly Initialize<T> Initialize;

            public Part(int parent, (int index, int count) children, Segment segment, Initialize<T> initialize)
            {
                Parent = parent;
                Children = children;
                Segment = segment;
                Initialize = initialize;
            }
        }

        static readonly Initialize<T> _default = (in Context _, in T _) => { };

        public readonly Cache<Segment[]> Segments;
        readonly World _world;
        readonly Part[] _parts;

        public Creator(Template<T> template, World world)
        {
            _world = world;
            _parts = Parts(0, -1, new[] { template }, world).ToArray();
            Segments = Cache.Constant(_parts.Select(part => part.Segment));

            static IEnumerable<Part> Parts(int self, int parent, Template<T>[] templates, World world)
            {
                var index = self + templates.Length;
                foreach (var template in templates)
                {
                    var count = template.Children.Length;
                    yield return Part(template, parent, (index, count), world);
                    index += count;
                }
                var children = templates.Select(template => template.Children).Flatten();
                foreach (var part in Parts(self + templates.Length, self, children, world)) yield return part;
            }

            static Part Part(Template<T> template, int parent, (int index, int count) children, World world)
            {
                var initializers = template.Initializers
                    .Select(pair => (meta: world.Meta(pair.Type), initialize: pair.Initialize));
                var initialize = default(Initialize<T>);
                var segment = world.Segment(initializers.Select(pair => pair.meta), template.Size);
                foreach (var pair in initializers)
                {
                    segment.TryIndex(pair.meta, out var store);
                    initialize += (in Context context, in T state) => pair.initialize(store, context, state);
                }
                return new(parent, children, segment, initialize ?? _default);
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
                var part = _parts[0];
                _world.Reserve(entities);
                _world.Initialize(entities, Array.Empty<Entity>(), Array.Empty<Entity>(), part.Segment, state, part.Initialize);
                return;
            }

            var batch = entities.Length;
            var count = batch * _parts.Length;
            var buffer = Buffer.Get<CreatorBufferKey, Entity>(count);
            _world.Reserve(buffer.AsSpan(0, count));
            for (int i = 0; i < _parts.Length; i++)
            {
                var part = _parts[i];
                var parents = part.Parent >= 0 ? buffer.Slice(part.Parent * batch, batch) : Array.Empty<Entity>();
                var children = part.Children.index >= 0 && part.Children.count > 0 ?
                    buffer.Slice(part.Children.index * batch, part.Children.count * batch) :
                    Array.Empty<Entity>();
                _world.Initialize(buffer.AsSpan(i * batch, batch), parents, children, part.Segment, state, part.Initialize);
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
            var creator = world.Creator(template.Descend(child =>
                child.Has<Family>() ? child :
                child.Add((entity, parent, children, _) => new Family { Self = entity, Parent = parent, Children = children.ToArray() })));
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