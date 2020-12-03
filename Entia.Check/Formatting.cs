using System;
using System.Reflection;
using Entia.Core;

namespace Entia.Check
{
    static class Formatting
    {
        public static class Name<T>
        {
            public static readonly string Parameters = $"<{typeof(T).Format()}>";
            public static readonly string Constant = $"{nameof(Constant)}{Parameters}";
            public static readonly string Factory = $"{nameof(Factory)}{Parameters}";
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
            public static readonly string And = $"{nameof(And)}{Parameters}";
            public static readonly string Shrink = $"{nameof(Shrink)}{Parameters}";
            public static readonly string Enumeration = $"{nameof(Enumeration)}{Parameters}";
            public static readonly string Flag = $"{nameof(Flag)}{Parameters}";
            public static readonly string Mutate = $"{nameof(Mutate)}{Parameters}";
            public static readonly string Cache = $"{nameof(Cache)}{Parameters}";
            public static readonly string Derived = $"{nameof(Derived)}{Parameters}";
            public static readonly string Choose = $"{nameof(Choose)}{Parameters}";
        }

        public static class Name<T1, T2>
        {
            public static readonly string Parameters = $"<{typeof(T1).Format()}, {typeof(T2).Format()}>";
            public static readonly string Map = $"{nameof(Map)}{Parameters}";
            public static readonly string Bind = $"{nameof(Bind)}{Parameters}";
            public static readonly string Choose = $"{nameof(Choose)}{Parameters}";
            public static readonly string And = $"{nameof(And)}{Parameters}";
        }

        public static class Name<T1, T2, T3>
        {
            public static readonly string Parameters = $"<{typeof(T1).Format()}, {typeof(T2).Format()}, {typeof(T3).Format()}>";
            public static readonly string And = $"{nameof(And)}{Parameters}";
            public static readonly string Map = $"{nameof(Map)}{Parameters}";
        }

        public static class Name<T1, T2, T3, T4>
        {
            public static readonly string Parameters = $"<{typeof(T1).Format()}, {typeof(T2).Format()}, {typeof(T3).Format()}, {typeof(T4).Format()}>";
            public static readonly string And = $"{nameof(And)}{Parameters}";
        }

        public static class Name<T1, T2, T3, T4, T5>
        {
            public static readonly string Parameters = $"<{typeof(T1).Format()}, {typeof(T2).Format()}, {typeof(T3).Format()}, {typeof(T4).Format()}, {typeof(T5).Format()}>";
            public static readonly string And = $"{nameof(And)}{Parameters}";
        }


        public static string Format<T>(T value) =>
#if DEBUG
            value is Type type ? type.Format() :
            value is MethodInfo method ? method.Format() :
            value is Delegate @delegate ? Format(@delegate.Method) :
            $"{value}";
#else
            "";
#endif

        public static string Format<T>(T[] values) =>
#if DEBUG
            $"{string.Join(", ", values)}";
#else
            "";
#endif

        public static string Format<T>(this string name, T value) =>
#if DEBUG
            $"{name}({Format(value)})";
#else
            name;
#endif

        public static string Format<T1, T2>(this string name, T1 value1, T2 value2) =>
#if DEBUG
            $"{name}({Format(value1)}, {Format(value2)})";
#else
            name;
#endif

        public static string Format<T1, T2, T3>(this string name, T1 value1, T2 value2, T3 value3) =>
#if DEBUG
            $"{name}({Format(value1)}, {Format(value2)}, {Format(value3)})";
#else
            name;
#endif

        public static string Format<T1, T2, T3, T4>(this string name, T1 value1, T2 value2, T3 value3, T4 value4) =>
#if DEBUG
            $"{name}({Format(value1)}, {Format(value2)}, {Format(value3)}, {Format(value4)})";
#else
            name;
#endif

        public static string Format<T1, T2, T3, T4, T5>(this string name, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5) =>
#if DEBUG
            $"{name}({Format(value1)}, {Format(value2)}, {Format(value3)}, {Format(value4)}, {Format(value5)})";
#else
            name;
#endif

        public static string Format<T>(this string name, T[] values) =>
#if DEBUG
            $"{name}({Format(values)})";
#else
            name;
#endif
    }
}