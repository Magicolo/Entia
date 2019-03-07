﻿using Entia.Core;
using System;
using System.Linq;
using System.Reflection;

namespace Entia.Modules
{
    public static class ModuleUtility
    {
        public const AttributeTargets AttributeUsage = AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method;

        public static TValue Default<TKey, TValue>(this Concurrent<TypeMap<TKey, TValue>> map, Type type, Type definition = null, Type attribute = null, Func<Type, TValue> @default = null)
            where TKey : class where TValue : class =>
            map.ReadValueOrWrite(type,
                (type, definition, attribute, @default),
                state1 => Default<TValue>(state1.type, state1.definition, state1.attribute).Or(state1, state2 => state2.@default?.Invoke(state2.type)));

        public static TValue Default<TKey, TValue>(this TypeMap<TKey, TValue> map, Type type, Type definition = null, Type attribute = null, Func<Type, TValue> @default = null)
            where TKey : class where TValue : class =>
            map.TryGet(type, out var value) ? value :
            map[type] = Default<TValue>(type, definition, attribute).Or((@default, type), state => state.@default?.Invoke(state.type));

        public static TValue Default<TKey, TValue>(this TypeMap<TKey, TValue> map, Type type, Type definition, Type attribute, Type @default)
            where TKey : class where TValue : class =>
            map.Default(
                type, definition, attribute,
                @default.IsGenericType ?
                    new Func<Type, TValue>(generic => Activator.CreateInstance(@default.MakeGenericType(generic)) as TValue) :
                    new Func<Type, TValue>(_ => Activator.CreateInstance(@default) as TValue));

        public static Result<T> Default<T>(Type type, Type definition = null, Type attribute = null) where T : class
        {
            var result = Result.Failure().AsResult<T>();

            if (attribute != null && result.IsFailure())
            {
                var generic = type.GetGenericArguments();
                result = type.StaticMembers()
                    .Where(member => member.GetCustomAttributes(true).Any(current => current.GetType().Is(attribute)))
                    .Select(member =>
                        member is Type nested ? Result.Try(() =>
                            Activator.CreateInstance(nested.IsGenericTypeDefinition ? nested.MakeGenericType(type.GetGenericArguments()) : nested)) :
                        member is FieldInfo field ? Result.Try(() => field.GetValue(null)) :
                        member is PropertyInfo property ? Result.Try(() => property.GetValue(null)) :
                        member is MethodInfo method ? Result.Try(() => method.Invoke(null, Array.Empty<object>())) :
                        Result.Failure().AsResult<object>())
                    .Select(value => value.Cast<T>())
                    .Any();
            }

            if (definition != null && result.IsFailure())
            {
                result = type.Hierarchy()
                    .Where(child => child.IsGenericType && child.GetGenericTypeDefinition() == definition)
                    .SelectMany(child => child.GetGenericArguments().Select(argument => Result.Try(Activator.CreateInstance, argument).Cast<T>()))
                    .Any();
            }

            return result;
        }
    }
}
