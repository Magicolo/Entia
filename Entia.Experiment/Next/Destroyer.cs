using System;

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

    public static partial class Extensions
    {
        public static Destroyer Destroyer(this World world, Matcher? matcher = null) => new(matcher ?? Matcher.True, world);
    }
}