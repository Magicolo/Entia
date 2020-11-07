using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using Entia.Core;

namespace Entia.Check
{
    public delegate (T value, Shrinker<T> shrinker) Generate<T>(Generator.State state);
    public readonly struct Generator<T>
    {
        public static implicit operator Generator<T>(T value) => Generator.Constant(value);

        public readonly string Name;
        public readonly Generate<T> Generate;
        public Generator(string name, Generate<T> generate) { Name = name; Generate = generate; }
        public Generator<T> With(string name = null, Generate<T> generate = null) => new Generator<T>(name ?? Name, generate ?? Generate);
        public override string ToString() => Name;
    }

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
            static readonly (Type definition, Type[] arguments)[] _constructables = _definitions
                .Select(definition => (definition, arguments: definition.GetGenericArguments()))
                .Where(pair => pair.arguments.All(argument => argument.GetGenericParameterConstraints().None()))
                .OrderBy(pair => pair.arguments.Length)
                .ToArray();

            public static readonly Generator<Type> Type = Any(_types).With($"{nameof(Types)}.{nameof(Type)}");
            public static readonly Generator<Type> Abstract = Any(_abstracts).With($"{nameof(Types)}.{nameof(Abstract)}");
            public static readonly Generator<Type> Interface = Any(_interfaces).With($"{nameof(Types)}.{nameof(Interface)}");
            public static readonly Generator<Type> Primitive = Any(_primitives).With($"{nameof(Types)}.{nameof(Primitive)}");
            public static readonly Generator<Type> Enumeration = Any(_enumerations).With($"{nameof(Types)}.{nameof(Enumeration)}");
            public static readonly Generator<Type> Definition = Range(_constructables).Map(pair => pair.definition).With($"{nameof(Types)}.{nameof(Definition)}");
            public static readonly Generator<Type> Enumerable = Any(_enumerables).With($"{nameof(Types)}.{nameof(Enumerable)}");
            public static readonly Generator<Type> Serializable = Any(_serializables).With($"{nameof(Types)}.{nameof(Serializable)}");
            public static readonly Generator<Type> Reference = Any(_references).With($"{nameof(Types)}.{nameof(Reference)}");
            public static readonly Generator<Type> Value = Any(_values).With($"{nameof(Types)}.{nameof(Value)}");
            public static readonly Generator<Type> Concrete = Any(_concretes).With($"{nameof(Types)}.{nameof(Concrete)}");
            public static readonly Generator<Type> Array = Any(_arrays).With($"{nameof(Types)}.{nameof(Array)}");
            public static readonly Generator<Type> Pointer = Any(_pointers).With($"{nameof(Types)}.{nameof(Pointer)}");
            public static readonly Generator<Type> Default = Any(_defaults).With($"{nameof(Types)}.{nameof(Default)}");

            static readonly Generator<Type> _argument = Any(_arguments);
            static readonly Generator<Type> _referenceArgument = Any(_arguments.Intersect(_references).ToArray());
            static readonly Generator<Type> _valueArgument = Any(_arguments.Intersect(_values).ToArray());
            static readonly Generator<Type> _defaultArgument = Any(_arguments.Intersect(_defaults).ToArray());

            public static readonly Generator<Type> Generic = Definition.Bind(Make).With($"{nameof(Types)}.{nameof(Generic)}");
            public static readonly Generator<Type> Unmanaged = Any(Primitive, Enumeration, Pointer).With($"{nameof(Types)}.{nameof(Unmanaged)}");
            public static readonly Generator<Type> List = Make(typeof(List<>)).With($"{nameof(Types)}.{nameof(List)}");
            public static readonly Generator<Type> Dictionary = Make(typeof(Dictionary<,>)).With($"{nameof(Types)}.{nameof(Dictionary)}");
            public static readonly Generator<Type> Nullable = Make(typeof(Nullable<>)).With($"{nameof(Types)}.{nameof(Nullable)}");
            public static readonly Generator<Type> Option = Make(typeof(Option<>)).With($"{nameof(Types)}.{nameof(Option)}");
            public static readonly Generator<Type> Result = Make(typeof(Result<>)).With($"{nameof(Types)}.{nameof(Result)}");

            public static readonly Generator<Type> Tuple =
                Any(
                    Range(Make(typeof(Tuple<>)), Make(typeof(Tuple<,>)), Make(typeof(Tuple<,,>)), Make(typeof(Tuple<,,,>)), Make(typeof(Tuple<,,,,>)), Make(typeof(Tuple<,,,,,>)), Make(typeof(Tuple<,,,,,,>))),
                    Range(Make(typeof(ValueTuple<>)), Make(typeof(ValueTuple<,>)), Make(typeof(ValueTuple<,,>)), Make(typeof(ValueTuple<,,,>)), Make(typeof(ValueTuple<,,,,>)), Make(typeof(ValueTuple<,,,,,>)), Make(typeof(ValueTuple<,,,,,,>))))
                .Flatten()
                .With($"{nameof(Types)}.{nameof(Tuple)}");

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
                .Choose(arguments => Core.Option.Try(() => definition.MakeGenericType(arguments)))
                .With(nameof(Make).Format(definition.Name));
        }

        static class Cache<T>
        {
            public static readonly Generator<T> Default = Constant(default(T)).With(NameCache<T>.Default);
            public static readonly Generator<T[]> Empty = Constant(Array.Empty<T>()).With(NameCache<T>.Empty);
        }

        static class NameCache<T>
        {
            public static readonly string Parameters = $"<{typeof(T).Name}>";
            public static readonly string Constant = $"{nameof(Constant)}{Parameters}";
            public static readonly string Lazy = $"{nameof(Lazy)}{Parameters}";
            public static readonly string Default = $"{nameof(Default)}{Parameters}";
            public static readonly string Empty = $"{nameof(Empty)}{Parameters}";
            public static readonly string Adapt = $"{nameof(Adapt)}{Parameters}";
            public static readonly string Size = $"{nameof(Size)}{Parameters}";
            public static readonly string Depth = $"{nameof(Depth)}{Parameters}";
            public static readonly string Attenuate = $"{nameof(Attenuate)}{Parameters}";
            public static readonly string Range = $"{nameof(Range)}{Parameters}";
            public static readonly string Repeat = $"{nameof(Repeat)}{Parameters}";
            public static readonly string Flatten = $"{nameof(Flatten)}{Parameters}";
            public static readonly string Filter = $"{nameof(Filter)}{Parameters}";
            public static readonly string Any = $"{nameof(Any)}{Parameters}";
            public static readonly string All = $"{nameof(All)}{Parameters}";
            public static readonly string Box = $"{nameof(Box)}{Parameters}";
            public static readonly string Number = $"{nameof(Number)}{Parameters}";
        }

        static class NameCache<T1, T2>
        {
            public static readonly string Parameters = $"<{typeof(T1).Name}, {typeof(T2).Name}>";
            public static readonly string Map = $"{nameof(Map)}{Parameters}";
            public static readonly string Bind = $"{nameof(Bind)}{Parameters}";
            public static readonly string Choose = $"{nameof(Choose)}{Parameters}";
            public static readonly string And = $"{nameof(And)}{Parameters}";
        }

        static class NameCache<T1, T2, T3>
        {
            public static readonly string Parameters = $"<{typeof(T1).Name}, {typeof(T2).Name}, {typeof(T3).Name}>";
            public static readonly string And = $"{nameof(And)}{Parameters}";
        }

        static class NameCache<T1, T2, T3, T4>
        {
            public static readonly string Parameters = $"<{typeof(T1).Name}, {typeof(T2).Name}, {typeof(T3).Name}, {typeof(T4).Name}>";
            public static readonly string And = $"{nameof(And)}{Parameters}";
        }

        static class NameCache<T1, T2, T3, T4, T5>
        {
            public static readonly string Parameters = $"<{typeof(T1).Name}, {typeof(T2).Name}, {typeof(T3).Name}, {typeof(T4).Name}, {typeof(T5).Name}>";
            public static readonly string And = $"{nameof(And)}{Parameters}";
        }

        static class EnumCache<T> where T : struct, Enum
        {
            public static readonly Generator<T> Any = Enum.GetValues(typeof(T)).OfType<T>().Select(Constant).Any()
                .With($"{nameof(Enumeration)}{NameCache<T>.Parameters}");
        }

        public static readonly Generator<char> Letter = Any(Range('A', 'Z'), Range('a', 'z')).With(nameof(Letter));
        public static readonly Generator<char> Digit = Range('0', '9').With(nameof(Digit));
        public static readonly Generator<char> ASCII = Any(Letter, Digit, Range((char)127)).With(nameof(ASCII));
        public static readonly Generator<char> Character = Range(char.MinValue, char.MaxValue).With(nameof(Character));
        public static readonly Generator<bool> True = Constant(true).With(nameof(True));
        public static readonly Generator<bool> False = Constant(false).With(nameof(False));
        public static readonly Generator<bool> Boolean = Any(True, False).With(nameof(Boolean));
        public static readonly Generator<int> Zero = Constant(0).With(nameof(Zero));
        public static readonly Generator<int> One = Constant(1).With(nameof(One));
        public static readonly Generator<int> Integer = Number(int.MinValue, int.MaxValue, 0m).Map(value => (int)Math.Round(value)).Size(size => Math.Pow(size, 5)).With(nameof(Integer));
        public static readonly Generator<float> Rational = Number(-1e15m, 1e15m, 0m).Map(value => (float)value).Size(size => Math.Pow(size, 15)).With(nameof(Rational));
        public static readonly Generator<float> Infinity = Any(float.NegativeInfinity, float.PositiveInfinity).With(nameof(Infinity));

        static readonly Generator<Enum> _enumeration = Types.Enumeration.Bind(Enumeration).With(nameof(Enumeration));

        public static Generator<T> Default<T>() => Cache<T>.Default;
        public static Generator<T[]> Empty<T>() => Cache<T>.Empty;
        public static Generator<Array> Empty(Type type) => Constant(Array.CreateInstance(type, 0)).With(nameof(Empty).Format(type.Name));

        public static Generator<T> From<T>(string name, Generate<T> generate) => new Generator<T>(name, generate);
        public static Generator<T> Constant<T>(T value, Shrinker<T> shrinker) => From(Format(value), _ => (value, shrinker));
        public static Generator<T> Constant<T>(T value) => Constant(value, Shrinker.Empty<T>());

        public static Generator<T> Lazy<T>(Func<T> provide) => Lazy(() => Constant(provide()));
        public static Generator<T> Lazy<T>(Func<Generator<T>> provide)
        {
            var generator = new Lazy<Generator<T>>(provide);
            return From(NameCache<T>.Lazy, state => generator.Value.Generate(state));
        }

        public static Generator<T> Adapt<T>(this Generator<T> generator, Func<State, State> map) =>
            From(NameCache<T>.Adapt.Format(generator), state => generator.Generate(map(state)));
        public static Generator<T> Size<T>(this Generator<T> generator, Func<double, double> map) =>
            generator.Adapt(state => state.With(map(state.Size))).With(NameCache<T>.Size.Format(generator));
        public static Generator<T> Depth<T>(this Generator<T> generator) =>
            generator.Adapt(state => state.With(depth: state.Depth + 1)).With(NameCache<T>.Depth.Format(generator));
        public static Generator<T> Attenuate<T>(this Generator<T> generator, Generator<uint> depth) =>
            depth.Bind(depth => generator.Adapt(state => state.With(state.Size * Math.Max(1.0 - (double)state.Depth / depth, 0.0))))
                .With(NameCache<T>.Attenuate.Format(generator, depth));

        public static Generator<sbyte> Signed(this Generator<byte> generator) => generator.Map(value => (sbyte)value).With(nameof(Signed).Format(generator));
        public static Generator<short> Signed(this Generator<ushort> generator) => generator.Map(value => (short)value).With(nameof(Signed).Format(generator));
        public static Generator<int> Signed(this Generator<uint> generator) => generator.Map(value => (int)value).With(nameof(Signed).Format(generator));
        public static Generator<long> Signed(this Generator<ulong> generator) => generator.Map(value => (long)value).With(nameof(Signed).Format(generator));
        public static Generator<byte> Unsigned(this Generator<sbyte> generator) => generator.Map(value => (byte)value).With(nameof(Unsigned).Format(generator));
        public static Generator<ushort> Unsigned(this Generator<short> generator) => generator.Map(value => (ushort)value).With(nameof(Unsigned).Format(generator));
        public static Generator<uint> Unsigned(this Generator<int> generator) => generator.Map(value => (uint)value).With(nameof(Unsigned).Format(generator));
        public static Generator<ulong> Unsigned(this Generator<long> generator) => generator.Map(value => (ulong)value).With(nameof(Unsigned).Format(generator));

        public static Generator<T> Enumeration<T>() where T : struct, Enum => EnumCache<T>.Any;
        public static Generator<Enum> Enumeration(Type type) => Enum.GetValues(type).OfType<Enum>().Select(Constant).Any().With(nameof(Enumeration).Format(type.Name));
        public static Generator<Enum> Enumeration() => _enumeration;
        public static Generator<string> String(Generator<int> count) => Character.String(count);
        public static Generator<string> String(this Generator<char> generator, Generator<int> count) =>
            generator.Repeat(count).Map(characters => new string(characters)).With(nameof(String).Format(generator, count));

        public static Generator<char> Range(char maximum) => Range('\0', maximum);
        public static Generator<char> Range(char minimum, char maximum) =>
            Number(minimum, maximum, minimum).Map(value => (char)Math.Round(value)).With(NameCache<char>.Range.Format(minimum, maximum));
        public static Generator<float> Range(float maximum) => Range(0f, maximum);
        public static Generator<float> Range(float minimum, float maximum) =>
            Number((decimal)minimum, (decimal)maximum, (decimal)minimum).Map(value => (float)value).With(NameCache<float>.Range.Format(minimum, maximum));
        public static Generator<int> Range(int maximum) => Range(0, maximum);
        public static Generator<int> Range(int minimum, int maximum) =>
            Number(minimum, maximum, minimum).Map(value => (int)Math.Round(value)).With(NameCache<int>.Range.Format(minimum, maximum));
        public static Generator<T> Range<T>(params T[] values) =>
            Range(values.Length - 1).Map(index => values[index]).With(NameCache<T>.Range.Format(values));

        public static Generator<T[]> Repeat<T>(this Generator<T> generator, Generator<int> count) =>
            From(NameCache<T>.Repeat.Format(generator, count), state =>
            {
                var length = count.Generate(state).value;
                if (length == 0) return Empty<T>().Generate(state);

                var values = new T[length];
                var shrinkers = new Shrinker<T>[length];
                for (int i = 0; i < length; i++) (values[i], shrinkers[i]) = generator.Generate(state);
                return (values, Shrinker.Repeat(values, shrinkers));
            });

        public static Generator<TTarget> Map<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, TTarget> map) =>
            From(NameCache<TSource, TTarget>.Map.Format(generator), state =>
            {
                var (value, shrinker) = generator.Generate(state);
                return (map(value), shrinker.Map(map));
            });

        public static Generator<TTarget> Bind<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, Generator<TTarget>> bind) =>
            generator.Map(bind).Flatten().With(NameCache<TSource, TTarget>.Bind.Format(generator));

        public static Generator<TTarget> Choose<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, Option<TTarget>> choose) =>
            From(NameCache<TSource, TTarget>.Choose.Format(generator), state =>
            {
                while (true)
                {
                    var (source, shrinker) = generator.Generate(state);
                    if (choose(source).TryValue(out var target)) return (target, shrinker.Choose(choose));
                }
            });

        public static Generator<T> Flatten<T>(this Generator<Generator<T>> generator) =>
            From(NameCache<T>.Flatten.Format(generator), state =>
            {
                var pair1 = generator.Generate(state);
                var pair2 = pair1.value.Generate(state);
                return (pair2.value, pair1.shrinker.Flatten().And(pair2.shrinker));
            });

        public static Generator<T> Filter<T>(this Generator<T> generator, Func<T, bool> filter) =>
            From(NameCache<T>.Filter.Format(generator), state =>
            {
                while (true)
                {
                    var (value, shrinker) = generator.Generate(state);
                    if (filter(value)) return (value, shrinker);
                }
            });

        public static Generator<T> Any<T>(params T[] values) => Any(values.Select(Constant));
        public static Generator<T> Any<T>(this IEnumerable<Generator<T>> generators) => Any(generators.ToArray());
        public static Generator<T> Any<T>(params Generator<T>[] generators) =>
            generators.Length == 0 ? throw new ArgumentException(nameof(generators)) :
            generators.Length == 1 ? generators[0] :
            From(NameCache<T>.Any.Format(generators), state =>
            {
                var index = state.Random.Next(generators.Length);
                return generators[index].Generate(state);
            });

        public static Generator<T> Any<T>(params (float weight, Generator<T> generator)[] generators)
        {
            if (generators.Length == 0) throw new ArgumentException(nameof(generators));
            if (generators.Length == 1) return generators[0].generator;

            var sum = generators.Sum(pair => pair.weight);
            return From(NameCache<T>.Any.Format(generators), state =>
            {
                var random = state.Random.NextDouble() * sum;
                var current = 0d;
                return generators.First(pair => random < (current += pair.weight)).generator.Generate(state);
            });
        }

        public static Generator<object> Box<T>(this Generator<T> generator) =>
            generator.Map(value => (object)value).With(NameCache<T>.Box.Format(generator));

        public static Generator<(T1, T2)> And<T1, T2>(this Generator<T1> generator1, Generator<T2> generator2) =>
            All(generator1.Box(), generator2.Box()).Map(values => ((T1)values[0], (T2)values[1]))
                .With(NameCache<T1, T2>.And.Format(generator1, generator2));
        public static Generator<(T1, T2, T3)> And<T1, T2, T3>(this Generator<T1> generator1, Generator<T2> generator2, Generator<T3> generator3) =>
            All(generator1.Box(), generator2.Box(), generator3.Box()).Map(values => ((T1)values[0], (T2)values[1], (T3)values[2]))
                .With(NameCache<T1, T2, T3>.And.Format(generator1, generator2, generator3));
        public static Generator<(T1, T2, T3, T4)> And<T1, T2, T3, T4>(this Generator<T1> generator1, Generator<T2> generator2, Generator<T3> generator3, Generator<T4> generator4) =>
            All(generator1.Box(), generator2.Box(), generator3.Box(), generator4.Box()).Map(values => ((T1)values[0], (T2)values[1], (T3)values[2], (T4)values[3]))
                .With(NameCache<T1, T2, T3, T4>.And.Format(generator1, generator2, generator3, generator4));
        public static Generator<(T1, T2, T3, T4, T5)> And<T1, T2, T3, T4, T5>(this Generator<T1> generator1, Generator<T2> generator2, Generator<T3> generator3, Generator<T4> generator4, Generator<T5> generator5) =>
            All(generator1.Box(), generator2.Box(), generator3.Box(), generator4.Box(), generator5.Box()).Map(values => ((T1)values[0], (T2)values[1], (T3)values[2], (T4)values[3], (T5)values[4]))
                .With(NameCache<T1, T2, T3, T4, T5>.And.Format(generator1, generator2, generator3, generator4, generator5));

        public static Generator<T[]> All<T>(this IEnumerable<Generator<T>> generators) => All(generators.ToArray());
        public static Generator<T[]> All<T>(params Generator<T>[] generators) =>
            generators.Length == 0 ? Empty<T>() :
            From(NameCache<T>.All.Format(generators), state =>
            {
                var values = new T[generators.Length];
                var shrinkers = new Shrinker<T>[generators.Length];
                for (int i = 0; i < generators.Length; i++) (values[i], shrinkers[i]) = generators[i].Generate(state);
                return (values, Shrinker.All(values, shrinkers));
            });

        public static IEnumerable<T> Sample<T>(this Generator<T> generator, int count)
        {
            var random = new Random();
            var maximum = count * 0.9;
            for (int i = 0; i < count; i++)
            {
                var seed = random.Next() ^ Thread.CurrentThread.ManagedThreadId ^ i;
                var size = Math.Min(i / maximum, 1.0);
                var state = new Generator.State(size, 0, new Random(seed));
                yield return generator.Generate(state).value;
            }
        }

        static Generator<decimal> Number(decimal minimum, decimal maximum, decimal target)
        {
            if (minimum == maximum) return minimum;
            return From(NameCache<decimal>.Number.Format(minimum, maximum, target), state =>
            {
                var random = Interpolate(minimum, maximum, (decimal)state.Random.NextDouble());
                var value = Interpolate(target, random, (decimal)state.Size);
                return (value, Shrinker.Number(value, target));
            });

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static decimal Interpolate(decimal source, decimal target, decimal ratio) => (target - source) * ratio + source;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string Format<T>(T value) =>
#if DEBUG
            $"{value}";
#else
            "";
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string Format<T>(T[] values) =>
#if DEBUG
            $"{string.Join(", ", values)}";
#else
            "";
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string Format<T>(this string name, T value) =>
#if DEBUG
            $"{name}({Format(value)})";
#else
            name;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string Format<T1, T2>(this string name, T1 value1, T2 value2) =>
#if DEBUG
            $"{name}({Format(value1)}, {Format(value2)})";
#else
            name;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string Format<T1, T2, T3>(this string name, T1 value1, T2 value2, T3 value3) =>
#if DEBUG
            $"{name}({Format(value1)}, {Format(value2)}, {Format(value3)})";
#else
            name;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string Format<T1, T2, T3, T4>(this string name, T1 value1, T2 value2, T3 value3, T4 value4) =>
#if DEBUG
            $"{name}({Format(value1)}, {Format(value2)}, {Format(value3)}, {Format(value4)})";
#else
            name;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string Format<T1, T2, T3, T4, T5>(this string name, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5) =>
#if DEBUG
            $"{name}({Format(value1)}, {Format(value2)}, {Format(value3)}, {Format(value4)}, {Format(value5)})";
#else
            name;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string Format<T>(this string name, T[] values) =>
#if DEBUG
            $"{name}({Format(values)})";
#else
            name;
#endif
    }
}