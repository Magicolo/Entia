using System;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Check
{
    public delegate IEnumerable<Generator<T>> Shrink<T>();
    public readonly struct Shrinker<T>
    {
        public readonly string Name;
        public readonly Shrink<T> Shrink;
        public Shrinker(string name, Shrink<T> shrink) { Name = name; Shrink = shrink; }
        public Shrinker<T> With(string name = null, Shrink<T> shrink = null) => new Shrinker<T>(name ?? Name, shrink ?? Shrink);
        public override string ToString() => Name;
    }

    public static class Shrinker
    {
        static class Cache<T>
        {
            public static Shrinker<T> Empty = From($"{nameof(Empty)}<{typeof(T).Name}>", () => Array.Empty<Generator<T>>());
        }

        public static Shrinker<T> From<T>(string name, Shrink<T> shrink) => new Shrinker<T>(name, shrink);
        public static Shrinker<T> From<T>(string name, IEnumerable<Generator<T>> shrinked) => new Shrinker<T>(name, () => shrinked);
        public static Shrinker<T> Empty<T>() => Cache<T>.Empty;
        public static Shrinker<TTarget> Map<TSource, TTarget>(this Shrinker<TSource> shrinker, Func<TSource, Generator.State, TTarget> map) =>
            From(nameof(Map), () => shrinker.Shrink().Select(generator => generator.Map(map)));
        public static Shrinker<TTarget> Choose<TSource, TTarget>(this Shrinker<TSource> shrinker, Func<TSource, Generator.State, Option<TTarget>> choose) =>
            From(nameof(Choose), () => shrinker.Shrink().Select(generator => generator.Choose(choose)));
        public static Shrinker<T> Flatten<T>(this Shrinker<Generator<T>> shrinker) =>
            From(nameof(Flatten), () => shrinker.Shrink().Select(generator => generator.Flatten()));
        public static Shrinker<T> And<T>(this Shrinker<T> shrinker1, Shrinker<T> shrinker2)
        {
            return From(nameof(And), Shrink);
            IEnumerable<Generator<T>> Shrink()
            {
                foreach (var generator in shrinker1.Shrink()) yield return generator;
                foreach (var generator in shrinker2.Shrink()) yield return generator;
            }
        }

        internal static Shrinker<decimal> Number(decimal source, decimal target)
        {
            return From(nameof(Number), Shrink);
            IEnumerable<Generator<decimal>> Shrink()
            {
                var difference = target - source;
                var sign = Math.Sign(difference);
                var magnitude = Math.Abs(difference);
                var direction = magnitude / 100m;
                for (int i = 0; i < 100 && magnitude > 0; i++, magnitude -= direction)
                {
                    var middle = Math.Round(magnitude * 0.5m * sign + source, 9);
                    if (middle == source) yield break;
                    yield return Generator.Constant(middle, Number(middle, target));
                }
            }
        }

        internal static Shrinker<T[]> Repeat<T>(T[] values, Shrinker<T>[] shrinkers)
        {
            return From(nameof(Repeat), Shrink);
            IEnumerable<Generator<T[]>> Shrink()
            {
                // Try to remove irrelevant generators.
                for (int i = 0; i < values.Length; i++)
                {
                    var pair = (values.RemoveAt(i), shrinkers.RemoveAt(i));
                    yield return Generator.Constant(pair.Item1, Repeat(pair.Item1, pair.Item2));
                }
                // Try to shrink relevant generators.
                foreach (var generator in All(values, shrinkers).Shrink()) yield return generator;
            }
        }

        internal static Shrinker<T[]> All<T>(T[] values, Shrinker<T>[] shrinkers)
        {
            return From(nameof(All), Shrink);
            IEnumerable<Generator<T[]>> Shrink()
            {
                for (int i = 0; i < shrinkers.Length; i++)
                {
                    foreach (var generator in shrinkers[i].Shrink())
                    {
                        yield return Generator.From(generator.Name, state =>
                        {
                            var pair = (CloneUtility.Shallow(values), CloneUtility.Shallow(shrinkers));
                            (pair.Item1[i], pair.Item2[i]) = generator.Generate(state);
                            return (pair.Item1, All(pair.Item1, pair.Item2));
                        });
                    }
                }
            }
        }
    }
}