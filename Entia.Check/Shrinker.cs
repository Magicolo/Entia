using System;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;
using static Entia.Check.Formatting;

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
            public static Shrinker<T> Empty = From(Name<T>.Empty, () => Array.Empty<Generator<T>>());
        }

        public static Shrinker<T> From<T>(string name, Shrink<T> shrink) => new Shrinker<T>(name, shrink);
        public static Shrinker<T> From<T>(string name, IEnumerable<Generator<T>> shrinked) => From(name, () => shrinked);
        public static Shrinker<T> Empty<T>() => Cache<T>.Empty;
        public static Shrinker<TTarget> Map<TSource, TTarget>(this Shrinker<TSource> shrinker, Func<TSource, Generator.State, TTarget> map) =>
            From(Name<TSource, TTarget>.Map.Format(shrinker),
                () => shrinker.Shrink().Select(generator => generator.Map(map)));
        public static Shrinker<TTarget> Choose<TSource, TTarget>(this Shrinker<TSource> shrinker, Func<TSource, Generator.State, Option<TTarget>> choose) =>
            From(Name<TSource, TTarget>.Choose.Format(shrinker),
                () => shrinker.Shrink().Select(generator => generator.Choose(choose)));
        public static Shrinker<T> Flatten<T>(this Shrinker<Generator<T>> shrinker) =>
            From(Name<T>.Flatten.Format(shrinker),
                () => shrinker.Shrink().Select(generator => generator.Flatten()));
        public static Shrinker<T> And<T>(this Shrinker<T> shrinker1, Shrinker<T> shrinker2)
        {
            return From(Name<T>.And.Format(shrinker1, shrinker2), Shrink);

            IEnumerable<Generator<T>> Shrink()
            {
                foreach (var generator in shrinker1.Shrink()) yield return generator;
                foreach (var generator in shrinker2.Shrink()) yield return generator;
            }
        }

        internal static Shrinker<decimal> Number(decimal source, decimal target)
        {
            return From(nameof(Number).Format(source, target), Shrink);

            IEnumerable<Generator<decimal>> Shrink()
            {
                var difference = target - source;
                var sign = Math.Sign(difference);
                var magnitude = Math.Abs(difference);
                var direction = magnitude / 100.0m;
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
            return From(Name<T>.Repeat.Format(shrinkers), Shrink);

            IEnumerable<Generator<T[]>> Shrink()
            {
                // Try to remove irrelevant generators.
                for (int i = 0; i < values.Length; i++)
                {
                    var pair = (values: values.RemoveAt(i), shrinkers: shrinkers.RemoveAt(i));
                    yield return Generator.Constant(pair.values, Repeat(pair.values, pair.shrinkers));
                }
                // Try to shrink relevant generators.
                foreach (var generator in All(values, shrinkers).Shrink()) yield return generator;
            }
        }

        internal static Shrinker<T[]> All<T>(T[] values, Shrinker<T>[] shrinkers)
        {
            return From(Name<T>.All.Format(shrinkers), Shrink);

            IEnumerable<Generator<T[]>> Shrink()
            {
                for (int i = 0; i < shrinkers.Length; i++)
                {
                    foreach (var generator in shrinkers[i].Shrink())
                    {
                        yield return Generator.From(generator.Name, state =>
                        {
                            var pair = (values: CloneUtility.Shallow(values), shrinkers: CloneUtility.Shallow(shrinkers));
                            (pair.values[i], pair.shrinkers[i]) = generator.Generate(state);
                            return (pair.values, All(pair.values, pair.shrinkers));
                        });
                    }
                }
            }
        }

        internal static Shrinker<Outcome<T>> Mutate<T>((Generator<T> generator, Shrinker<T> shrinker) value, (Generator<Mutation<T>[]> generator, Shrinker<Mutation<T>[]> shrinker) mutations)
        {
            return From(Name<T>.Mutate.Format(value.shrinker, mutations.shrinker), Shrink);

            IEnumerable<Generator<Outcome<T>>> Shrink()
            {
                foreach (var generator in value.shrinker.Shrink()) yield return generator.Mutate(mutations.generator);
                foreach (var generator in mutations.shrinker.Shrink()) yield return value.generator.Mutate(generator);
            }
        }
    }
}