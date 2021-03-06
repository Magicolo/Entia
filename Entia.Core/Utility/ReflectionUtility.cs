﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Entia.Core
{
    public static class ReflectionUtility
    {
        static class Cache<T>
        {
            public static readonly TypeData Data = GetData(typeof(T));
        }

        public const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
        public const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public const BindingFlags PublicStatic = BindingFlags.Static | BindingFlags.Public;
        public const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        public const BindingFlags All = Instance | Static;

        public static ICollection<Assembly> AllAssemblies => _assemblies.Values;
        public static ICollection<Type> AllTypes => _types.Values;

        static readonly ConcurrentDictionary<string, Assembly> _assemblies = new();
        static readonly ConcurrentDictionary<(Assembly, string), Type> _types = new();
        static readonly ConcurrentDictionary<MemberInfo, IMemberData> _memberToData = new();
        static readonly ConcurrentDictionary<Guid, Type> _guidToType = new();
        static readonly Func<Type, bool> _isByRefLike = Option.Try(() => typeof(Type).GetProperty("IsByRefLike").GetMethod.CreateDelegate<Func<Type, bool>>()).Or(new Func<Type, bool>(_ => false));

        static ReflectionUtility()
        {
            static void Register(Assembly assembly)
            {
                try
                {
                    var name = assembly.GetName();
                    _assemblies.TryAdd(name.Name, assembly);
                    _assemblies.TryAdd(name.FullName, assembly);

                    foreach (var type in assembly.GetTypes())
                    {
                        _types.TryAdd((null, type.Name), type);
                        _types.TryAdd((null, type.FullName), type);
                        _types.TryAdd((null, type.AssemblyQualifiedName), type);
                        _types.TryAdd((assembly, type.Name), type);
                        _types.TryAdd((assembly, type.FullName), type);
                        _types.TryAdd((assembly, type.AssemblyQualifiedName), type);
                        // If multiple guids collide, the guid is not a unique identifier, so it is discarded.
                        if (type.HasGuid()) _guidToType.AddOrUpdate(type.GUID, type, (_, __) => null);
                    }
                }
                catch { }
            }
            AppDomain.CurrentDomain.AssemblyLoad += (_, arguments) => Register(arguments.LoadedAssembly);
            AppDomain.CurrentDomain.GetAssemblies().Iterate(Register);
        }

        public static TypeData GetData<T>() => Cache<T>.Data;
        public static TypeData GetData(this Type type) => GetData((MemberInfo)type) as TypeData;
        public static FieldData GetData(this FieldInfo field) => GetData((MemberInfo)field) as FieldData;
        public static PropertyData GetData(this PropertyInfo property) => GetData((MemberInfo)property) as PropertyData;
        public static MethodData GetData(this MethodInfo method) => GetData((MemberInfo)method) as MethodData;
        public static ConstructorData GetData(this ConstructorInfo constructor) => GetData((MemberInfo)constructor) as ConstructorData;
        public static IMemberData GetData(this MemberInfo member) =>
            member is null ? null : _memberToData.GetOrAdd(member, key => key switch
            {
                Type type => new TypeData(type),
                FieldInfo field => new FieldData(field),
                PropertyInfo property => new PropertyData(property),
                ConstructorInfo constructor => new ConstructorData(constructor),
                MethodInfo method => new MethodData(method),
                _ => new MemberData(member),
            });

        public static Option<Assembly> GetAssembly(string name) => TryGetAssembly(name, out var assembly) ? assembly : default;
        public static Option<Type> GetType(string name) => TryGetType(name, out var type) ? type : default;
        public static Option<Type> GetType(string assembly, string name) => TryGetType(assembly, name, out var type) ? type : default;
        public static Option<Type> GetType(Assembly assembly, string name) => TryGetType(assembly, name, out var type) ? type : default;
        public static Option<Type> GetType(Guid guid) => TryGetType(guid, out var type) ? type : default;

        public static bool TryGetAssembly(string name, out Assembly assembly) => _assemblies.TryGetValue(name, out assembly);
        public static bool TryGetType(string name, out Type type) => TryGetType("", name, out type);
        public static bool TryGetType(string assembly, string name, out Type type) =>
            TryGetAssembly(assembly, out var current) & TryGetType(current, name, out type);
        public static bool TryGetType(Assembly assembly, string name, out Type type) => _types.TryGetValue((assembly, name), out type);
        public static bool TryGetType(Guid guid, out Type type) => _guidToType.TryGetValue(guid, out type) && type != null;
        public static bool TryGetGuid(Type type, out Guid guid)
        {
            guid = type.GUID;
            return TryGetType(guid, out var other) && type == other;
        }

        public static bool HasGuid<T>(this T provider) where T : ICustomAttributeProvider =>
            provider.IsDefined(typeof(GuidAttribute), true);

        public static string Trimmed(this Type type) => type.Name.Split('`').First();

        public static string Format(this Type type)
        {
            var name = type.Name;
            if (type.IsGenericParameter) return name;

            if (type.IsGenericType)
            {
                var arguments = string.Join(", ", type.GetGenericArguments().Select(Format));
                name = $"{type.Trimmed()}<{arguments}>";
            }

            return string.Join(".", type.Declaring().Reverse().Select(Trimmed).Append(name));
        }

        public static string FullFormat(this Type type) =>
            type.DeclaringType is Type ? $"{type.DeclaringType.FullFormat()}.{type.Format()}" :
            type.Namespace is string ? $"{type.Namespace}.{type.Format()}" :
            type.Format();

        public static string Format(this MethodInfo method)
        {
            var name = method.Name.Split(new[] { "__" }, StringSplitOptions.None).LastOrDefault().Split('|').FirstOrDefault();
            return method.IsGenericMethod ?
                $"{name}<{string.Join(", ", method.GetGenericArguments().Select(type => type.Format()))}>" :
                name;
        }

        public static bool IsNullable(this Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);

        public static bool Is(this Type type, Type other)
        {
            static bool Equal(Type left, Type right) =>
                left == right || right.IsAssignableFrom(left) || left.GenericDefinition() == right;

            if (Equal(type, other)) return true;
            else if (other.IsGenericType)
            {
                var definition = other.GetGenericTypeDefinition();
                foreach (var child in type.Hierarchy()) if (Equal(child, definition)) return true;
            }
            return false;
        }

        public static bool Is<T>(this Type type) => typeof(T).IsAssignableFrom(type);
        public static bool IsStatic(this Type type) => type.IsAbstract && type.IsSealed;
        public static bool IsByRefLike(this Type type) => _isByRefLike(type);
        public static bool IsConcrete(this Type type) => !type.IsAbstract && !type.IsInterface && !type.IsGenericTypeDefinition && !type.IsGenericParameter;

        public static bool IsPlain(this Type type)
        {
            if (type.IsPrimitive || type.IsPointer || type.IsEnum) return true;
            return type.IsValueType && type.Fields(true, false).All(field => field.FieldType.IsPlain());
        }

        public static bool IsBlittable(this Type type)
        {
            if (type.IsArray || type.IsPointer) return type.GetElementType().IsBlittable();
            if (type.IsEnum) return type.GetEnumUnderlyingType().IsBlittable();
            if (type.IsGenericType) return false;
            if (type.IsPrimitive) return type != typeof(bool) && type != typeof(char) && type != typeof(decimal);
            return type.IsValueType && type.Fields(true, false).All(field => field.FieldType.IsBlittable());
        }

        public static unsafe Option<int> Size(this Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: return sizeof(bool);
                case TypeCode.Byte: return sizeof(byte);
                case TypeCode.Char: return sizeof(char);
                case TypeCode.DateTime: return sizeof(DateTime);
                case TypeCode.Decimal: return sizeof(decimal);
                case TypeCode.Double: return sizeof(double);
                case TypeCode.Int16: return sizeof(short);
                case TypeCode.Int32: return sizeof(int);
                case TypeCode.Int64: return sizeof(long);
                case TypeCode.SByte: return sizeof(sbyte);
                case TypeCode.Single: return sizeof(float);
                case TypeCode.UInt16: return sizeof(ushort);
                case TypeCode.UInt32: return sizeof(uint);
                case TypeCode.UInt64: return sizeof(ulong);
                default:
                    // Do not 'try-catch' 'Marshal.SizeOf' because it may cause inconsistencies between
                    // serialization and deserialization if they occur on different platforms
                    if (type.IsBlittable()) return Marshal.SizeOf(type);
                    return Option.None();
            }
        }

        public static IEnumerable<string> Path(this Type type)
        {
            var stack = new Stack<string>();
            var current = type;
            var root = type;
            while (current != null)
            {
                stack.Push(current.Name.Split('`').FirstOrDefault());
                root = current;
                current = current.DeclaringType;
            }

            return stack.Prepend(root.Namespace.Split('.'));
        }

        public static IEnumerable<Type> Hierarchy(this Type type)
        {
            yield return type;
            foreach (var @base in type.Bases()) yield return @base;
            foreach (var @interface in type.GetInterfaces()) yield return @interface;
        }

        public static IEnumerable<Type> Bases(this Type type)
        {
            type = type.BaseType;
            while (type != null)
            {
                yield return type;
                type = type.BaseType;
            }
        }

        public static IEnumerable<Type> Declaring(this Type type)
        {
            type = type.DeclaringType;
            while (type != null)
            {
                yield return type;
                type = type.DeclaringType;
            }
        }

        public static Type[] GenericArguments(this Type type) =>
            type.IsGenericType ? type.GetGenericArguments() : Array.Empty<Type>();

        public static Type[] GenericConstraints(this Type type) =>
            type.IsGenericParameter ? type.GetGenericParameterConstraints() : Array.Empty<Type>();

        public static GenericParameterAttributes GenericAttributes(this Type type) =>
            type.IsGenericParameter ? type.GenericParameterAttributes : default;

        public static Option<Type> GenericDefinition(this Type type) =>
            type.IsGenericType ? type.GetGenericTypeDefinition() : default;

        public static Option<object> DefaultInstance(this Type type)
        {
            try { return Array.CreateInstance(type, 1).GetValue(0); }
            catch { return default; }
        }

        public static Option<Type> DictionaryInterface(this Type type, bool generic) => generic ?
            type.GetInterfaces().FirstOrNone(@interface => @interface.GenericDefinition() == typeof(IDictionary<,>)) :
            type.GetInterfaces().FirstOrNone(@interface => @interface == typeof(IDictionary));

        public static Option<(Type key, Type value)> DictionaryArguments(this Type type, bool generic) => generic ?
            type.DictionaryInterface(true).Bind(enumerable => enumerable.GetGenericArguments().Two()) :
            type.DictionaryInterface(false).Return((typeof(object), typeof(object)));

        public static Option<Type> EnumerableInterface(this Type type, bool generic) => generic ?
            type.GetInterfaces().FirstOrNone(@interface => @interface.GenericDefinition() == typeof(IEnumerable<>)) :
            type.GetInterfaces().FirstOrNone(@interface => @interface == typeof(IEnumerable));

        public static Option<Type> EnumerableArgument(this Type type, bool generic) => generic ?
            type.EnumerableInterface(generic).Bind(enumerable => enumerable.GetGenericArguments().FirstOrNone()) :
            type.EnumerableInterface(generic).Return(typeof(object));

        public static Option<ConstructorInfo> EnumerableConstructor(this Type type, bool generic) =>
            type.EnumerableArgument(generic)
                .Bind(argument => argument.ArrayType())
                .Bind(array => type.Constructors(true, false).FirstOrNone(constructor =>
                    constructor.GetParameters() is var parameters &&
                    parameters.Length == 1 &&
                    array.Is(parameters[0].ParameterType)));

        public static Option<Type> ArrayType(this Type type) => Option.Try(type, state => state.MakeArrayType());
        public static Option<Type> PointerType(this Type type) => Option.Try(type, state => state.MakePointerType());

        public static Option<ConstructorInfo> SerializableConstructor(this Type type) =>
            type.Is<ISerializable>() ?
            type.Constructors(true, false).FirstOrNone(constructor =>
                constructor.GetParameters() is var parameters &&
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(SerializationInfo) &&
                parameters[1].ParameterType == typeof(StreamingContext)) :
            Option.None();

        public static IEnumerable<MemberInfo> Members(this Type type, bool instance = true, bool @static = true)
        {
            var flags = default(BindingFlags);
            if (instance) flags |= Instance;
            if (@static) flags |= Static;
            if (flags == default) return Array.Empty<MemberInfo>();
            return type.Hierarchy().SelectMany(@base => @base.GetMembers(flags));
        }

        public static IEnumerable<TypeInfo> Types(this Type type, bool instance = true, bool @static = true) =>
            type.Members(instance, @static).OfType<TypeInfo>();

        public static IEnumerable<FieldInfo> Fields(this Type type, bool instance = true, bool @static = true) =>
            type.Members(instance, @static).OfType<FieldInfo>();

        public static IEnumerable<PropertyInfo> Properties(this Type type, bool instance = true, bool @static = true) =>
            type.Members(instance, @static).OfType<PropertyInfo>();

        public static IEnumerable<MethodInfo> Methods(this Type type, bool instance = true, bool @static = true) =>
            type.Members(instance, @static).OfType<MethodInfo>();

        public static IEnumerable<EventInfo> Events(this Type type, bool instance = true, bool @static = true) =>
            type.Members(instance, @static).OfType<EventInfo>();

        public static IEnumerable<ConstructorInfo> Constructors(this Type type, bool instance = true, bool @static = true)
        {
            var flags = default(BindingFlags);
            if (instance) flags |= Instance;
            if (@static) flags |= Static;
            if (flags == default) return Array.Empty<ConstructorInfo>();
            return type.GetConstructors(flags);
        }

        public static Option<ConstructorInfo> DefaultConstructor(this Type type) =>
            type.DefaultConstructors().TryFirst(out var pair) && pair.parameters.Length == 0 ? pair.constructor : Option.None();

        public static IEnumerable<(ConstructorInfo constructor, object[] parameters)> DefaultConstructors(this Type type)
        {
            return Get(type).OrderBy(pair => pair.parameters.Length);

            static IEnumerable<(ConstructorInfo constructor, object[] parameters)> Get(Type type)
            {
                foreach (var constructor in type.Constructors(true, false))
                {
                    if (constructor.IsAbstract) continue;
                    var parameters = constructor.GetParameters();
                    if (parameters.All(parameter => parameter.HasDefaultValue))
                        yield return (constructor, parameters.Select(parameter => parameter.DefaultValue));
                }
            }
        }

        public static Option<PropertyInfo> AutoProperty(this FieldInfo field)
        {
            if (field.IsPrivate && field.Name[0] == '<' &&
                field.Name.IndexOf('>') is var index && index > 0 &&
                field.Name.Substring(1, index - 1) is var name &&
                field.DeclaringType.GetProperty(name, All) is PropertyInfo property &&
                field.FieldType == property.PropertyType)
                return property;
            return Option.None();
        }

        public static Option<FieldInfo> BackingField(this PropertyInfo property) =>
            property.DeclaringType.Fields().FirstOrNone(field => field.AutoProperty() == property);

        public static bool IsAbstract(this PropertyInfo property) =>
            (property.GetMethod?.IsAbstract ?? false) || (property.SetMethod?.IsAbstract ?? false);
        public static bool IsConcrete(this PropertyInfo property) => !property.IsAbstract();
    }
}
