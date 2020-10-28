using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Entia.Core;

namespace Entia.Json.Converters
{
    public sealed class ConcreteType : Converter<Type>
    {
        public override Node Convert(in Type instance, in ToContext context) => Node.Type(instance);
        public override Type Instantiate(in FromContext context) =>
            context.Node.AsType() ?? JsonType.Instance.Instantiate(context);
    }

    public sealed class JsonType : Converter<Type>
    {
        [Preserve]
        readonly struct Members
        {
            [Preserve] public readonly object Field;
            [Preserve] public object Property { get; }
            [Preserve] public void Method() { }

            [Preserve]
            public Members(object field, object property) { Field = field; Property = property; Method(); }
        }

        enum Types { Array = 1, Pointer = 2, Generic = 3 }

        public static readonly JsonType Instance = new JsonType();

        static readonly (int identifier, Type type)[] _types =
        {
            // Skip identifier '0' to detect some failure cases.
            #region System
            (1, typeof(bool)), (31, typeof(bool[])), (61, typeof(bool?)), (91, typeof(bool*)),
            (2, typeof(char)), (32, typeof(char[])), (62, typeof(char?)), (92, typeof(char*)),
            (3, typeof(byte)), (33, typeof(byte[])), (63, typeof(byte?)), (93, typeof(byte*)),
            (4, typeof(sbyte)), (34, typeof(sbyte[])), (64, typeof(sbyte?)), (94, typeof(sbyte*)),
            (5, typeof(short)), (35, typeof(short[])), (65, typeof(short?)), (95, typeof(short*)),
            (6, typeof(ushort)), (36, typeof(ushort[])), (66, typeof(ushort?)), (96, typeof(ushort*)),
            (7, typeof(int)), (37, typeof(int[])), (67, typeof(int?)), (97, typeof(int*)),
            (8, typeof(uint)), (38, typeof(uint[])), (68, typeof(uint?)), (98, typeof(uint*)),
            (9, typeof(long)), (39, typeof(long[])), (69, typeof(long?)), (99, typeof(long*)),
            (10, typeof(ulong)), (40, typeof(ulong[])), (70, typeof(ulong?)), (100, typeof(ulong*)),
            (11, typeof(float)), (41, typeof(float[])), (71, typeof(float?)), (101, typeof(float*)),
            (12, typeof(double)), (42, typeof(double[])), (72, typeof(double?)), (102, typeof(double*)),
            (13, typeof(decimal)), (43, typeof(decimal[])), (73, typeof(decimal?)), (103, typeof(decimal*)),
            (14, typeof(IntPtr)), (44, typeof(IntPtr[])), (74, typeof(IntPtr?)), (104, typeof(IntPtr*)),
            (15, typeof(DateTime)), (45, typeof(DateTime[])), (75, typeof(DateTime?)), (105, typeof(DateTime*)),
            (16, typeof(TimeSpan)), (46, typeof(TimeSpan[])), (76, typeof(TimeSpan?)), (106, typeof(TimeSpan*)),
            (17, typeof(string)), (47, typeof(string[])),
            (18, typeof(object)), (48, typeof(object[])),
            (19, typeof(Action)), (49, typeof(Action[])),
            (20, typeof(Func<bool>)), (50, typeof(Func<bool>[])),
            (21, typeof(void)), (111, typeof(void*)),

            (200, typeof(Nullable<>)),
            (210, typeof(List<>)), (211, typeof(LinkedList<>)), (212, typeof(LinkedListNode<>)),
            (220, typeof(Stack<>)), (221, typeof(Queue<>)), (222, typeof(HashSet<>)), (223, typeof(Dictionary<,>)), (224, typeof(KeyValuePair<,>)),
            (230, typeof(SortedDictionary<,>)), (231, typeof(SortedList<,>)), (232, typeof(SortedSet<>)),
            (240, typeof(Tuple<>)), (241, typeof(Tuple<,>)), (242, typeof(Tuple<,,>)), (243, typeof(Tuple<,,,>)), (244, typeof(Tuple<,,,,>)), (245, typeof(Tuple<,,,,,>)), (246, typeof(Tuple<,,,,,,>)), (247, typeof(Tuple<,,,,,,,>)),
            (250, typeof(ValueTuple<>)), (251, typeof(ValueTuple<,>)), (252, typeof(ValueTuple<,,>)), (253, typeof(ValueTuple<,,,>)), (254, typeof(ValueTuple<,,,,>)), (255, typeof(ValueTuple<,,,,,>)), (256, typeof(ValueTuple<,,,,,,>)), (257, typeof(ValueTuple<,,,,,,,>)),
            (260, typeof(Action<>)), (261, typeof(Action<,>)), (262, typeof(Action<,,>)), (263, typeof(Action<,,,>)), (264, typeof(Action<,,,,>)), (265, typeof(Action<,,,,,>)), (266, typeof(Action<,,,,,,>)), (267, typeof(Action<,,,,,,,>)), (268, typeof(Action<,,,,,,,,>)), (269, typeof(Action<,,,,,,,,,>)),
            (270, typeof(Func<>)), (271, typeof(Func<,>)), (272, typeof(Func<,,>)), (273, typeof(Func<,,,>)), (274, typeof(Func<,,,,>)), (275, typeof(Func<,,,,,>)), (276, typeof(Func<,,,,,,>)), (277, typeof(Func<,,,,,,,>)), (278, typeof(Func<,,,,,,,,>)), (279, typeof(Func<,,,,,,,,,>)),
            (280, typeof(Predicate<>)), (281, typeof(Comparison<>)),
            #endregion

            #region Reflection
            (300, typeof(object).GetType()), (301, typeof(object).Module.GetType()), (302, typeof(object).Assembly.GetType()),
            (303, typeof(Members).GetField(nameof(Members.Field)).GetType()),
            (304, typeof(Members).GetProperty(nameof(Members.Property)).GetType()),
            (305, typeof(Members).GetMethod(nameof(Members.Method)).GetType()),
            (306, typeof(Pointer)),
            #endregion
        };

        static readonly Dictionary<int, Type> _identifierToType = _types.ToDictionary(pair => pair.identifier, pair => pair.type);
        static readonly Dictionary<Type, Node> _typeToIdentifier = _types.ToDictionary(pair => pair.type, pair => Node.Number(pair.identifier));
        static readonly Node _array = Types.Array;
        static readonly Node _pointer = Types.Pointer;
        static readonly Node _generic = Types.Generic;

        public override Node Convert(in Type instance, in ToContext context)
        {
            if (_typeToIdentifier.TryGetValue(instance, out var identifier))
                return identifier;
            else if (instance.IsArray)
                return Node.Array(_array, instance.GetArrayRank(), context.Convert(instance.GetElementType(), this, this));
            else if (instance.IsPointer)
                return Node.Array(_pointer, context.Convert(instance.GetElementType(), this, this));
            else if (instance.IsConstructedGenericType)
            {
                var definition = instance.GetGenericTypeDefinition();
                var arguments = instance.GetGenericArguments();
                var items = new Node[arguments.Length + 2];
                items[0] = _generic;
                items[1] = context.Convert(definition, this, this);
                for (int i = 0; i < arguments.Length; i++)
                    items[i + 2] = context.Convert(arguments[i], this, this);
                return Node.Array(items);
            }
            else if (ReflectionUtility.TryGetGuid(instance, out var guid))
                return Node.String(guid.ToString(), Node.Tags.Plain);
            else
                return Node.String(instance.AssemblyQualifiedName);
        }

        public override Type Instantiate(in FromContext context)
        {
            var node = context.Node;
            switch (node.Kind)
            {
                case Node.Kinds.Number:
                    if (_identifierToType.TryGetValue(node.AsInt(), out var type)) return type;
                    break;
                case Node.Kinds.String:
                    {
                        var value = node.AsString();
                        if (ReflectionUtility.TryGetType(value, out type)) return type;
                        if (Guid.TryParse(value, out var guid) && ReflectionUtility.TryGetType(guid, out type)) return type;
                        break;
                    }
                case Node.Kinds.Array:
                    switch (node.Children[0].AsEnum<Types>())
                    {
                        case Types.Array:
                            var rank = node.Children[1].AsInt();
                            if (context.Convert<Type>(node.Children[2], this, this) is Type element)
                                return rank > 1 ? element.MakeArrayType(rank) : element.MakeArrayType();
                            break;
                        case Types.Pointer:
                            if (context.Convert<Type>(node.Children[1], this, this) is Type pointer)
                                return pointer.MakePointerType();
                            break;
                        case Types.Generic:
                            if (context.Convert<Type>(node.Children[1], this, this) is Type definition)
                            {
                                var arguments = new Type[node.Children.Length - 2];
                                for (int i = 0; i < arguments.Length; i++)
                                {
                                    if (context.Convert<Type>(node.Children[i + 2], this, this) is Type argument)
                                        arguments[i] = argument;
                                    else
                                        return default;
                                }
                                return definition.MakeGenericType(arguments);
                            }
                            break;
                    }
                    break;
            }
            return default;
        }
    }
}