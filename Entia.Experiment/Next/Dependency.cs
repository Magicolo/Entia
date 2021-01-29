using System;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Dependency
    {
        public static readonly Dependency Unknown = new Dependency(Kinds.Unknown, default, default);

        public static bool Conflicts(Dependency[] left, Dependency[] right)
        {
            if (left.Length == 0 || right.Length == 0) return false;
            return
                left.Any(dependency => dependency.Kind == Dependency.Kinds.Unknown) ||
                right.Any(dependency => dependency.Kind == Dependency.Kinds.Unknown) ||
                left.Pairs(right)
                    .Where(pair => pair.Item1.Type == pair.Item2.Type && pair.Item1.Segment == pair.Item2.Segment)
                    .Any(pair => pair.Item1.Kind == Dependency.Kinds.Write || pair.Item2.Kind == Dependency.Kinds.Write);
        }

        public enum Kinds { Unknown, Read, Write }
        public readonly Kinds Kind;
        public readonly Type Type;
        public readonly Segment Segment;

        public Dependency(Kinds kind, Type type, Segment segment)
        {
            Kind = kind;
            Type = type;
            Segment = segment;
        }
    }
}