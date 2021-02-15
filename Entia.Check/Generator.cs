using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public Generator<T> With(string? name = null, Generate<T>? generate = null) => new(name ?? Name, generate ?? Generate);
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
                new(size ?? Size, depth ?? Depth, Random);
        }

        public static class Types
        {
            public enum Filter { None, Abstract, Concrete }

            static class AllDerivedCache<T>
            {
                public static readonly Generator<Type> Derived = Types.Derived(typeof(T), Filter.None);
            }

            static class ConcreteDerivedCache<T>
            {
                public static readonly Generator<Type> Derived = Types.Derived(typeof(T), Filter.Concrete);
            }

            static class AbstractDerivedCache<T>
            {
                public static readonly Generator<Type> Derived = Types.Derived(typeof(T), Filter.Abstract);
            }

            static readonly Type[] _types = ReflectionUtility.AllTypes
                .Except(typeof(void), typeof(TypedReference))
                .Where(type => type.IsVisible)
                .DistinctBy(type => type.GUID)
                .ToArray();
            static readonly Type[] _instances = _types.Where(type => !type.IsStatic()).ToArray();
            static readonly Type[] _definitions = _instances.Where(type => type.IsGenericTypeDefinition).ToArray();
            static readonly Type[] _abstracts = _instances.Except(_definitions).Where(type => type.IsAbstract).ToArray();
            static readonly Type[] _interfaces = _abstracts.Where(type => type.IsInterface).ToArray();
            static readonly Type[] _concretes = _instances.Except(_abstracts).ToArray();
            static readonly Type[] _references = _concretes.Where(type => type.IsClass).ToArray();
            static readonly Type[] _values = _concretes.Where(type => type != typeof(Nullable<>) && type.IsValueType).ToArray();
            internal static readonly Type[] _enumerations = _values.Where(type => type.IsEnum).ToArray();
            internal static readonly Type[] _flags = _enumerations.Where(type => type.IsDefined(typeof(FlagsAttribute))).ToArray();
            static readonly Type[] _primitives = _values.Where(type => type.IsPrimitive).ToArray();
            static readonly Type[] _arguments = _instances.Where(type => !type.IsByRef && !type.IsPointer && !type.IsByRefLike()).ToArray();
            static readonly Type[] _arrays = _arguments.Select(type => type.ArrayType()).Choose().ToArray();
            static readonly Type[] _pointers = _values.Select(type => type.PointerType()).Choose().ToArray();
            static readonly Type[] _defaults = _concretes.Where(type => type.IsConcrete() && type.DefaultConstructor().TryValue(out var constructor) && constructor.IsPublic).ToArray();

            public static readonly Generator<Type> Type = Range(_types).With($"{nameof(Types)}.{nameof(Type)}");
            public static readonly Generator<Type> Abstract = Range(_abstracts).With($"{nameof(Types)}.{nameof(Abstract)}");
            public static readonly Generator<Type> Interface = Range(_interfaces).With($"{nameof(Types)}.{nameof(Interface)}");
            public static readonly Generator<Type> Primitive = Range(_primitives).With($"{nameof(Types)}.{nameof(Primitive)}");
            public static readonly Generator<Type> Enumeration = Range(_enumerations).With($"{nameof(Types)}.{nameof(Enumeration)}");
            public static readonly Generator<Type> Flags = Range(_flags).With($"{nameof(Types)}.{nameof(Flags)}");
            public static readonly Generator<Type> Definition = Range(_definitions).With($"{nameof(Types)}.{nameof(Definition)}");
            public static readonly Generator<Type> Reference = Range(_references, true).With($"{nameof(Types)}.{nameof(Reference)}");
            public static readonly Generator<Type> Value = Range(_values, true).With($"{nameof(Types)}.{nameof(Value)}");
            public static readonly Generator<Type> Concrete = Range(_concretes).With($"{nameof(Types)}.{nameof(Concrete)}");
            public static readonly Generator<Type> Array = Range(_arrays).With($"{nameof(Types)}.{nameof(Array)}");
            public static readonly Generator<Type> Pointer = Range(_pointers).With($"{nameof(Types)}.{nameof(Pointer)}");
            public static readonly Generator<Type> Default = Range(_defaults).Make().With($"{nameof(Types)}.{nameof(Default)}");
            public static readonly Generator<Type> Unmanaged = Any(Primitive, Enumeration, Pointer).With($"{nameof(Types)}.{nameof(Unmanaged)}");

            static readonly Generator<Type> _argument = Range(_arguments, true);
            static readonly ConcurrentDictionary<Type, Option<Generator<Type>>> _typeToArgument = new();
            static readonly ConcurrentDictionary<Type, Option<Generator<Type>>> _typeToMakeType = new();
            static readonly ConcurrentDictionary<MethodInfo, Option<Generator<MethodInfo>>> _typeToMakeMethod = new();

            public static Option<Generator<Type>> Make(Type definition) =>
                _typeToMakeType.GetOrAdd(definition, key => key.GetGenericArguments().Select(Argument).All().Map(arguments => Make(key, arguments)));
            public static Generator<Type> Make(Type definition, Generator<Type> argument) =>
                Make(definition, definition.GetGenericArguments().Select(_ => argument));
            public static Generator<Type> Make(Type definition, params Generator<Type>[] arguments) =>
                Make(definition, arguments.All());
            public static Generator<Type> Make(Type definition, Generator<Type[]> arguments) =>
                definition.IsGenericTypeDefinition ?
                arguments
                    .Choose(arguments => Core.Option.Try(() => definition.MakeGenericType(arguments)))
                    .With(nameof(Make).Format(definition)) :
                Constant(definition).With(nameof(Make).Format(definition));

            public static Option<Generator<MethodInfo>> Make(MethodInfo definition) =>
                _typeToMakeMethod.GetOrAdd(definition, key => key.GetGenericArguments().Select(Argument).All().Map(arguments => Make(key, arguments)));
            public static Generator<MethodInfo> Make(MethodInfo definition, Generator<Type> argument) =>
                Make(definition, definition.GetGenericArguments().Select(_ => argument).All());
            public static Generator<MethodInfo> Make(MethodInfo definition, params Generator<Type>[] arguments) =>
                Make(definition, arguments.All());
            public static Generator<MethodInfo> Make(MethodInfo definition, Generator<Type[]> arguments) =>
                definition.IsGenericMethodDefinition ?
                arguments
                    .Choose(arguments => Core.Option.Try(() => definition.MakeGenericMethod(arguments)))
                    .With(nameof(Make).Format(definition)) :
                Constant(definition).With(nameof(Make).Format(definition));

            public static Generator<Type> Argument() => _argument;
            public static Option<Generator<Type>> Argument(Type argument) => _typeToArgument.GetOrAdd(argument, key =>
            {
                var types = _arguments.ToSet();
                var attributes = key.GenericAttributes();
                var constraints = key.GenericConstraints();

                foreach (var constraint in constraints)
                {
                    // No support for constraints that relate to other parameters.
                    if (constraint.ContainsGenericParameters) return default;
                    types.IntersectWith(_arguments.Where(type => type.Is(constraint)));
                }

                if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                    types.IntersectWith(_references);
                if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                    types.IntersectWith(_values);
                else if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
                    types.IntersectWith(_defaults);

                return Option.Try(() => Range(types, true).With(nameof(Argument).Format(key)));
            });

            public static Generator<Type> Derived<T>(Filter filter = Filter.None) => filter switch
            {
                Filter.Abstract => AbstractDerivedCache<T>.Derived,
                Filter.Concrete => ConcreteDerivedCache<T>.Derived,
                _ => AllDerivedCache<T>.Derived
            };

            public static Generator<Type> Derived(Type type, Filter filter = Filter.None)
            {
                var types = filter switch
                {
                    Filter.Abstract => _abstracts,
                    Filter.Concrete => _concretes,
                    _ => _types
                };
                return Range(types.Where(other => other.Is(type))).With(nameof(Derived).Format(type, filter));
            }

            static Generator<Type> Range(IEnumerable<Type> types, bool make = false)
            {
                var groups = types
                    .GroupBy(type => type.GenericArguments().Length)
                    .OrderBy(group => group.Key)
                    .Select(group => group.ToArray())
                    .Where(group => group.Length > 0)
                    .ToArray();
                var generator = Generator.Range(groups).Any();
                return make ? generator.Make().Attenuate(2) : generator;
            }
        }

        static class EmptyCache<T>
        {
            public static readonly Generator<T[]> Empty = Constant(Array.Empty<T>()).With(Name<T>.Empty);
        }

        static class EnumerationCache<T> where T : struct, Enum, IConvertible
        {
            public static readonly Generator<T> Enumeration = Generator.Enumeration(typeof(T)).Map(value => (T)value).With(Name<T>.Enumeration);
        }

        static class FlagsCache<T> where T : struct, Enum, IConvertible
        {
            public static readonly Generator<T> Flags = Generator.Flags(typeof(T)).Map(value => (T)value).With(Name<T>.Flag);
        }

        static class FactoryCache<T> where T : new()
        {
            public static readonly Generator<T> Factory = Factory(() => new T()).With(Name<T>.Factory);
        }

        static readonly Generator<Enum> _enumeration = Types._enumerations.Select(Enumeration).Any().With(nameof(_enumeration));
        static readonly Generator<Enum> _flags = Types._flags.Select(Flags).Any().With(nameof(_flags));
        static readonly Generator<decimal> _number = Number(int.MinValue, int.MaxValue, 0).Size(size => Math.Pow(size, 15) + (1.0 - size) * 9e-9);

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

        public static Generator<T> From<T>(string name, Generate<T> generate) => new(name, generate);
        public static Generator<T> From<T>(Generate<T> generate) => From(Format(generate.Method), generate);

        public static Generator<T> Constant<T>(T value) => From(nameof(Constant).Format(value), _ => (value, Shrinker.Empty<T>()));

        public static Generator<T[]> Empty<T>() => EmptyCache<T>.Empty;
        public static Option<Generator<Array>> Empty(Type type) => Option.Try(() => Array.CreateInstance(type, 0)).Map(value => Constant(value).With(nameof(Empty).Format(type)));

        public static Generator<T> Factory<T>(Func<T> create) => From(Name<T>.Factory, _ => (create(), Shrinker.Empty<T>()));
        public static Generator<T> Factory<T>() where T : new() => FactoryCache<T>.Factory;
        public static Generator<object> Factory(this Generator<Type> type) => type.Choose(Factory).Flatten();
        public static Option<Generator<object>> Factory(Type type)
        {
            if (type.IsConcrete() &&
                type.DefaultConstructors()
                    .Where(pair => pair.constructor.IsPublic)
                    .OrderBy(pair => pair.parameters.Length)
                    .TryFirst(out var pair) &&
                // Try to invoke the constructor once to check for exceptions.
                Option.Try(() => pair.constructor.Invoke(pair.parameters)).IsSome())
                return Factory<object>(() => pair.constructor.Invoke(pair.parameters)).With(nameof(Factory).Format(type));
            return default;
        }

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
        public static Generator<T> Size<T>(this Generator<T> generator, double size) =>
            generator.Size(_ => size).With(Name<T>.Size.Format(size));
        public static Generator<T> Size<T>(this Generator<T> generator, Generator<double> size) =>
            size.Bind(size => generator.Size(size)).With(Name<T>.Size.Format(size));

        public static Generator<T> Depth<T>(this Generator<T> generator, uint depth = 1) =>
            generator.Adapt(state => state.With(depth: state.Depth + depth))
                .With(Name<T>.Depth.Format(generator, depth));

        public static Generator<T> Attenuate<T>(this Generator<T> generator, uint depth) =>
            generator.Adapt(state => state.With(state.Size * Math.Max(1.0 - (double)state.Depth / depth, 0.0), state.Depth + 1))
                .With(Name<T>.Attenuate.Format(generator, depth));
        public static Generator<T> Attenuate<T>(this Generator<T> generator, Generator<uint> depth) =>
            depth.Bind(depth => generator.Attenuate(depth))
                .With(Name<T>.Attenuate.Format(generator, depth));

        public static Generator<T> Shrink<T>(this Generator<T> generator, Shrink<T> shrink) =>
            generator.Shrink(Shrinker.From(Name<T>.Shrink, shrink));
        public static Generator<T> Shrink<T>(this Generator<T> generator, Shrinker<T> shrinker) =>
            From(Name<T>.Shrink.Format(generator), state =>
            {
                var pair = generator.Generate(state);
                return (pair.value, shrinker);
            });

        public static Generator<float> Inverse(this Generator<float> generator) => generator.Map(value => 1 / value).With(nameof(Inverse).Format(generator));
        public static Generator<double> Inverse(this Generator<double> generator) => generator.Map(value => 1 / value).With(nameof(Inverse).Format(generator));
        public static Generator<decimal> Inverse(this Generator<decimal> generator) => generator.Map(value => value == 0 ? 0 : 1 / value).With(nameof(Inverse).Format(generator));

        public static Generator<sbyte> Signed(this Generator<byte> generator) => generator.Map(value => (sbyte)value).With(nameof(Signed).Format(generator));
        public static Generator<short> Signed(this Generator<ushort> generator) => generator.Map(value => (short)value).With(nameof(Signed).Format(generator));
        public static Generator<int> Signed(this Generator<uint> generator) => generator.Map(value => (int)value).With(nameof(Signed).Format(generator));
        public static Generator<long> Signed(this Generator<ulong> generator) => generator.Map(value => (long)value).With(nameof(Signed).Format(generator));

        public static Generator<byte> Unsigned(this Generator<sbyte> generator) => generator.Map(value => (byte)value).With(nameof(Unsigned).Format(generator));
        public static Generator<ushort> Unsigned(this Generator<short> generator) => generator.Map(value => (ushort)value).With(nameof(Unsigned).Format(generator));
        public static Generator<uint> Unsigned(this Generator<int> generator) => generator.Map(value => (uint)value).With(nameof(Unsigned).Format(generator));
        public static Generator<ulong> Unsigned(this Generator<long> generator) => generator.Map(value => (ulong)value).With(nameof(Unsigned).Format(generator));

        public static Generator<Enum> Enumeration() => _enumeration;
        public static Generator<T> Enumeration<T>() where T : struct, Enum => EnumerationCache<T>.Enumeration;
        public static Generator<Enum> Enumeration(Type type) => Any(Enum.GetValues(type).OfType<Enum>().ToArray()).With(nameof(Enumeration).Format(type));

        public static Generator<Enum> Flags() => _flags;
        public static Generator<T> Flags<T>() where T : struct, Enum => FlagsCache<T>.Flags;
        public static Generator<Enum> Flags(Type type)
        {
            var values = Enum.GetValues(type).OfType<Enum>().ToArray();
            return Any(values)
                .Repeat(Range(values.Length))
                .Map(values => (Enum)Enum.ToObject(type, values.Aggregate(0L, (flags, value) => flags | Convert.ToInt64(value))))
                .With(nameof(Flags).Format(type));
        }

        public static Generator<string> String(Generator<int> count) => Character.String(count);
        public static Generator<string> String(this Generator<char> generator, Generator<int> count) =>
            generator.Repeat(count).Map(characters => new string(characters)).With(nameof(String).Format(generator, count));

        public static Generator<char> Range(char maximum) => Range('\0', maximum);
        public static Generator<char> Range(char minimum, char maximum) =>
            Number(minimum, maximum, minimum).Map(value => (char)Math.Round(value)).With(Name<char>.Range.Format(minimum, maximum));
        public static Generator<float> Range(float maximum) => Range(0, maximum);
        public static Generator<float> Range(float minimum, float maximum) =>
            Number((decimal)minimum, (decimal)maximum, (decimal)minimum).Map(value => (float)value).With(Name<float>.Range.Format(minimum, maximum));
        public static Generator<double> Range(double maximum) => Range(0, maximum);
        public static Generator<double> Range(double minimum, double maximum) =>
            Number((decimal)minimum, (decimal)maximum, (decimal)minimum).Map(value => (double)value).With(Name<double>.Range.Format(minimum, maximum));
        public static Generator<decimal> Range(decimal maximum) => Range(0, maximum);
        public static Generator<decimal> Range(decimal minimum, decimal maximum) =>
            Number(minimum, maximum, minimum).With(Name<decimal>.Range.Format(minimum, maximum));
        public static Generator<int> Range(int maximum) => Range(0, maximum);
        public static Generator<int> Range(int minimum, int maximum) =>
            Number(minimum, maximum, minimum).Map(value => (int)Math.Round(value)).With(Name<int>.Range.Format(minimum, maximum));
        public static Generator<long> Range(long maximum) => Range(0, maximum);
        public static Generator<long> Range(long minimum, long maximum) =>
            Number(minimum, maximum, minimum).Map(value => (long)Math.Round(value)).With(Name<long>.Range.Format(minimum, maximum));
        public static Generator<T> Range<T>(params T[] values) =>
            values.Length == 0 ? throw new ArgumentException(nameof(values)) :
            values.Length == 1 ? values[0] :
            Range(values.Length - 1).Map(index => values[index]).With(Name<T>.Range.Format(values));

        public static Generator<TTarget> Map<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, State, TTarget> map) =>
            From(Name<TSource, TTarget>.Map.Format(generator), state =>
            {
                var (value, shrinker) = generator.Generate(state);
                return (map(value, state), shrinker.Map(shrink => shrink.Map(map)));
            });
        public static Generator<TTarget> Map<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, TTarget> map) =>
            From(Name<TSource, TTarget>.Map.Format(generator), state =>
            {
                var (value, shrinker) = generator.Generate(state);
                return (map(value), shrinker.Map(shrink => shrink.Map(map)));
            });

        public static Generator<TTarget> Bind<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, State, Generator<TTarget>> bind) =>
            From(Name<TSource, TTarget>.Bind.Format(generator), state =>
            {
                var pair1 = generator.Generate(state);
                var pair2 = bind(pair1.value, state).Generate(state);
                return (pair2.value, pair1.shrinker.Map(pair2.shrinker, shrink => shrink.Bind(bind)));
            });
        public static Generator<TTarget> Bind<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, Generator<TTarget>> bind) =>
            From(Name<TSource, TTarget>.Bind.Format(generator), state =>
            {
                var pair1 = generator.Generate(state);
                var pair2 = bind(pair1.value).Generate(state);
                return (pair2.value, pair1.shrinker.Map(pair2.shrinker, shrink => shrink.Bind(bind)));
            });

        public static Generator<T> Choose<T>(this Generator<Option<T>> generator) =>
            From(Name<T>.Choose.Format(generator), state =>
            {
                while (true)
                {
                    var pair = generator.Generate(state);
                    if (pair.value.TryValue(out var value))
                        return (value, pair.shrinker.Map(shrink => shrink.Choose()));
                }
            });
        public static Generator<TTarget> Choose<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, State, Option<TTarget>> choose) =>
            From(Name<TSource, TTarget>.Choose.Format(generator), state =>
            {
                while (true)
                {
                    var pair = generator.Generate(state);
                    if (choose(pair.value, state).TryValue(out var value))
                        return (value, pair.shrinker.Map(shrink => shrink.Choose(choose)));
                }
            });
        public static Generator<TTarget> Choose<TSource, TTarget>(this Generator<TSource> generator, Func<TSource, Option<TTarget>> choose) =>
            From(Name<TSource, TTarget>.Choose.Format(generator), state =>
            {
                while (true)
                {
                    var pair = generator.Generate(state);
                    if (choose(pair.value).TryValue(out var value))
                        return (value, pair.shrinker.Map(shrink => shrink.Choose(choose)));
                }
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
        public static Generator<T> Filter<T>(this Generator<T> generator, Func<T, State, bool> filter) =>
            From(Name<T>.Filter.Format(generator), state =>
            {
                while (true)
                {
                    var (value, shrinker) = generator.Generate(state);
                    if (filter(value, state)) return (value, shrinker);
                }
            });

        public static Generator<T> Flatten<T>(this Generator<Generator<T>> generator) =>
            From(Name<T>.Flatten.Format(generator), state =>
            {
                var pair1 = generator.Generate(state);
                var pair2 = pair1.value.Generate(state);
                return (pair2.value, pair1.shrinker.Map(pair2.shrinker, shrink => shrink.Flatten()));
            });

        public static Generator<T> Any<T>(this Generator<IEnumerable<T>> generator) => generator.Bind(Any);
        public static Generator<T> Any<T>(this Generator<T[]> generator) => generator.Bind(Any);
        public static Generator<T> Any<T>(this Generator<IEnumerable<(double weight, T value)>> generator) => generator.Bind(Any);
        public static Generator<T> Any<T>(this Generator<(double weight, T value)[]> generator) => generator.Bind(Any);

        public static Generator<T> Any<T>(IEnumerable<T> values) => Any(values.ToArray());
        public static Generator<T> Any<T>(params T[] values) =>
            values.Length == 0 ? throw new ArgumentException(nameof(values)) :
            values.Length == 1 ? values[0] :
            From(Name<T>.Any.Format(values), state =>
            {
                var index = state.Random.Next(values.Length);
                return (values[index], Shrinker.Empty<T>());
            });

        public static Generator<T> Any<T>(this IEnumerable<Generator<T>> generators) => Any(generators.ToArray());
        public static Generator<T> Any<T>(params Generator<T>[] generators) =>
            generators.Length == 0 ? throw new ArgumentException(nameof(generators)) :
            generators.Length == 1 ? generators[0] :
            From(Name<T>.Any.Format(generators), state =>
            {
                var index = state.Random.Next(generators.Length);
                return generators[index].Generate(state);
            });

        public static Generator<T> Any<T>(IEnumerable<(double weight, T value)> values) => Any(values.ToArray());
        public static Generator<T> Any<T>(params (double weight, T value)[] values)
        {
            if (values.Length == 0) throw new ArgumentException(nameof(values));
            if (values.Length == 1) return values[0].value;

            var sum = values.Sum(pair => pair.weight);
            return From(Name<T>.Any.Format(values), state =>
            {
                var random = state.Random.NextDouble() * sum;
                var current = 0d;
                return (values.First(pair => random < (current += pair.weight)).value, Shrinker.Empty<T>());
            });
        }

        public static Generator<T> Any<T>(IEnumerable<(double weight, Generator<T> generator)> generators) => Any(generators.ToArray());
        public static Generator<T> Any<T>(params (double weight, Generator<T> generator)[] generators)
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

        public static Generator<T> Any<T>(params (Generator<double> weight, Generator<T> generator)[] generators)
        {
            if (generators.Length == 0) throw new ArgumentException(nameof(generators));
            if (generators.Length == 1) return generators[0].generator;

            return From(Name<T>.Any.Format(generators), state =>
            {
                var sum = 0.0;
                var weights = new double[generators.Length];
                for (int i = 0; i < weights.Length; i++)
                    sum += weights[i] = Math.Max(generators[i].weight.Generate(state).value, 0.0);
                var random = state.Random.NextDouble() * sum;
                var current = 0.0;
                for (int i = 0; i < weights.Length; i++)
                {
                    current += weights[i];
                    if (random < current) return generators[i].generator.Generate(state);
                }
                return generators[0].generator.Generate(state);
            });
        }

        public static Generator<T[]> All<T>(this IEnumerable<Generator<T>> generators) => All(generators.ToArray());
        public static Generator<T[]> All<T>(params Generator<T>[] generators) =>
            generators.Length == 0 ? Empty<T>() :
            From(Name<T>.All.Format(generators), state =>
            {
                var values = new T[generators.Length];
                var shrinkers = new Shrinker<T>[generators.Length];
                for (int i = 0; i < generators.Length; i++) (values[i], shrinkers[i]) = generators[i].Generate(state);
                return (values, Shrinker.All(values, shrinkers));
            });

        public static Generator<T[]> Repeat<T>(this Generator<T> generator, int count) =>
            count <= 0 ? Empty<T>() :
            From(Name<T>.Repeat.Format(generator, count), state =>
            {
                var values = new T[count];
                var shrinkers = new Shrinker<T>[count];
                for (int i = 0; i < count; i++) (values[i], shrinkers[i]) = generator.Generate(state);
                return (values, Shrinker.Repeat(values, shrinkers));
            });
        public static Generator<T[]> Repeat<T>(this Generator<T> generator, Generator<int> count) =>
            From(Name<T>.Repeat.Format(generator, count), state =>
            {
                var pair = count.Generate(state);
                if (pair.value <= 0) return Empty<T>().Generate(state);

                var values = new T[pair.value];
                var shrinkers = new Shrinker<T>[pair.value];
                for (int i = 0; i < pair.value; i++) (values[i], shrinkers[i]) = generator.Generate(state);
                return (values, Shrinker.Repeat(values, shrinkers));
            });

        public static Generator<object?> Box<T>(this Generator<T> generator) =>
            generator.Map(value => (object?)value).With(Name<T>.Box.Format(generator));

        public static Generator<(T1, T2)> And<T1, T2>(this Generator<T1> generator1, Generator<T2> generator2) =>
            All(generator1.Box(), generator2.Box()).Map(values => ((T1)values[0]!, (T2)values[1]!))
                .With(Name<T1, T2>.And.Format(generator1, generator2));
        public static Generator<(T1, T2, T3)> And<T1, T2, T3>(this Generator<T1> generator1, Generator<T2> generator2, Generator<T3> generator3) =>
            All(generator1.Box(), generator2.Box(), generator3.Box()).Map(values => ((T1)values[0]!, (T2)values[1]!, (T3)values[2]!))
                .With(Name<T1, T2, T3>.And.Format(generator1, generator2, generator3));
        public static Generator<(T1, T2, T3, T4)> And<T1, T2, T3, T4>(this Generator<T1> generator1, Generator<T2> generator2, Generator<T3> generator3, Generator<T4> generator4) =>
            All(generator1.Box(), generator2.Box(), generator3.Box(), generator4.Box()).Map(values => ((T1)values[0]!, (T2)values[1]!, (T3)values[2]!, (T4)values[3]!))
                .With(Name<T1, T2, T3, T4>.And.Format(generator1, generator2, generator3, generator4));
        public static Generator<(T1, T2, T3, T4, T5)> And<T1, T2, T3, T4, T5>(this Generator<T1> generator1, Generator<T2> generator2, Generator<T3> generator3, Generator<T4> generator4, Generator<T5> generator5) =>
            All(generator1.Box(), generator2.Box(), generator3.Box(), generator4.Box(), generator5.Box()).Map(values => ((T1)values[0]!, (T2)values[1]!, (T3)values[2]!, (T4)values[3]!, (T5)values[4]!))
                .With(Name<T1, T2, T3, T4, T5>.And.Format(generator1, generator2, generator3, generator4, generator5));

        public static Generator<T> Cache<T>(this Generator<T> generator, double ratio = 0.5, uint size = 64)
        {
            if (size == 0) return generator;

            var cache = new Tuple<T, Shrinker<T>>[size];
            var count = 0;
            return From(Name<T>.Cache, state =>
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

        public static Generator<Type> Make(this Generator<Type> definition, Generator<Type> argument) =>
            definition.Bind(definition => Types.Make(definition, argument)).With(nameof(Make).Format(definition, argument));
        public static Generator<Type> Make(this Generator<Type> definition) =>
            From(nameof(Make).Format(definition), state =>
            {
                while (true)
                {
                    var pair = definition.Generate(state);
                    if (Types.Make(pair.value).TryValue(out var generator))
                        return generator.Generate(state);
                }
            });

        public static Generator<MethodInfo> Make(this Generator<MethodInfo> definition, Generator<Type> argument) =>
            definition.Bind(definition => Types.Make(definition, argument)).With(nameof(Make).Format(definition, argument));
        public static Generator<MethodInfo> Make(this Generator<MethodInfo> definition) =>
            From(nameof(Make).Format(definition), state =>
            {
                while (true)
                {
                    var pair = definition.Generate(state);
                    if (Types.Make(pair.value).TryValue(out var generator))
                        return generator.Generate(state);
                }
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

        public static Generator<Outcome<T>> Mutate<T>(this Generator<T> generator, Generator<Mutation<T>[]> mutations) =>
            From(Name<T>.Mutate.Format(generator, mutations), state =>
            {
                var pair = (value: generator.Generate(state), mutations: mutations.Generate(state));
                var properties = pair.mutations.value
                    .Select(pair.value.value, (mutation, value) => mutation.Mutate(value))
                    .Flatten();
                var outcome = new Outcome<T>(pair.value.value, pair.mutations.value, properties);
                return (outcome, Shrinker.Map(
                    pair.value.shrinker,
                    pair.mutations.shrinker,
                    shrink => shrink.Mutate(mutations),
                    shrink => generator.Mutate(shrink)));
            });

        static Generator<decimal> Number(decimal minimum, decimal maximum, decimal target)
        {
            if (minimum == maximum) return minimum;
            return From(nameof(Number).Format(minimum, maximum, target), state =>
            {
                var random = Interpolate(minimum, maximum, (decimal)state.Random.NextDouble());
                var value = Interpolate(target, random, (decimal)state.Size);
                return (value, Shrinker.Number(value, target));
            });

            static decimal Interpolate(decimal source, decimal target, decimal ratio) => (target - source) * ratio + source;
        }
    }
}