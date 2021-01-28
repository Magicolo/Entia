using System;
using System.Collections.Concurrent;
using System.Reflection;
using Entia.Core.Documentation;

namespace Entia.Core
{
    [ThreadSafe]
    public static class DefaultUtility
    {
        [ThreadSafe]
        static class Cache<T>
        {
            public static readonly Func<T> Provide = Provider<T>();
        }

        static readonly ConcurrentDictionary<Type, Delegate> _generics = new();
        static readonly ConcurrentDictionary<Type, Func<object>> _reflections = new();

        public static T Default<T>() => Cache<T>.Provide();
        public static object Default(Type type) => Provider(type)();

        static Func<T> Provider<T>()
        {
            return (Func<T>)_generics.GetOrAdd(typeof(T), _ => Create());

            static Func<T> Create()
            {
                var provide = Provide();
                var reflection = new Func<object>(() => provide());
                _reflections.AddOrUpdate(typeof(T), () => reflection, (_, _) => reflection);
                return provide;
            }

            static Func<T> Provide()
            {
                foreach (var member in typeof(T).GetMembers(ReflectionUtility.Static))
                {
                    try
                    {
                        if (member.IsDefined(typeof(DefaultAttribute), true))
                        {
                            switch (member)
                            {
                                case FieldInfo field when field.FieldType.Is<T>():
                                    var value = (T)field.GetValue(null);
                                    return () => value;
                                case PropertyInfo property when property.PropertyType == typeof(T) && property.GetMethod is MethodInfo getter:
                                    return getter.CreateDelegate<Func<T>>();
                                case MethodInfo method when method.ReturnType == typeof(T) && method.GetParameters().None():
                                    return method.CreateDelegate<Func<T>>();
                            }
                        }
                    }
                    catch { }
                }

                if (typeof(T).DefaultInstance().Cast<T>().TryValue(out var instance))
                    return () => CloneUtility.Shallow(instance);
                else if (typeof(T).DefaultConstructors().TryFirst(out var pair))
                    return () => (T)pair.constructor.Invoke(pair.parameters);
                else
                    return () => default;
            }
        }

        static Func<object> Provider(Type type)
        {
            return _reflections.GetOrAdd(type, key => Create(key));

            static Func<object> Create(Type type)
            {
                foreach (var member in type.GetMembers(ReflectionUtility.Static))
                {
                    try
                    {
                        if (member.IsDefined(typeof(DefaultAttribute), true))
                        {
                            switch (member)
                            {
                                case FieldInfo field when field.FieldType.Is(type):
                                    var value = field.GetValue(null);
                                    return () => value;
                                case PropertyInfo property when property.PropertyType.Is(type) && property.GetMethod is MethodInfo getter:
                                    return () => getter.Invoke(null, Array.Empty<object>());
                                case MethodInfo method when method.ReturnType.Is(type) && method.GetParameters().None():
                                    return () => method.Invoke(null, Array.Empty<object>());
                            }
                        }
                    }
                    catch { }
                }

                if (type.DefaultInstance().TryValue(out var instance))
                    return () => CloneUtility.Shallow(instance);
                else if (type.DefaultConstructors().TryFirst(out var pair))
                    return () => pair.constructor.Invoke(pair.parameters);
                else
                    return () => default;
            }
        }
    }
}