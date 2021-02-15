using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Mutator<T>
    {
        public readonly Cache<Segment[]> Segments;
        readonly ConcurrentDictionary<Segment, Segment> _add;
        readonly ConcurrentDictionary<Segment, Segment> _remove;

        public Mutator(Matcher matcher, World world)
        {
            Segments = world.Segments(matcher);
            _add = new();
            _remove = new();
        }

        // public bool Add(Entity entity, T component) { }
        // public bool Remove(Entity entity) { }
    }

    public static partial class Node
    {
        public static Nodes.INode Mutate<T>(Func<Mutator<T>, Nodes.INode> provide) => Lazy(world =>
        {
            var mutator = world.Mutator<T>();
            return provide(mutator).Map(plan =>
            {
                var dependencies = plan.Dependencies.Or(mutator.Segments).Change().Map(pair =>
                    pair.Item1.Append(pair.Item2.Select(segment => segment.Write()).Flatten()));
                var runs = plan.Runs.Or(dependencies).Change().Map(pair =>
                    pair.Item2.Conflicts() ? new[] { pair.Item1.Combine().Or(() => { }) } : pair.Item1);
                return new(runs, dependencies);
            });
        });
    }

    public static partial class Extensions
    {
        public static Mutator<T> Mutator<T>(this World world, Matcher? matcher = null) => new(matcher ?? Matcher.True, world);
    }
}