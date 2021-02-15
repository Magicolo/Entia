using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Entia.Core
{
    public static partial class Option
    {
        public static Option<T> At<T>(this T[] values, int index) => values.TryAt(index, out var value) ? value : Option.None();

        public static Option<T[]> All<T>(this Option<T>[] options)
        {
            if (options.Length == 0) return From(Array.Empty<T>());

            var values = new T[options.Length];
            for (var i = 0; i < options.Length; i++)
            {
                if (options[i].TryValue(out values[i])) continue;
                else return None();
            }
            return values;
        }

        public static Option<T> Any<T>(this Option<T>[] options)
        {
            foreach (var option in options) if (option.TryValue(out var value)) return value;
            return None();
        }

        public static IEnumerable<T> Choose<T>(this Option<T>[] options)
        {
            foreach (var option in options) if (option.TryValue(out var value)) yield return value;
        }

        public static IEnumerable<TResult> Choose<TSource, TResult>(this TSource[] source, Func<TSource, Option<TResult>> map)
        {
            foreach (var item in source) if (map(item).TryValue(out var value)) yield return value;
        }

        public static Option<T> FirstOrNone<T>(this T[] source)
        {
            if (source.Length > 0) return source[0];
            return None();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> FirstOrNone<T>(this T[] source, Func<T, bool> predicate)
        {
            foreach (var item in source) if (predicate(item)) return item;
            return None();
        }

        public static Option<T> LastOrNone<T>(this T[] source)
        {
            if (source.Length > 0) return source[source.Length - 1];
            return None();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> LastOrNone<T>(this T[] source, Func<T, bool> predicate)
        {
            for (int i = source.Length - 1; i >= 0; i--)
            {
                var item = source[i];
                if (predicate(item)) return item;
            }
            return None();
        }
    }
}
