using System;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Dependency
    {
        public static readonly Dependency Unknown = new Dependency(Kinds.Unknown, default, default);

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

    public static partial class Extensions
    {
        public static bool Conflicts(this Dependency[] left, Dependency[] right) =>
            left.Length == 0 || right.Length == 0 ? false : left.Append(right).Conflicts();
        public static bool Conflicts(this Dependency[] dependencies)
        {
            if (dependencies.Length == 0) return false;
            return
                dependencies.Any(dependency => dependency.IsUnknown()) ||
                Pairs(dependencies)
                    .Where(pair => Equals(pair.Item1.Type, pair.Item2.Type) && Equals(pair.Item1.Segment, pair.Item2.Segment))
                    .Any(pair => pair.Item1.Kind == Dependency.Kinds.Write || pair.Item2.Kind == Dependency.Kinds.Write);

            static IEnumerable<(Dependency, Dependency)> Pairs(Dependency[] dependencies)
            {
                for (int i = 0; i < dependencies.Length; i++)
                    for (int j = i + 1; j < dependencies.Length; j++)
                        yield return (dependencies[i], dependencies[j]);
            }
        }

        public static bool Is(this Dependency dependency, Dependency.Kinds kind) => dependency.Kind == kind;
        public static bool IsUnknown(this Dependency dependency) => dependency.Is(Dependency.Kinds.Unknown);
        public static bool IsWrite(this Dependency dependency) => dependency.Is(Dependency.Kinds.Write);
        public static bool IsRead(this Dependency dependency) => dependency.Is(Dependency.Kinds.Read);
        public static Dependency[] Write(this Segment segment) => segment.Depend(Dependency.Kinds.Write);
        public static Dependency Write<T>(this Segment segment) => segment.Depend<T>(Dependency.Kinds.Write);
        public static Dependency Write(this Segment segment, Type type) => segment.Depend(Dependency.Kinds.Write, type);
        public static Dependency[] Read(this Segment segment) => segment.Depend(Dependency.Kinds.Read);
        public static Dependency Read<T>(this Segment segment) => segment.Depend<T>(Dependency.Kinds.Read);
        public static Dependency Read(this Segment segment, Type type) => segment.Depend(Dependency.Kinds.Read, type);
        public static Dependency[] Depend(this Segment segment, Dependency.Kinds kind) => segment.Metas.Select(meta => segment.Depend(kind, meta.Type)).Prepend(segment.Depend<Entity>(kind));
        public static Dependency Depend<T>(this Segment segment, Dependency.Kinds kind) => segment.Depend(kind, typeof(T));
        public static Dependency Depend(this Segment segment, Dependency.Kinds kind, Type type) => new(kind, type, segment);
    }
}