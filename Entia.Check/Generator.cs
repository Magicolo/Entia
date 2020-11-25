using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using Entia.Core;
using static Entia.Check.Formatting;

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
            static FieldInfo[] _fields = typeof(Random).Fields(true, false)
                .Where(field => !field.FieldType.IsPrimitive)
                .ToArray();
            static Random Clone(Random random)
            {
                var clone = CloneUtility.Shallow(random);
                foreach (var field in _fields)
                    field.SetValue(clone, CloneUtility.Shallow(field.GetValue(random)));
                return clone;
            }

            public readonly double Size;
            public readonly uint Depth;
            public readonly Random Random;

            public State(double size, uint depth, Random random)
            {
                Size = size;
                Depth = depth;
                Random = random;
            }

            public State Clone() => new State(Size, Depth, Clone(Random));

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
            static readonly Type[] _flags = _enumerations.Where(type => type.IsDefined(typeof(FlagsAttribute))).ToArray();
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
            public static readonly Generator<Type> Flags = Any(_flags).With($"{nameof(Types)}.{nameof(Flags)}");
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
                .Select(Argument)
                .All()
                .Choose(arguments => Core.Option.Try(() => definition.MakeGenericType(arguments)))
                .With(nameof(Make).Format(definition.Name));

            public static Generator<MethodInfo> Make(MethodInfo definition) => definition.GetGenericArguments()
                .Select(Argument)
                .All()
                .Choose(arguments => Core.Option.Try(() => definition.MakeGenericMethod(arguments)))
                .With(nameof(Make).Format(definition.Name));

            public static Generator<Type> Argument() => _argument;
            public static Generator<Type> Argument(Type argument)
            {
                var attributes = argument.GenericParameterAttributes;
                if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
                    return _defaultArgument;
                if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                    return _referenceArgument;
                if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                    return _valueArgument;
                return _argument;
            }
        }

        static class GeneratorCache<T>
        {
            public static readonly Generator<T> Default = Constant(default(T)).With(Name<T>.Default);
            public static readonly Generator<T[]> Empty = Constant(Array.Empty<T>()).With(Name<T>.Empty);
        }

        static class EnumCache<T> where T : struct, Enum, IConvertible
        {
            public static readonly Generator<T> Enumeration = Generator.Enumeration(typeof(T)).Map(value => (T)value).With(Name<T>.Enumeration);
            public static readonly Generator<T> Flags = Generator.Flags(typeof(T)).Map(value => (T)value).With(Name<T>.Flag);
        }

        static readonly Generator<Enum> _enumeration = Types.Enumeration.Bind(Enumeration).With(nameof(Enumeration));
        static readonly Generator<Enum> _flags = Types.Flags.Bind(Flags).With(nameof(Flags));
        static readonly Generator<double> _number = Number(int.MinValue, int.MaxValue, 0).Size(size => Math.Pow(size, 15) + (1.0 - size) * 9e-9);

        public static readonly Generator<char> Letter = Any(Range('A', 'Z'), Range('a', 'z')).With(nameof(Letter));
        public static readonly Generator<char> Digit = Range('0', '9').With(nameof(Digit));
        public static readonly Generator<char> ASCII = Any(Letter, Digit, Range((char)127)).With(nameof(ASCII));
        public static readonly Generator<char> Character = Range(char.MinValue, char.MaxValue).With(nameof(Character));
        public static readonly Generator<bool> True = Constant(true).With(nameof(True));
        public static readonly Generator<bool> False = Constant(false).With(nameof(False));
        public static readonly Generator<bool> Boolean = Any(True, False).With(nameof(Boolean));
        public static readonly Generator<int> Zero = Constant(0).With(nameof(Zero));
        public static readonly Generator<int> One = Constant(1).With(nameof(One));
        public static readonly Generator<int> Integer = _number.Map(value => (int)Math.Round(value)).With(nameof(Integer));
        public static readonly Generator<float> Rational = _number.Map(value => (float)value).With(nameof(Rational));
        public static readonly Generator<float> Infinity = Any(float.NegativeInfinity, float.PositiveInfinity).With(nameof(Infinity));
        public static readonly Generator<Assembly> Assembly = ReflectionUtility.AllAssemblies.Select(Constant).Any().With(nameof(Assembly));

        public static Generator<T> Default<T>() => GeneratorCache<T>.Default;
        public static Generator<T[]> Empty<T>() => GeneratorCache<T>.Empty;
        public static Generator<Array> Empty(Type type) => Constant(Array.CreateInstance(type, 0)).With(nameof(Empty).Format(type.Name));
        public static Generator<T> From<T>(string name, Generate<T> generate) => new Generator<T>(name, generate);
        public static Generator<T> Constant<T>(T value) => Constant(value, Shrinker.Empty<T>());
        public static Generator<T> Constant<T>(T value, Shrinker<T> shrinker) => From(Format(value), _ => (value, shrinker));
        public static Generator<T> Factory<T>(Func<T> create) => Factory(create, Shrinker.Empty<T>());
        public static Generator<T> Factory<T>(Func<T> create, Shrinker<T> shrinker) => From(Name<T>.Factory, _ => (create(), shrinker));
        public static Generator<T> Lazy<T>(Func<T> provide) => Lazy(() => Constant(provide()));
        public static Generator<T> Lazy<T>(Func<Generator<T>> provide)
        {
            var generator = new Lazy<Generator<T>>(provide);
            return From(Name<T>.Lazy, state => generator.Value.Generate(state));
        }

        public static Generator<T> Adapt<T>(this Generator<T> generator, Func<State, State> map) =>
            From(Name<T>.Adapt.Format(generator), state => generator.Generate(map(state)));
        public static Generator<T> Adapt<T>(this Generator<T> generator, State state) => generator.Adapt(_ => state);
        public static Generator<T> Size<T>(this Generator<T> generator, Func<double, double> map) =>
            generator.Adapt(state => state.With(map(state.Size))).With(Name<T>.Size.Format(generator));
        public static Generator<T> Size<T>(this Generator<T> generator, double size) => generator.Size(_ => size);
        public static Generator<T> Depth<T>(this Generator<T> generator) =>
            generator.Adapt(state => state.With(depth: state.Depth + 1)).With(Name<T>.Depth.Format(generator));
        public static Generator<T> Attenuate<T>(this Generator<T> generator, Generator<uint> depth) =>
            depth.Bind(depth => generator.Adapt(state => state.With(state.Size * Math.Max(1.0 - (double)state.Depth / depth, 0.0))))
                .With(Name<T>.Attenuate.Format(generator, depth));
        public static Generator<T> Shrink<T>(this Generator<T> generator, Shrinker<T> shrinker) =>
            From(Name<T>.Shrink.Format(generator), state =>
            {
                var pair = generator.Generate(state);
                return (pair.value, shrinker);
            });
        public static Generator<T> Shrink<T>(this Generator<T> generator, Shrink<T> shrink) =>
            generator.Shrink(Shrinker.From(Name<T>.Shrink, shrink));

        public static Generator<float> Inverse(this Generator<float> generator) => generator.Map(value => 1f / value).With(nameof(Inverse).Format(generator));
        public static Generator<double> Inverse(this Generator<double> generator) => generator.Map(value => 1.0 / value).With(nameof(Inverse).Format(generator));
        public static Generator<sbyte> Signed(this Generator<byte> generator) => generator.Map(value => (sbyte)value).With(nameof(Signed).Format(generator));
        public static Generator<short> Signed(this Generator<ushort> generator) => generator.Map(value => (short)value).With(nameof(Signed).Format(generator));
        public static Generator<int> Signed(this Generator<uint> generator) => generator.Map(value => (int)value).With(nameof(Signed).Format(generator));
        public static Generator<long> Signed(this Generator<ulong> generator) => generator.Map(value => (long)value).With(nameof(Signed).Format(generator));
        public static Generator<byte> Unsigned(this Generator<sbyte> generator) => generator.Map(value => (byte)value).With(nameof(Unsigned).Format(generator));
        public static Generator<ushort> Unsigned(this Generator<short> generator) => generator.Map(value => (ushort)value).With(nameof(Unsigned).Format(generator));
        public static Generator<uint> Unsigned(this Generator<int> generator) => generator.Map(value => (uint)value).With(nameof(Unsigned).Format(generator));
        public static Generator<ulong> Unsigned(this Generator<long> generator) => generator.Map(value => (ulong)value).With(nameof(Unsigned).Format(generator));

        public static Generator<T> Enumeration<T>() where T : struct, Enum => EnumCache<T>.Enumeration;
        public static Generator<Enum> Enumeration(Type type) => Any(Enum.GetValues(type).OfType<Enum>().ToArray()).With(nameof(Enumeration).Format(type.Name));
        public static Generator<Enum> Enumeration() => _enumeration;
        public static Generator<T> Flags<T>() where T : struct, Enum => EnumCache<T>.Flags;
        public static Generator<Enum> Flags(Type type)
        {
            var values = Enum.GetValues(type).OfType<Enum>().ToArray();
            return Any(values)
                .Repeat(values.Length)
                .Map(values => (Enum)Enum.ToObject(type, values.Aggregate(0L, (flags, value) => flags | Convert.ToInt64(value))))
                .With(nameof(Flags).Format(type.Name));
        }
        public static Generator<Enum> Flags() => _flags;

        public static Generator<string> String(Generator<int> count) => Character.String(count);
        public static Generator<string> String(this Generator<char> generator, Generator<int> count) =>
            generator.Repeat(count).Map(characters => new string(characters)).With(nameof(String).Format(generator, count));

        public static Generator<char> Range(char maximum) => Range('\0', maximum);
        public static Generator<char> Range(char minimum, char maximum) =>
            Number(minimum, maximum, minimum).Map(value => (char)Math.Round(value)).With(Name<char>.Range.Format(minimum, maximum));
        public static Generator<float> Range(float maximum) => Range(0f, maximum);
        public static Generator<float> Range(float minimum, float maximum) =>
            Number(minimum, maximum, minimum).Map(value => (float)value).With(Name<float>.Range.Format(minimum, maximum));
        public static Generator<int> Range(int maximum) => Range(0, maximum);
        public static Generator<int> Range(int minimum, int maximum) =>
            Number(minimum, maximum, minimum).Map(value => (int)Math.Round(value)).With(Name<int>.Range.Format(minimum, maximum));
        public static Generator<T> Range<T>(params T[] values) =>
            Range(values.Length - 1).Map(index => values[index]).With(Name<T>.Range.Format(values));

        public static Generator<TTarget> Map<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, State, TTarget> map) =>
            From(Name<TSource, TTarget>.Map.Format(generator), state =>
            {
                var (value, shrinker) = generator.Generate(state);
                return (map(value, state), shrinker.Map(map));
            });
        public static Generator<TTarget> Map<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, TTarget> map) =>
            generator.Map((value, _) => map(value));

        public static Generator<TTarget> Bind<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, Generator<TTarget>> bind) =>
            generator.Map(bind).Flatten().With(Name<TSource, TTarget>.Bind.Format(generator));
        public static Generator<TTarget> Bind<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, State, Generator<TTarget>> bind) =>
            generator.Map(bind).Flatten().With(Name<TSource, TTarget>.Bind.Format(generator));

        public static Generator<TTarget> Choose<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, State, Option<TTarget>> choose) =>
            From(Name<TSource, TTarget>.Choose.Format(generator), state =>
            {
                while (true)
                {
                    var (source, shrinker) = generator.Generate(state);
                    if (choose(source, state).TryValue(out var target)) return (target, shrinker.Choose(choose));
                }
            });
        public static Generator<TTarget> Choose<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, Option<TTarget>> choose) =>
            generator.Choose((value, _) => choose(value));

        public static Generator<T> Flatten<T>(this Generator<Generator<T>> generator) =>
            From(Name<T>.Flatten.Format(generator), state =>
            {
                var pair1 = generator.Generate(state);
                var pair2 = pair1.value.Generate(state);
                return (pair2.value, pair1.shrinker.Flatten().And(pair2.shrinker));
            });

        public static Generator<T> Filter<T>(this Generator<T> generator, Func<T, bool> filter) =>
            From(Name<T>.Filter.Format(generator), state =>
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
            From(Name<T>.Any.Format(generators), state =>
            {
                var index = state.Random.Next(generators.Length);
                return generators[index].Generate(state);
            });

        public static Generator<T> Any<T>(params (float weight, Generator<T> generator)[] generators)
        {
            if (generators.Length == 0) throw new ArgumentException(nameof(generators));
            if (generators.Length == 1) return generators[0].generator;

            var sum = generators.Sum(pair => pair.weight);
            return From(Name<T>.Any.Format(generators), state =>
            {
                var random = state.Random.NextDouble() * sum;
                var current = 0d;
                return generators.First(pair => random < (current += pair.weight)).generator.Generate(state);
            });
        }

        public static Generator<object> Box<T>(this Generator<T> generator) =>
            generator.Map(value => (object)value).With(Name<T>.Box.Format(generator));

        public static Generator<(T1, T2)> And<T1, T2>(this Generator<T1> generator1, Generator<T2> generator2) =>
            All(generator1.Box(), generator2.Box()).Map(values => ((T1)values[0], (T2)values[1]))
                .With(Name<T1, T2>.And.Format(generator1, generator2));
        public static Generator<(T1, T2, T3)> And<T1, T2, T3>(this Generator<T1> generator1, Generator<T2> generator2, Generator<T3> generator3) =>
            All(generator1.Box(), generator2.Box(), generator3.Box()).Map(values => ((T1)values[0], (T2)values[1], (T3)values[2]))
                .With(Name<T1, T2, T3>.And.Format(generator1, generator2, generator3));
        public static Generator<(T1, T2, T3, T4)> And<T1, T2, T3, T4>(this Generator<T1> generator1, Generator<T2> generator2, Generator<T3> generator3, Generator<T4> generator4) =>
            All(generator1.Box(), generator2.Box(), generator3.Box(), generator4.Box()).Map(values => ((T1)values[0], (T2)values[1], (T3)values[2], (T4)values[3]))
                .With(Name<T1, T2, T3, T4>.And.Format(generator1, generator2, generator3, generator4));
        public static Generator<(T1, T2, T3, T4, T5)> And<T1, T2, T3, T4, T5>(this Generator<T1> generator1, Generator<T2> generator2, Generator<T3> generator3, Generator<T4> generator4, Generator<T5> generator5) =>
            All(generator1.Box(), generator2.Box(), generator3.Box(), generator4.Box(), generator5.Box()).Map(values => ((T1)values[0], (T2)values[1], (T3)values[2], (T4)values[3], (T5)values[4]))
                .With(Name<T1, T2, T3, T4, T5>.And.Format(generator1, generator2, generator3, generator4, generator5));

        public static Generator<T[]> All<T>(this IEnumerable<Generator<T>> generators) => All(generators.ToArray());
        public static Generator<T[]> All<T>(params Generator<T>[] generators) =>
            generators.Length == 0 ? Empty<T>() :
            From(Name<T>.All.Format(generators), state =>
            {
                var initial = state.Clone();
                var values = new T[generators.Length];
                var shrinkers = new Shrinker<T>[generators.Length];
                for (int i = 0; i < generators.Length; i++) (values[i], shrinkers[i]) = generators[i].Generate(state);
                // return (values, Shrinker.All2(generators, shrinkers, initial));
                return (values, Shrinker.All(values, shrinkers));
            });

        public static Generator<T[]> Repeat<T>(this Generator<T> generator, Generator<int> count) =>
            From(Name<T>.Repeat.Format(generator, count), state =>
            {
                var length = count.Generate(state).value;
                if (length == 0) return Empty<T>().Generate(state);

                var initial = state.Clone();
                var values = new T[length];
                var shrinkers = new Shrinker<T>[length];
                for (int i = 0; i < length; i++) (values[i], shrinkers[i]) = generator.Generate(state);
                // return (values, Shrinker.Repeat2(generator, shrinkers, initial));
                return (values, Shrinker.Repeat(values, shrinkers));
            });

        public static Generator<T> Cache<T>(this Generator<T> generator, double ratio = 0.5, uint size = 64)
        {
            if (size == 0) return generator;

            var cache = new Tuple<T, Shrinker<T>>[size];
            var count = 0;
            return From("", state =>
            {
                if (state.Random.NextDouble() < ratio &&
                    state.Random.Next(count) is var index &&
                    cache[index % size] is Tuple<T, Shrinker<T>> tuple)
                    return (tuple.Item1, tuple.Item2);
                else
                {
                    var pair = generator.Generate(state);
                    index = Interlocked.Increment(ref count) - 1;
                    Interlocked.Exchange(ref cache[index % size], Tuple.Create(pair.value, pair.shrinker));
                    return pair;
                }
            });
        }

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

        static Generator<double> Number(double minimum, double maximum, double target)
        {
            if (minimum == maximum) return minimum;
            return From(nameof(Number).Format(minimum, maximum, target), state =>
            {
                var random = Interpolate(minimum, maximum, state.Random.NextDouble());
                var value = Interpolate(target, random, state.Size);
                return (value, Shrinker.Number(value, target));
            });

            static double Interpolate(double source, double target, double ratio) => (target - source) * ratio + source;
        }
    }
}