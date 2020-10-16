using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Entia.Core;

namespace Entia.Json.Converters
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class ConverterAttribute : PreserveAttribute { }

    public interface IConverter
    {
        Type Type { get; }
        Node Convert(in ToContext context);
        object Instantiate(in FromContext context);
        void Initialize(ref object instance, in FromContext context);
    }

    public abstract class Converter<T> : IConverter
    {
        public virtual Type Type => typeof(T);
        public abstract Node Convert(in T instance, in ToContext context);
        public abstract T Instantiate(in FromContext context);
        public virtual void Initialize(ref T instance, in FromContext context) { }

        Node IConverter.Convert(in ToContext context) =>
            context.Instance is T casted ? Convert(casted, context) : Node.Null;
        object IConverter.Instantiate(in FromContext context) => Instantiate(context);
        void IConverter.Initialize(ref object instance, in FromContext context)
        {
            if (instance is T casted)
            {
                Initialize(ref casted, context);
                instance = casted;
            }
        }
    }

    public static class Converter
    {
        public delegate Option<(int version, Node node)> Upgrade(Node node);
        public delegate bool Validate(Type type);
        public delegate Node Convert<T>(in T instance, in ToContext context);
        public delegate T Instantiate<T>(in FromContext context);
        public delegate void Initialize<T>(ref T instance, in FromContext context);

        sealed class Function<T> : Converter<T>
        {
            public readonly Convert<T> _convert;
            public readonly Instantiate<T> _instantiate;
            public readonly Initialize<T> _initialize;

            public Function(Convert<T> convert, Instantiate<T> instantiate, Initialize<T> initialize)
            {
                _convert = convert;
                _instantiate = instantiate;
                _initialize = initialize;
            }

            public override Node Convert(in T instance, in ToContext context) => _convert(instance, context);
            public override T Instantiate(in FromContext context) => _instantiate(context);
            public override void Initialize(ref T instance, in FromContext context) => _initialize(ref instance, context);
        }

        static class Cache<T>
        {
            public static readonly IConverter Default = Converter.Default(typeof(T));
            public static readonly Convert<T> Convert = (in T instance, in ToContext context) => Default.Convert(context);
            public static readonly Instantiate<T> Instantiate = (in FromContext context) => (T)Default.Instantiate(context);
            public static readonly Initialize<T> Initialize = (ref T instance, in FromContext context) =>
            {
                var box = (object)instance;
                Default.Initialize(ref box, context);
                instance = (T)box;
            };
        }

        static readonly ConcurrentDictionary<Type, IConverter> _converters = new ConcurrentDictionary<Type, IConverter>();

        public static IConverter Default(Type type) => _converters.GetOrAdd(type, key => CreateConverter(key));
        public static IConverter Default<T>() => Cache<T>.Default;

        public static Converter<TSource> Create<TSource, TTarget>(InFunc<TSource, TTarget> to, InFunc<TTarget, TSource> from, Converter<TTarget> converter = null) =>
            Create(
                (in TSource instance, in ToContext context) => context.Convert(to(instance), converter, converter),
                (in FromContext context) => from(context.Convert<TTarget>(context.Node, converter, converter)));

        public static Converter<T> Create<T>(InFunc<T, Node> to, Func<Node, T> from) => Create(
            (in T instance, in ToContext context) => to(instance),
            (in FromContext context) => from(context.Node));

        public static Converter<T> Create<T>(Convert<T> convert = null, Instantiate<T> instantiate = null, Initialize<T> initialize = null) =>
            new Function<T>(convert ?? Cache<T>.Convert, instantiate ?? Cache<T>.Instantiate, initialize ?? Cache<T>.Initialize);

        public static Converter<T> Version<T>(params (int version, Converter<T> converter)[] converters) =>
            Version(converters.Min(pair => pair.version), converters.Max(pair => pair.version), converters);
        public static Converter<T> Version<T>(int @default, int latest, params (int version, Converter<T> converter)[] converters)
        {
            var versionToConverter = converters.ToDictionary(pair => pair.version, pair => pair.converter);
            var defaultConverter = versionToConverter[@default];
            var latestConverter = versionToConverter[latest];

            Converter<T> Converter(Node node, out Node value)
            {
                var pair =
                    node.IsObject() && node.Children.Length == 4 &&
                    node.Children[0] == Node.DollarKString && node.Children[2] == Node.DollarVString ?
                    (version: node.Children[1].AsInt(), value: node.Children[3]) : (version: @default, value: node);
                value = pair.value;
                return versionToConverter.TryGetValue(pair.version, out var converter) ? converter : defaultConverter;
            }

            return Create(
                (in T instance, in ToContext context) =>
                    Node.Object(Node.DollarKString, latest, Node.DollarVString, latestConverter.Convert(instance, context)),
                (in FromContext context) =>
                    Converter(context.Node, out var value).Instantiate(context.With(value)),
                (ref T instance, in FromContext context) =>
                    Converter(context.Node, out var value).Initialize(ref instance, context.With(value)));
        }

        public static Converter<T> If<T>((InFunc<T, bool> to, Func<Node, bool> from) condition, Converter<T> @true, Converter<T> @false)
        {
            condition.to ??= (in T _) => true;
            condition.from ??= _ => true;
            return Create(
                (in T instance, in ToContext context) =>
                    condition.to(instance) ? @true.Convert(instance, context) : @false.Convert(instance, context),
                (in FromContext context) =>
                    condition.from(context.Node) ? @true.Instantiate(context) : @false.Instantiate(context),
                (ref T instance, in FromContext context) =>
                {
                    if (condition.from(context.Node))
                        @true.Initialize(ref instance, context);
                    else
                        @false.Initialize(ref instance, context);
                }
            );
        }

        public static Converter<T> Object<T>(Instantiate<T> instantiate = null, params Member<T>[] members)
        {
            var map = members
                .SelectMany(member => member.Aliases.Prepend(member.Name).Select(name => (member, name)))
                .ToDictionary(pair => pair.name, pair => pair.member);
            return Create(
                (in T instance, in ToContext context) =>
                {
                    var children = new List<Node>(members.Length * 2);
                    for (int i = 0; i < members.Length; i++)
                    {
                        var member = members[i];
                        if (member.Convert(instance, context) is Node node)
                        {
                            children.Add(member.Key);
                            children.Add(node);
                        }
                    }
                    return Node.Object(children.ToArray());
                },
                instantiate,
                (ref T instance, in FromContext context) =>
                {
                    foreach (var (key, value) in context.Node.Members())
                    {
                        if (map.TryGetValue(key, out var member))
                            member.Initialize(ref instance, context.With(value));
                    }
                }
            );
        }

        public static Converter<T> Array<T>(Instantiate<T> instantiate = null, params Item<T>[] items)
        {
            var map = new Item<T>[items.Length == 0 ? 0 : items.Max(item => item.Index + 1)];
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                map[item.Index] = item;
            }
            return Create(
                (in T instance, in ToContext context) =>
                {
                    var children = new Node[map.Length];
                    for (int i = 0; i < map.Length; i++)
                        children[i] = map[i]?.Convert(instance, context) ?? Node.Null;
                    return Node.Array(children.ToArray());
                },
                instantiate,
                (ref T instance, in FromContext context) =>
                {
                    var children = context.Node.Children;
                    for (int i = 0; i < children.Length && i < map.Length; i++)
                        map[i]?.Initialize(ref instance, context.With(children[i]));
                }
            );
        }

        static IConverter CreateConverter(Type type)
        {
            if (type.IsArray) return CreateArray(type);
            if (type == typeof(DateTime)) return new ConcreteDateTime();
            if (type == typeof(TimeSpan)) return new ConcreteTimeSpan();
            if (type == typeof(Guid)) return new ConcreteGuid();
            if (type == typeof(Node)) return new ConcreteNode();
            if (type.Is<Type>()) return new ConcreteType();
            if (type.GenericDefinition().TryValue(out var definition))
            {
                var arguments = type.GetGenericArguments();
                if (definition == typeof(Nullable<>)) return CreateNullable(type, arguments[0]);
                if (definition == typeof(Option<>)) return CreateOption(type, arguments[0]);
                if (definition == typeof(List<>)) return CreateList(type, arguments[0]);
                if (definition == typeof(Dictionary<,>)) return CreateDictionary(type, arguments[0], arguments[1]);
            }

            if (type.Hierarchy()
                .SelectMany(@base => Enumerable.Concat(
                    @base.GetFields(ReflectionUtility.Static)
                        .Where(field => field.IsDefined(typeof(ConverterAttribute), true))
                        .Select(field => field.GetValue(null)),
                    @base.GetProperties(ReflectionUtility.Static)
                        .Where(property => property.IsDefined(typeof(ConverterAttribute), true) && property.CanRead)
                        .Select(property => property.GetValue(null))))
                .OfType<IConverter>()
                .TryFirst(converter => type.Is(converter.Type, true, true), out var converter))
                return converter;

            if (type.Is<IList>()) return CreateIList(type);
            if (type.Is<IDictionary>()) return CreateIDictionary(type);
            if (type.Is<IEnumerable>()) return CreateIEnumerable(type);
            if (type.Is<ISerializable>()) return CreateISerializable(type);
            return CreateDefault(type);
        }

        static IConverter CreateDefault(Type type) => new DefaultObject(type);

        static IConverter CreateOption(Type type, Type argument)
        {
            if (Option.Try(argument, state => Activator.CreateInstance(typeof(ConcreteOption<>).MakeGenericType(state)))
                .Cast<IConverter>()
                .TryValue(out var converter))
                return converter;
            if (type.Constructors(true, false).TryFirst(current =>
                current.GetParameters().Length == 2, out var constructor))
                return new AbstractOption(constructor, argument);

            return CreateDefault(type);
        }

        static IConverter CreateNullable(Type type, Type argument) =>
            Option.Try(argument, state => Activator.CreateInstance(typeof(ConcreteNullable<>).MakeGenericType(state)))
                .Cast<IConverter>()
                .Or(() => new AbstractNullable(type, argument));

        static IConverter CreateArray(Type type)
        {
            var element = type.GetElementType();
            switch (Type.GetTypeCode(element))
            {
                case TypeCode.Char: return new PrimitiveArray<char>(_ => _, node => node.AsChar());
                case TypeCode.Byte: return new PrimitiveArray<byte>(_ => _, node => node.AsByte());
                case TypeCode.SByte: return new PrimitiveArray<sbyte>(_ => _, node => node.AsSByte());
                case TypeCode.Int16: return new PrimitiveArray<short>(_ => _, node => node.AsShort());
                case TypeCode.Int32: return new PrimitiveArray<int>(_ => _, node => node.AsInt());
                case TypeCode.Int64: return new PrimitiveArray<long>(_ => _, node => node.AsLong());
                case TypeCode.UInt16: return new PrimitiveArray<ushort>(_ => _, node => node.AsUShort());
                case TypeCode.UInt32: return new PrimitiveArray<uint>(_ => _, node => node.AsUInt());
                case TypeCode.UInt64: return new PrimitiveArray<ulong>(_ => _, node => node.AsULong());
                case TypeCode.Single: return new PrimitiveArray<float>(_ => _, node => node.AsFloat());
                case TypeCode.Double: return new PrimitiveArray<double>(_ => _, node => node.AsDouble());
                case TypeCode.Decimal: return new PrimitiveArray<decimal>(_ => _, node => node.AsDecimal());
                case TypeCode.Boolean: return new PrimitiveArray<bool>(_ => _, node => node.AsBool());
                case TypeCode.String: return new PrimitiveArray<string>(_ => _, node => node.AsString());
                default:
                    return Option.Try(() => Activator.CreateInstance(typeof(ConcreteArray<>).MakeGenericType(element)))
                        .Cast<IConverter>()
                        .Or(() => new AbstractArray(element));
            }
        }

        static IConverter CreateList(Type type, Type argument)
        {
            switch (Type.GetTypeCode(argument))
            {
                case TypeCode.Char: return new PrimitiveList<char>(_ => _, node => node.AsChar());
                case TypeCode.Byte: return new PrimitiveList<byte>(_ => _, node => node.AsByte());
                case TypeCode.SByte: return new PrimitiveList<sbyte>(_ => _, node => node.AsSByte());
                case TypeCode.Int16: return new PrimitiveList<short>(_ => _, node => node.AsShort());
                case TypeCode.Int32: return new PrimitiveList<int>(_ => _, node => node.AsInt());
                case TypeCode.Int64: return new PrimitiveList<long>(_ => _, node => node.AsLong());
                case TypeCode.UInt16: return new PrimitiveList<ushort>(_ => _, node => node.AsUShort());
                case TypeCode.UInt32: return new PrimitiveList<uint>(_ => _, node => node.AsUInt());
                case TypeCode.UInt64: return new PrimitiveList<ulong>(_ => _, node => node.AsULong());
                case TypeCode.Single: return new PrimitiveList<float>(_ => _, node => node.AsFloat());
                case TypeCode.Double: return new PrimitiveList<double>(_ => _, node => node.AsDouble());
                case TypeCode.Decimal: return new PrimitiveList<decimal>(_ => _, node => node.AsDecimal());
                case TypeCode.Boolean: return new PrimitiveList<bool>(_ => _, node => node.AsBool());
                case TypeCode.String: return new PrimitiveList<string>(_ => _, node => node.AsString());
                default:
                    return Option.Try(() => Activator.CreateInstance(typeof(ConcreteList<>).MakeGenericType(argument)))
                        .Cast<IConverter>()
                        .Or(() => CreateIList(type));
            }
        }

        static IConverter CreateIList(Type type) => CreateIEnumerable(type);

        static IConverter CreateIEnumerable(Type type)
        {
            if (type.EnumerableArgument(true).TryValue(out var argument) &&
                type.EnumerableConstructor(true).TryValue(out var constructor))
            {
                switch (Type.GetTypeCode(argument))
                {
                    case TypeCode.Char: return new PrimitiveEnumerable<char>(_ => _, node => node.AsChar(), constructor);
                    case TypeCode.Byte: return new PrimitiveEnumerable<byte>(_ => _, node => node.AsByte(), constructor);
                    case TypeCode.SByte: return new PrimitiveEnumerable<sbyte>(_ => _, node => node.AsSByte(), constructor);
                    case TypeCode.Int16: return new PrimitiveEnumerable<short>(_ => _, node => node.AsShort(), constructor);
                    case TypeCode.Int32: return new PrimitiveEnumerable<int>(_ => _, node => node.AsInt(), constructor);
                    case TypeCode.Int64: return new PrimitiveEnumerable<long>(_ => _, node => node.AsLong(), constructor);
                    case TypeCode.UInt16: return new PrimitiveEnumerable<ushort>(_ => _, node => node.AsUShort(), constructor);
                    case TypeCode.UInt32: return new PrimitiveEnumerable<uint>(_ => _, node => node.AsUInt(), constructor);
                    case TypeCode.UInt64: return new PrimitiveEnumerable<ulong>(_ => _, node => node.AsULong(), constructor);
                    case TypeCode.Single: return new PrimitiveEnumerable<float>(_ => _, node => node.AsFloat(), constructor);
                    case TypeCode.Double: return new PrimitiveEnumerable<double>(_ => _, node => node.AsDouble(), constructor);
                    case TypeCode.Decimal: return new PrimitiveEnumerable<decimal>(_ => _, node => node.AsDecimal(), constructor);
                    case TypeCode.Boolean: return new PrimitiveEnumerable<bool>(_ => _, node => node.AsBool(), constructor);
                    case TypeCode.String: return new PrimitiveEnumerable<string>(_ => _, node => node.AsString(), constructor);
                    default:
                        return Option.Try(() => Activator.CreateInstance(typeof(AbstractEnumerable<>).MakeGenericType(argument), constructor))
                            .Cast<IConverter>()
                            .Or(() => new AbstractEnumerable(argument, constructor));
                }
            }

            return Option.And(type.EnumerableArgument(false), type.EnumerableConstructor(false))
                .Map(pair => new AbstractEnumerable(pair.Item1, pair.Item2))
                .Cast<IConverter>()
                .Or(() => CreateDefault(type));
        }

        static IConverter CreateDictionary(Type type, Type key, Type value) =>
            Option.Try(() => Activator.CreateInstance(typeof(ConcreteDictionary<,>).MakeGenericType(key, value)))
                .Cast<IConverter>()
                .Or(() => CreateIDictionary(type));

        static IConverter CreateIDictionary(Type type)
        {
            if (type.DefaultConstructor().TryValue(out var constructor))
            {
                if (type.DictionaryArguments(true).TryValue(out var types))
                {
                    return Option.Try(() => Activator.CreateInstance(typeof(AbstractDictionary<,>).MakeGenericType(types.key, types.value)))
                        .Cast<IConverter>()
                        .Or(() => new AbstractDictionary(types.key, types.value, constructor));
                }

                if (type.DictionaryArguments(false).TryValue(out types))
                    return new AbstractDictionary(types.key, types.value, constructor);
            }

            return CreateIEnumerable(type);
        }

        static IConverter CreateISerializable(Type type) => type.SerializableConstructor()
            .Map(constructor => new AbstractSerializable(constructor))
            .Cast<IConverter>()
            .Or(() => CreateDefault(type));
    }
}