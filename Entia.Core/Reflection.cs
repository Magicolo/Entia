using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Entia.Core
{
    public interface IMemberData
    {
        MemberInfo Member { get; }
    }

    /// <summary> Caches metadata about a type lazily. </summary>
    public sealed class TypeData : IMemberData
    {
        public static implicit operator TypeData(Type type) => type.GetData();
        public static implicit operator Type(TypeData type) => type?.Type;

        public readonly Type Type;
        public TypeCode Code => _code.Value;
        public Option<Guid> Guid => _guid.Value;
        public Option<TypeData> Array => _array.Value;
        public Option<TypeData> Pointer => _pointer.Value;
        public Option<TypeData> Element => _element.Value;
        public Option<TypeData> Definition => _definition.Value;
        public Dictionary<int, IMemberData> Members => _members.Value;
        public IMemberData[] StaticMembers => _staticMembers.Value;
        public IMemberData[] InstanceMembers => _instanceMembers.Value;
        public Dictionary<string, FieldData> Fields => _fields.Value;
        public Dictionary<string, PropertyData> Properties => _properties.Value;
        public FieldData[] InstanceFields => _instanceFields.Value;
        public PropertyData[] InstanceProperties => _instanceProperties.Value;
        public MethodData[] InstanceMethods => _instanceMethods.Value;
        public ConstructorData[] InstanceConstructors => _instanceConstructors.Value;
        public Option<ConstructorData> DefaultConstructor => _defaultConstructor.Value;
        public TypeData[] Interfaces => _interfaces.Value;
        public TypeData[] Declaring => _declaring.Value;
        public TypeData[] Arguments => _arguments.Value;
        public TypeData[] Bases => _bases.Value;
        public bool IsPlain => _isPlain.Value;
        public bool IsBlittable => _isBlittable.Value;
        public Option<object> Default => _default.Value;
        public Option<int> Size => _size.Value;

        MemberInfo IMemberData.Member => Type;

        readonly Lazy<TypeCode> _code;
        readonly Lazy<Option<Guid>> _guid;
        readonly Lazy<Option<TypeData>> _array;
        readonly Lazy<Option<TypeData>> _pointer;
        readonly Lazy<Option<TypeData>> _element;
        readonly Lazy<Option<TypeData>> _definition;
        readonly Lazy<Dictionary<int, IMemberData>> _members;
        readonly Lazy<IMemberData[]> _staticMembers;
        readonly Lazy<IMemberData[]> _instanceMembers;
        readonly Lazy<Dictionary<string, FieldData>> _fields;
        readonly Lazy<Dictionary<string, PropertyData>> _properties;
        readonly Lazy<FieldData[]> _instanceFields;
        readonly Lazy<PropertyData[]> _instanceProperties;
        readonly Lazy<MethodData[]> _instanceMethods;
        readonly Lazy<ConstructorData[]> _instanceConstructors;
        readonly Lazy<Option<ConstructorData>> _defaultConstructor;
        readonly Lazy<TypeData[]> _interfaces;
        readonly Lazy<TypeData[]> _declaring;
        readonly Lazy<TypeData[]> _arguments;
        readonly Lazy<TypeData[]> _bases;
        readonly Lazy<bool> _isPlain;
        readonly Lazy<bool> _isBlittable;
        readonly Lazy<Option<object>> _default;
        readonly Lazy<Option<int>> _size;

        public TypeData(Type type)
        {
            static Type GetElement(Type current)
            {
                if (current.IsArray || current.IsPointer) return current.GetElementType();
                if (current.IsEnum) return current.GetEnumUnderlyingType();
                if (current.IsNullable()) return current.GetGenericArguments().FirstOrDefault();
                return current.GetInterfaces()
                    .FirstOrDefault(child => child.IsGenericType && child.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    ?.GetGenericArguments()
                    ?.FirstOrDefault();
            }

            Type = type;
            _array = new Lazy<Option<TypeData>>(() => Option.Try(() => Type.MakeArrayType().GetData()));
            _pointer = new Lazy<Option<TypeData>>(() => Option.Try(() => Type.MakePointerType().GetData()));
            _guid = new Lazy<Option<Guid>>(() => Option.Some(Type.GUID).Filter(Type.HasGuid()));
            _code = new Lazy<TypeCode>(() => Type.GetTypeCode(type));
            _interfaces = new Lazy<TypeData[]>(() => Type.GetInterfaces().Select(@interface => @interface.GetData()));
            _bases = new Lazy<TypeData[]>(() => Type.Bases().Select(@base => @base.GetData()).ToArray());
            _element = new Lazy<Option<TypeData>>(() => GetElement(Type).GetData());
            _definition = new Lazy<Option<TypeData>>(() => (Type.IsGenericType ? Type.GetGenericTypeDefinition() : default).GetData());
            _members = new Lazy<Dictionary<int, IMemberData>>(() => Type.Members().DistinctBy(member => member.MetadataToken).Select(member => member.GetData()).ToDictionary(member => member.Member.MetadataToken));
            _staticMembers = new Lazy<IMemberData[]>(() => Type.Members(false, true).Select(member => member.GetData()).ToArray());
            _instanceMembers = new Lazy<IMemberData[]>(() => Type.Members(true, false).Select(member => member.GetData()).ToArray());
            _fields = new Lazy<Dictionary<string, FieldData>>(() => Type.Fields().Select(field => field.GetData()).ToDictionary(field => field.Field.Name));
            _properties = new Lazy<Dictionary<string, PropertyData>>(() => Type.Properties().Select(property => property.GetData()).ToDictionary(property => property.Property.Name));
            _instanceFields = new Lazy<FieldData[]>(() => Type.Fields(true, false).Select(field => field.GetData()).ToArray());
            _instanceProperties = new Lazy<PropertyData[]>(() => Type.Properties(true, false).Select(property => property.GetData()).ToArray());
            _instanceMethods = new Lazy<MethodData[]>(() => Type.Methods(true, false).Select(method => method.GetData()).ToArray());
            // NOTE: do not use 'InstanceMembers' such that base class constructors are not included
            _instanceConstructors = new Lazy<ConstructorData[]>(() => Type.Constructors(true, false).Select(constructor => constructor.GetData()).ToArray());
            _defaultConstructor = new Lazy<Option<ConstructorData>>(() => Type.DefaultConstructor().Map(constructor => constructor.GetData()));
            _declaring = new Lazy<TypeData[]>(() => Type.Declaring().Select(declaring => declaring.GetData()).ToArray());
            _arguments = new Lazy<TypeData[]>(() => Type.GetGenericArguments().Select(argument => argument.GetData()));
            _isPlain = new Lazy<bool>(() => Type.IsPlain());
            _isBlittable = new Lazy<bool>(() => Type.IsBlittable());
            _default = new Lazy<Option<object>>(() => Type.DefaultInstance());
            _size = new Lazy<Option<int>>(() => Type.Size());
        }

        public override string ToString() => Type.FullFormat();
    }

    /// <summary> Caches metadata about a field lazily. </summary>
    public sealed class FieldData : IMemberData
    {
        public static implicit operator FieldData(FieldInfo field) => field.GetData();
        public static implicit operator FieldInfo(FieldData field) => field.Field;

        public readonly FieldInfo Field;
        public TypeData Type => _type.Value;
        public TypeData Declaring => _declaring.Value;
        public Option<PropertyData> AutoProperty => _autoProperty.Value;

        MemberInfo IMemberData.Member => Field;

        readonly Lazy<TypeData> _type;
        readonly Lazy<TypeData> _declaring;
        readonly Lazy<Option<PropertyData>> _autoProperty;

        public FieldData(FieldInfo field)
        {
            Field = field;
            _type = new Lazy<TypeData>(() => field.FieldType);
            _declaring = new Lazy<TypeData>(() => field.DeclaringType);
            _autoProperty = new Lazy<Option<PropertyData>>(() => field.AutoProperty().Map(ReflectionUtility.GetData));
        }
    }

    /// <summary> Caches metadata about a property lazily. </summary>
    public sealed class PropertyData : IMemberData
    {
        public static implicit operator PropertyData(PropertyInfo property) => property.GetData();
        public static implicit operator PropertyInfo(PropertyData property) => property.Property;

        public readonly PropertyInfo Property;
        public TypeData Type => _type.Value;
        public TypeData Declaring => _declaring.Value;
        public Option<MethodData> Get => _get.Value;
        public Option<MethodData> Set => _set.Value;
        public Option<FieldData> BackingField => _backingField.Value;

        MemberInfo IMemberData.Member => Property;

        readonly Lazy<TypeData> _type;
        readonly Lazy<TypeData> _declaring;
        readonly Lazy<Option<MethodData>> _get;
        readonly Lazy<Option<MethodData>> _set;
        readonly Lazy<Option<FieldData>> _backingField;

        public PropertyData(PropertyInfo property)
        {
            Property = property;
            _type = new Lazy<TypeData>(() => Property.PropertyType);
            _declaring = new Lazy<TypeData>(() => Property.DeclaringType);
            _get = new Lazy<Option<MethodData>>(() => Property.GetMethod.GetData());
            _set = new Lazy<Option<MethodData>>(() => Property.SetMethod.GetData());
            _backingField = new Lazy<Option<FieldData>>(() => Property.BackingField().Map(ReflectionUtility.GetData));
        }
    }

    /// <summary> Caches metadata about a method lazily. </summary>
    public sealed class MethodData : IMemberData
    {
        public static implicit operator MethodData(MethodInfo method) => method.GetData();
        public static implicit operator MethodInfo(MethodData method) => method.Method;

        public readonly MethodInfo Method;
        public TypeData Declaring => _declaring.Value;
        public TypeData Return => _return.Value;
        public ParameterInfo[] Parameters => _parameters.Value;

        MemberInfo IMemberData.Member => Method;

        readonly Lazy<TypeData> _declaring;
        readonly Lazy<TypeData> _return;
        readonly Lazy<ParameterInfo[]> _parameters;

        public MethodData(MethodInfo method)
        {
            Method = method;
            _declaring = new Lazy<TypeData>(() => Method.DeclaringType);
            _return = new Lazy<TypeData>(() => Method.ReturnType);
            _parameters = new Lazy<ParameterInfo[]>(() => Method.GetParameters());
        }
    }

    /// <summary> Caches metadata about a constructor lazily. </summary>
    public sealed class ConstructorData : IMemberData
    {
        public static implicit operator ConstructorData(ConstructorInfo constructor) => constructor.GetData();
        public static implicit operator ConstructorInfo(ConstructorData constructor) => constructor.Constructor;

        public readonly ConstructorInfo Constructor;
        public TypeData Declaring => _declaring.Value;
        public ParameterInfo[] Parameters => _parameters.Value;

        MemberInfo IMemberData.Member => Constructor;

        readonly Lazy<TypeData> _declaring;
        readonly Lazy<ParameterInfo[]> _parameters;

        public ConstructorData(ConstructorInfo constructor)
        {
            Constructor = constructor;
            _declaring = new Lazy<TypeData>(() => Constructor.DeclaringType);
            _parameters = new Lazy<ParameterInfo[]>(() => Constructor.GetParameters());
        }
    }

    public sealed class MemberData : IMemberData
    {
        public MemberInfo Member { get; }

        public MemberData(MemberInfo member) { Member = member; }
    }
}