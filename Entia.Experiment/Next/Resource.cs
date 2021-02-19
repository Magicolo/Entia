using System;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Resource<T>
    {
        static readonly Func<T> _provide = DefaultUtility.Default<T>;
        static readonly Template<Unit> _template = Template.Empty().Add(_ => _provide()).Size(1);

        public ref T Value => ref _store[0];
        readonly T[] _store;

        public Resource(World world)
        {
            var creator = world.Creator(_template);
            var segment = creator.Segments[0];
            if (segment.Chunks.Length == 0) creator.Create();
            _store = (T[])segment.Chunks[0].Stores[0];
        }
    }

    public static partial class Extensions
    {
        public static Resource<T> Resource<T>(this World world) => new(world);
    }
}