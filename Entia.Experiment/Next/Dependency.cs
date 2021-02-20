using System;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Dependency : IEquatable<Dependency>
    {
        public static readonly Dependency Unknown = new(Kinds.Unknown, default, default);
        public static readonly Dependency Destroy = new(Kinds.Destroy, default, default);

        public enum Kinds { Unknown, Read, Write, Create, Destroy }
        public readonly Kinds Kind;
        public readonly Type Type;
        public readonly Segment Segment;

        public Dependency(Kinds kind, Type type, Segment segment) { Kind = kind; Type = type; Segment = segment; }
        public bool Equals(Dependency other) => Kind == other.Kind && Type == other.Type && Segment == other.Segment;
        public override bool Equals(object obj) => obj is Dependency dependency && Equals(dependency);
        public override int GetHashCode() => HashCode.Combine(Kind, Type, Segment);
        public override string ToString() => $"{{ Kind: {Kind}, Type: {Type}, Segment: {Segment.Index} }}";
        public void Deconstruct(out Kinds kind, out Segment segment, out Type type) => (kind, segment, type) = (Kind, Segment, Type);
    }

    public static partial class Extensions
    {
        public static bool Conflicts(this Dependency[] left, Dependency[] right) =>
            left.Length != 0 && right.Length != 0 && left.Append(right).Conflicts();
        public static bool Conflicts(this Dependency[] dependencies)
        {
            var destroy = false;
            var creates = new HashSet<Segment>();
            var segments = new HashSet<Segment>();
            var reads = new HashSet<(Segment segment, Type type)>();
            var writes = new HashSet<(Segment segment, Type type)>();
            foreach (var (kind, segment, type) in dependencies)
            {
                var pair = (segment, type);
                switch (kind)
                {
                    case Dependency.Kinds.Unknown: return true;
                    case Dependency.Kinds.Destroy:
                        if (creates.Count > 0 || segments.Count > 0 || reads.Count > 0 || writes.Count > 0) return true;
                        destroy = true;
                        break;
                    case Dependency.Kinds.Create:
                        if (destroy || segments.Contains(segment)) return true;
                        creates.Add(segment);
                        break;
                    case Dependency.Kinds.Read:
                        if (destroy || creates.Contains(segment) || writes.Contains(pair)) return true;
                        reads.Add(pair);
                        segments.Add(segment);
                        break;
                    case Dependency.Kinds.Write:
                        if (destroy || creates.Contains(segment) || reads.Contains(pair) || writes.Contains(pair)) return true;
                        writes.Add(pair);
                        segments.Add(segment);
                        break;
                }
            }
            return false;
        }

        public static bool Is(this Dependency dependency, Dependency.Kinds kind) => dependency.Kind == kind;
        public static bool IsUnknown(this Dependency dependency) => dependency.Is(Dependency.Kinds.Unknown);
        public static bool IsWrite(this Dependency dependency) => dependency.Is(Dependency.Kinds.Write);
        public static bool IsRead(this Dependency dependency) => dependency.Is(Dependency.Kinds.Read);
        public static bool IsCreate(this Dependency dependency) => dependency.Is(Dependency.Kinds.Create);
        public static bool IsDestroy(this Dependency dependency) => dependency.Is(Dependency.Kinds.Destroy);
        public static Dependency Write<T>(this Segment segment) => segment.Depend<T>(Dependency.Kinds.Write);
        public static Dependency Write(this Segment segment, Type type) => segment.Depend(Dependency.Kinds.Write, type);
        public static Dependency Read<T>(this Segment segment) => segment.Depend<T>(Dependency.Kinds.Read);
        public static Dependency Read(this Segment segment, Type type) => segment.Depend(Dependency.Kinds.Read, type);
        public static Dependency Create(this Segment segment) => segment.Depend(Dependency.Kinds.Create, default);
        public static Dependency Depend<T>(this Segment segment, Dependency.Kinds kind) => segment.Depend(kind, typeof(T));
        public static Dependency Depend(this Segment segment, Dependency.Kinds kind, Type type) => new(kind, type, segment);
    }
}