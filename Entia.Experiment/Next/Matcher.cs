using System;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Matcher : IEquatable<Matcher>
    {
        public static readonly Matcher True = new((_, _) => true);
        public static readonly Matcher False = new((_, _) => false);

        public static implicit operator Matcher(Type type) => Has(type);

        public static Matcher Has<T>() => Has(typeof(T));
        public static Matcher Has(Type type) => new((segment, world) =>
            world.TryMeta(type, out var meta) && segment.TryIndex(meta, out _));

        public static Matcher Not(Matcher matcher) => new((segment, world) => !matcher.Match(segment, world));

        public static Matcher All(params Matcher[] matchers)
        {
            if (matchers.Contains(False)) return False;
            matchers = matchers.Except(True).ToArray();
            return
                matchers.Length == 0 ? True :
                matchers.Length == 1 ? matchers[0] :
                new((segment, world) => matchers.All(matcher => matcher.Match(segment, world)));
        }

        public static Matcher Any(params Matcher[] matchers)
        {
            if (matchers.Contains(True)) return True;
            matchers = matchers.Except(False).ToArray();
            return
                matchers.Length == 0 ? False :
                matchers.Length == 1 ? matchers[0] :
                new((segment, world) => matchers.Any(matcher => matcher.Match(segment, world)));
        }

        public static Matcher None(params Matcher[] matchers)
        {
            if (matchers.Contains(True)) return False;
            matchers = matchers.Except(False).ToArray();
            return
                matchers.Length == 0 ? True :
                matchers.Length == 1 ? matchers[0] :
                new((segment, world) => matchers.None(matcher => matcher.Match(segment, world)));
        }

        public readonly Func<Segment, World, bool> Match;
        public Matcher(Func<Segment, World, bool> match) { Match = match; }
        public bool Equals(Matcher other) => Match == other.Match;
        public override bool Equals(object obj) => obj is Matcher matcher && Equals(matcher);
        public override int GetHashCode() => Match.GetHashCode();
    }
}