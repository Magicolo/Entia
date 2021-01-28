using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Entia.Core
{
    public static class ReflectionExtensions
    {
        public static T CreateDelegate<T>(this MethodInfo method) where T : Delegate =>
            (T)Delegate.CreateDelegate(typeof(T), method);
        public static T CreateDelegate<T>(this MethodInfo method, object instance) where T : Delegate =>
            (T)Delegate.CreateDelegate(typeof(T), instance, method);

        public static bool IsCompilerGenerated<T>(this T provider) where T : ICustomAttributeProvider =>
            provider.IsDefined(typeof(CompilerGeneratedAttribute), true);

        public static bool IsStatic(this PropertyInfo property) =>
            property.GetMethod?.IsStatic ?? property.SetMethod.IsStatic;
        public static bool IsInstance(this FieldInfo field) => !field.IsStatic;
        public static bool IsInstance(this PropertyInfo property) => !property.IsStatic();
        public static Option<T> Combine<T>(this T[] delegates) where T : Delegate => (T)Delegate.Combine(delegates);
    }
}
