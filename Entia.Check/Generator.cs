using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using Entia.Core;

namespace Entia.Check
{
    public delegate (T value, IEnumerable<Generator<T>> shrinked) Generator<T>(Generator.State state);

    public static class Generator
    {
        public sealed class State
        {
            public readonly double Size;
            public readonly uint Depth;
            public readonly Random Random;

            public State(double size, uint depth, Random random)
            {
                Size = size;
                Depth = depth;
                Random = random;
            }

            public State With(double? size = null, uint? depth = null) =>
                new State(size ?? Size, depth ?? Depth, Random);
        }

        public static class Types
        {
            static readonly Type[] _types = ReflectionUtility.AllTypes
                .Except(typeof(void), typeof(TypedReference))
                .Where(type => type.IsPublic)
                .DistinctBy(type => type.GUID)
                .ToArray();
            static readonly Type[] _definitions = _types.Where(type => type.IsGenericTypeDefinition).ToArray();
            static readonly Type[] _constructables = _definitions.Where(definition => definition.GetGenericArguments().All(argument => argument.GetGenericParameterConstraints().None())).ToArray();
            static readonly Type[] _abstracts = _types.Except(_definitions).Where(type => type.IsAbstract).ToArray();
            static readonly Type[] _interfaces = _abstracts.Where(type => type.IsInterface).ToArray();
            static readonly Type[] _concretes = _types.Except(_abstracts).Except(_definitions).ToArray();
            static readonly Type[] _references = _concretes.Where(type => type.IsClass).ToArray();
            static readonly Type[] _values = _concretes.Where(type => type.IsValueType).ToArray();
            static readonly Type[] _enumerations = _values.Where(type => type.IsEnum).ToArray();
            static readonly Type[] _primitives = _values.Where(type => type.IsPrimitive).ToArray();
            static readonly Type[] _enumerables = _concretes.Where(type => type.Is<IEnumerable>()).ToArray();
            static readonly Type[] _serializables = _concretes.Where(type => type.Is<ISerializable>()).ToArray();
            static readonly Type[] _arguments = _types.Where(type => !type.IsByRef && !type.IsPointer && !type.IsGenericTypeDefinition).ToArray();
            static readonly Type[] _arrays = _arguments.Select(type => type.ArrayType()).Choose().ToArray();
            static readonly Type[] _pointers = _values.Select(type => type.PointerType()).Choose().ToArray();
            static readonly Type[] _defaults = _concretes.Where(type => type.DefaultConstructor().TryValue(out var constructor) && constructor.IsPublic).ToArray();

            public static readonly Generator<Type> Type = Any(_types);
            public static readonly Generator<Type> Abstract = Any(_abstracts);
            public static readonly Generator<Type> Interface = Any(_interfaces);
            public static readonly Generator<Type> Primitive = Any(_primitives);
            public static readonly Generator<Type> Enumeration = Any(_enumerations);
            public static readonly Generator<Type> Definition = Any(_constructables);
            public static readonly Generator<Type> Enumerable = Any(_enumerables);
            public static readonly Generator<Type> Serializable = Any(_serializables);
            public static readonly Generator<Type> Reference = Any(_references);
            public static readonly Generator<Type> Value = Any(_values);
            public static readonly Generator<Type> Concrete = Any(_concretes);
            public static readonly Generator<Type> Array = Any(_arrays);
            public static readonly Generator<Type> Pointer = Any(_pointers);
            public static readonly Generator<Type> Default = Any(_defaults);

            static readonly Generator<Type> _argument = Any(_arguments);
            static readonly Generator<Type> _referenceArgument = Any(_arguments.Intersect(_references).ToArray());
            static readonly Generator<Type> _valueArgument = Any(_arguments.Intersect(_values).ToArray());
            static readonly Generator<Type> _defaultArgument = Any(_arguments.Intersect(_defaults).ToArray());

            public static readonly Generator<Type> Generic = Definition.Bind(Make);
            public static readonly Generator<Type> Unmanaged = Any(Primitive, Enumeration, Pointer);
            public static readonly Generator<Type> List = Make(typeof(List<>));
            public static readonly Generator<Type> Dictionary = Make(typeof(Dictionary<,>));
            public static readonly Generator<Type> Nullable = Make(typeof(Nullable<>));
            public static readonly Generator<Type> Option = Make(typeof(Option<>));
            public static readonly Generator<Type> Result = Make(typeof(Result<>));

            static readonly Generator<Type>[] _referenceTuples =
            {
                Make(typeof(Tuple<>)),
                Make(typeof(Tuple<,>)),
                Make(typeof(Tuple<,,>)),
                Make(typeof(Tuple<,,,>)),
                Make(typeof(Tuple<,,,,>)),
                Make(typeof(Tuple<,,,,,>)),
                Make(typeof(Tuple<,,,,,,>)),
            };
            static readonly Generator<Type>[] _valueTuples =
            {
                Make(typeof(ValueTuple<>)),
                Make(typeof(ValueTuple<,>)),
                Make(typeof(ValueTuple<,,>)),
                Make(typeof(ValueTuple<,,,>)),
                Make(typeof(ValueTuple<,,,,>)),
                Make(typeof(ValueTuple<,,,,,>)),
                Make(typeof(ValueTuple<,,,,,,>)),
            };
            public static readonly Generator<Type> Tuple = Any(
                Range(0, 6).Bind(index => _referenceTuples[index]),
                Range(0, 6).Bind(index => _valueTuples[index]));

            public static Generator<Type> Make(Type definition) => definition.GetGenericArguments()
                .Select(argument =>
                {
                    var attributes = argument.GenericParameterAttributes;
                    if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
                        return _defaultArgument;
                    if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                        return _referenceArgument;
                    if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                        return _valueArgument;
                    return _argument;
                })
                .All()
                .Choose(arguments => Core.Option.Try(() => definition.MakeGenericType(arguments)));
        }

        static class Cache<T>
        {
            public static readonly Generator<T> Default = Constant(default(T));
            public static readonly Generator<T[]> Empty = Constant(Array.Empty<T>());
        }

        static class EnumCache<T> where T : struct, Enum
        {
            public static readonly Generator<T> Any = Enum.GetValues(typeof(T)).OfType<T>().Select(Constant).Any();
        }

        public static readonly Generator<char> Letter = Any(Range('A', 'Z'), Range('a', 'z'));
        public static readonly Generator<char> Digit = Range('0', '9');
        public static readonly Generator<char> ASCII = Any(Letter, Digit, Range((char)127));
        public static readonly Generator<char> Character = Range(char.MinValue, char.MaxValue);
        public static readonly Generator<bool> True = Constant(true);
        public static readonly Generator<bool> False = Constant(false);
        public static readonly Generator<bool> Boolean = Any(True, False);
        public static readonly Generator<int> Zero = Constant(0);
        public static readonly Generator<int> One = Constant(1);
        public static readonly Generator<int> Integer = Number(int.MinValue, int.MaxValue, 0m).Map(value => (int)Math.Round(value)).Size(size => Math.Pow(size, 5));
        public static readonly Generator<float> Rational = Number(-1e15m, 1e15m, 0m).Map(value => (float)value).Size(size => Math.Pow(size, 15));
        public static readonly Generator<float> Infinity = Any(float.NegativeInfinity, float.PositiveInfinity);

        public static Generator<T> Default<T>() => Cache<T>.Default;
        public static Generator<T[]> Empty<T>() => Cache<T>.Empty;
        public static Generator<Array> Empty(Type type) => Constant(Array.CreateInstance(type, 0));

        public static Generator<T> Constant<T>(T value, IEnumerable<Generator<T>> shrinked) => _ => (value, shrinked);
        public static Generator<T> Constant<T>(T value) => Constant(value, Array.Empty<Generator<T>>());

        public static Generator<T> Lazy<T>(Func<T> provide) => Lazy(() => Constant(provide()));
        public static Generator<T> Lazy<T>(Func<Generator<T>> provide)
        {
            var generator = new Lazy<Generator<T>>(provide);
            return state => generator.Value(state);
        }

        public static Generator<T> Size<T>(this Generator<T> generator, Func<double, double> map) =>
            state => generator(state.With(map(state.Size)));
        public static Generator<T> Depth<T>(this Generator<T> generator) => state =>
            generator(state.With(depth: state.Depth + 1));
        public static Generator<T> Attenuate<T>(this Generator<T> generator, uint depth) => state =>
            generator(state.With(state.Size * Math.Max(1.0 - (double)state.Depth / depth, 0.0)));

        public static Generator<T> Enumeration<T>() where T : struct, Enum => EnumCache<T>.Any;
        public static Generator<Enum> Enumeration(Type type) => Enum.GetValues(type).OfType<Enum>().Select(Constant).Any();

        public static Generator<char> Range(char maximum) => Range('\0', maximum);
        public static Generator<char> Range(char minimum, char maximum) =>
            Number(minimum, maximum, minimum).Map(value => (char)Math.Round(value));
        public static Generator<float> Range(float maximum) => Range(0f, maximum);
        public static Generator<float> Range(float minimum, float maximum) =>
            Number((decimal)minimum, (decimal)maximum, (decimal)minimum).Map(value => (float)value);
        public static Generator<int> Range(int maximum) => Range(0, maximum);
        public static Generator<int> Range(int minimum, int maximum) =>
            Number(minimum, maximum, minimum).Map(value => (int)Math.Round(value));

        public static Generator<string> String(int count) => Character.String(count);
        public static Generator<string> String(Generator<int> count) => Character.String(count);
        public static Generator<string> String(this Generator<char> character, int count) =>
            character.Repeat(count).Map(characters => new string(characters));
        public static Generator<string> String(this Generator<char> character, Generator<int> count) =>
            character.Repeat(count).Map(characters => new string(characters));

        public static Generator<T[]> Repeat<T>(this Generator<T> generator, int count) =>
            count == 0 ? Cache<T>.Empty : generator.Repeat(Constant(count));
        public static Generator<T[]> Repeat<T>(this Generator<T> generator, Generator<int> count) => state =>
        {
            var length = count(state).value;
            if (length == 0) return Cache<T>.Empty(state);

            var values = new T[length];
            var shrinked = new IEnumerable<Generator<T>>[length];
            for (int i = 0; i < length; i++) (values[i], shrinked[i]) = generator(state);
            return (values, ShrinkRepeat(values, shrinked));
        };

        public static Generator<TTarget> Map<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, TTarget> map) =>
            state =>
            {
                var (value, shrinked) = generator(state);
                return (map(value), shrinked.Select(shrink => shrink.Map(map)));
            };

        public static Generator<TTarget> Bind<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, Generator<TTarget>> bind) =>
            generator.Map(bind).Flatten();

        public static Generator<TTarget> Choose<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, Option<TTarget>> map) =>
            state =>
            {
                while (true)
                {
                    var (source, shrinked) = generator(state);
                    if (map(source).TryValue(out var target)) return (target, shrinked.Select(shrink => shrink.Choose(map)));
                }
            };

        public static Generator<T> Flatten<T>(this Generator<Generator<T>> generator) => state =>
        {
            var pair1 = generator(state);
            var pair2 = pair1.value(state);
            return (pair2.value, pair1.shrinked.Select(shrink => shrink.Flatten()).Concat(pair2.shrinked));
        };

        public static Generator<T> Filter<T>(this Generator<T> generator, Func<T, bool> filter) => state =>
        {
            while (true)
            {
                var (value, shrinked) = generator(state);
                if (filter(value)) return (value, shrinked);
            }
        };

        public static Generator<T> Any<T>(params T[] values) => Any(values.Select(Constant));
        public static Generator<T> Any<T>(this IEnumerable<Generator<T>> generators) => Any(generators.ToArray());
        public static Generator<T> Any<T>(params Generator<T>[] generators) =>
            generators.Length == 0 ? throw new ArgumentException(nameof(generators)) :
            generators.Length == 1 ? generators[0] :
            state =>
            {
                var index = state.Random.Next(generators.Length);
                return generators[index](state);
            };

        public static Generator<T> Any<T>(params (float weight, Generator<T> generator)[] generators)
        {
            if (generators.Length == 0) throw new ArgumentException(nameof(generators));
            if (generators.Length == 1) return generators[0].generator;

            var sum = generators.Sum(pair => pair.weight);
            return state =>
            {
                var random = state.Random.NextDouble() * sum;
                var current = 0d;
                return generators.First(pair => random < (current += pair.weight)).generator(state);
            };
        }

        public static Generator<object> Box<T>(this Generator<T> generator) => generator.Map(value => (object)value);

        public static Generator<(T1, T2)> And<T1, T2>(this Generator<T1> generator1, Generator<T2> generator2) =>
            All(generator1.Box(), generator2.Box()).Map(values => ((T1)values[0], (T2)values[1]));
        public static Generator<(T1, T2, T3)> And<T1, T2, T3>(this Generator<T1> generator1, Generator<T2> generator2, Generator<T3> generator3) =>
            All(generator1.Box(), generator2.Box(), generator3.Box()).Map(values => ((T1)values[0], (T2)values[1], (T3)values[2]));
        public static Generator<(T1, T2, T3, T4)> And<T1, T2, T3, T4>(this Generator<T1> generator1, Generator<T2> generator2, Generator<T3> generator3, Generator<T4> generator4) =>
            All(generator1.Box(), generator2.Box(), generator3.Box(), generator4.Box()).Map(values => ((T1)values[0], (T2)values[1], (T3)values[2], (T4)values[3]));
        public static Generator<(T1, T2, T3, T4, T5)> And<T1, T2, T3, T4, T5>(this Generator<T1> generator1, Generator<T2> generator2, Generator<T3> generator3, Generator<T4> generator4, Generator<T5> generator5) =>
            All(generator1.Box(), generator2.Box(), generator3.Box(), generator4.Box(), generator5.Box()).Map(values => ((T1)values[0], (T2)values[1], (T3)values[2], (T4)values[3], (T5)values[4]));

        public static Generator<T[]> All<T>(this IEnumerable<Generator<T>> generators) => All(generators.ToArray());
        public static Generator<T[]> All<T>(params Generator<T>[] generators) =>
            generators.Length == 0 ? Empty<T>() :
            state =>
            {
                var values = new T[generators.Length];
                var shrinked = new IEnumerable<Generator<T>>[generators.Length];
                for (int i = 0; i < generators.Length; i++) (values[i], shrinked[i]) = generators[i](state);
                return (values, ShrinkAll(values, shrinked));
            };

        public static IEnumerable<T> Sample<T>(this Generator<T> generator, int count)
        {
            var random = new Random();
            var maximum = count * 0.9;
            for (int i = 0; i < count; i++)
            {
                var seed = random.Next() ^ Thread.CurrentThread.ManagedThreadId ^ i;
                var size = Math.Min(i / maximum, 1.0);
                var state = new Generator.State(size, 0, new Random(seed));
                yield return generator(state).value;
            }
        }

        static Generator<decimal> Number(decimal minimum, decimal maximum, decimal target)
        {
            if (minimum == maximum) return Constant(minimum);
            return state =>
            {
                var random = Interpolate(minimum, maximum, (decimal)state.Random.NextDouble());
                var value = Interpolate(target, random, (decimal)state.Size);
                return (value, ShrinkNumber(value, target));
            };

            static decimal Interpolate(decimal source, decimal target, decimal ratio) => (target - source) * ratio + source;
        }

        static IEnumerable<Generator<decimal>> ShrinkNumber(decimal source, decimal target)
        {
            var difference = target - source;
            var sign = Math.Sign(difference);
            var magnitude = Math.Abs(difference);
            var direction = magnitude / 100m;
            for (int i = 0; i < 100 && magnitude > 0; i++, magnitude -= direction)
            {
                var middle = Math.Round(magnitude * 0.5m * sign + source, 9);
                if (middle == source) yield break;
                yield return Constant(middle, ShrinkNumber(middle, target));
            }
        }

        static IEnumerable<Generator<T[]>> ShrinkRepeat<T>(T[] values, IEnumerable<Generator<T>>[] shrinked)
        {
            // Try to remove irrelevant generators.
            for (int i = 0; i < values.Length; i++)
            {
                var pair = (values.RemoveAt(i), shrinked.RemoveAt(i));
                yield return Constant(pair.Item1, ShrinkRepeat(pair.Item1, pair.Item2));
            }
            // Try to shrink relevant generators.
            foreach (var generator in ShrinkAll(values, shrinked)) yield return generator;
        }

        static IEnumerable<Generator<T[]>> ShrinkAll<T>(T[] values, IEnumerable<Generator<T>>[] shrinked)
        {
            for (int i = 0; i < shrinked.Length; i++)
            {
                foreach (var shrink in shrinked[i])
                {
                    yield return new Generator<T[]>(state =>
                    {
                        var pair = (CloneUtility.Shallow(values), CloneUtility.Shallow(shrinked));
                        (pair.Item1[i], pair.Item2[i]) = shrink(state);
                        return (pair.Item1, ShrinkAll(pair.Item1, pair.Item2));
                    });
                }
            }
        }
    }
}