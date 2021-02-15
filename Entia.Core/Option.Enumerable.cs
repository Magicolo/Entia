using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Entia.Core
{
    public static partial class Option
    {
        public static Option<T> At<T>(this IEnumerable<T> values, int index) => values.TryAt(index, out var value) ? value : Option.None();

        public static Option<T[]> All<T>(this IEnumerable<Option<T>> options) => All(options.ToArray());
        public static Option<T> Any<T>(this IEnumerable<Option<T>> options)
        {
            foreach (var option in options) if (option.TryValue(out var value)) return value;
            return None();
        }

        public static IEnumerable<T> Choose<T>(this IEnumerable<Option<T>> options)
        {
            foreach (var option in options) if (option.TryValue(out var value)) yield return value;
        }

        public static IEnumerable<TResult> Choose<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, Option<TResult>> map)
        {
            foreach (var item in source) if (map(item).TryValue(out var value)) yield return value;
        }

        public static Option<T> FirstOrNone<T>(this IEnumerable<T> source)
        {
            foreach (var item in source) return item;
            return None();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> FirstOrNone<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            foreach (var item in source) if (predicate(item)) return item;
            return None();
        }

        public static Option<T> LastOrNone<T>(this IEnumerable<T> source)
        {
            var option = None().AsOption<T>();
            foreach (var item in source) option = item;
            return option;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> LastOrNone<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            var option = None().AsOption<T>();
            foreach (var item in source) if (predicate(item)) option = item;
            return option;
        }
    }
}
