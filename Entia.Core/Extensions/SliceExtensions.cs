using System;
using System.Collections.Generic;

namespace Entia.Core
{
    public static class SliceExtensions
    {
        public static Slice<T>.Read AsRead<T>(this Slice<T> source) => source;
        public static Slice<T> Slice<T>(this T[] source) => new(source, 0, (uint)source.Length, 1u);
        public static Slice<T> Slice<T>(this T[] source, uint index = 0, uint? count = null, uint step = 1u) => source.Slice().Slice(index, count, step);
        public static Slice<T> Slice<T>(this T[] source, int index = 0, int? count = null, int step = 1) => source.Slice().Slice((uint)index, (uint)count, (uint)step);
        public static Slice<T> Slice<T>(this (T[] items, int count) source) => source.items.Slice(0, source.count);
        public static Slice<T> Slice<T>(this (T[] items, uint count) source) => source.items.Slice(0u, source.count);

        public static Slice<T> Slice<T>(this Slice<T> source, int index = 0, int? count = null, int step = 1) => source.Slice((uint)index, (uint)count, (uint)step);
        public static Slice<T> Slice<T>(this Slice<T> source, uint index = 0u, uint? count = null, uint step = 1u)
        {
            index = Math.Min(index, source.Count);
            step = Math.Max(step, 1u);
            return new(
                source.Array,
                source.Index + index * source.Step,
                Math.Min(count ?? source.Count, (source.Count - index) / step),
                source.Step * step);
        }

        public static Slice<T>.Read Slice<T>(this Slice<T>.Read source, int index = 0, int? count = null, int step = 1) => source.Slice((uint)index, (uint)count, (uint)step);
        public static Slice<T>.Read Slice<T>(this Slice<T>.Read source, uint index = 0u, uint? count = null, uint step = 1u)
        {
            index = Math.Min(index, source.Count);
            step = Math.Max(step, 1u);
            return new(
                source.Array,
                source.Index + index * source.Step,
                Math.Min(count ?? source.Count, (source.Count - index) / step),
                source.Step * step);
        }

        public static void CopyTo<T>(this Slice<T> source, Slice<T> target) => source.AsRead().CopyTo(target);
        public static void CopyTo<T>(this Slice<T>.Read source, Slice<T> target)
        {
            var count = Math.Min(source.Count, target.Count);
            for (var i = 0u; i < count; i++) target[i] = source[i];
        }

        public static T[] ToArray<T>(this Slice<T> source) => source.AsRead().ToArray();
        public static T[] ToArray<T>(this Slice<T>.Read source)
        {
            if (source.Count == 0u) return Array.Empty<T>();
            var array = new T[source.Count];
            source.CopyTo(array);
            return array;
        }

        public static Option<T> Get<T>(this Slice<T> source, int index) => source.AsRead().Get(index);
        public static Option<T> Get<T>(this Slice<T>.Read source, int index) =>
            index < source.Count ? source[index] : Option.None();
        public static bool TryGet<T>(this Slice<T> source, int index, out T value) => source.AsRead().TryGet(index, out value);
        public static bool TryGet<T>(this Slice<T>.Read source, int index, out T value) =>
            source.Get(index).TryValue(out value);

        public static void Reset<T>(ref this (T[] items, int count) source, Slice<T>.Read items)
        {
            source.count = (int)items.Count;
            source.Ensure();
            items.CopyTo(source.items);
        }

        public static void Push<T>(ref this (T[] items, int count) source, Slice<T>.Read items) => source.Insert(source.count, items);
        public static bool Insert<T>(ref this (T[] items, int count) source, int index, Slice<T>.Read items)
        {
            if (items.Count == 1) return source.Insert(index, items[0]);
            if (index < 0 || index > source.count || items.Count == 0) return false;

            var count = source.count;
            source.Ensure(source.count += (int)items.Count);
            if (index < count) Array.Copy(source.items, index, source.items, index + items.Count, count - index);
            items.CopyTo(source.items.Slice((uint)index, items.Count));
            return true;
        }

        public static bool Contains<T>(this Slice<T> source, T item, IEqualityComparer<T> comparer = null) =>
            source.AsRead().Contains(item, comparer);
        public static bool Contains<T>(this Slice<T>.Read source, T item, IEqualityComparer<T> comparer = null) =>
            source.IndexOf(item, comparer).IsSome();

        public static Option<uint> IndexOf<T>(this Slice<T> source, T item, IEqualityComparer<T> comparer = null) =>
            source.AsRead().IndexOf(item, comparer);
        public static Option<uint> IndexOf<T>(this Slice<T>.Read source, T item, IEqualityComparer<T> comparer = null)
        {
            comparer ??= EqualityComparer<T>.Default;
            for (var i = 0u; i < source.Count; i++) if (comparer.Equals(source[i], item)) return i;
            return Option.None();
        }

        public static IEnumerable<T> Except<T>(this Slice<T> source, T item, IEqualityComparer<T> comparer = null) =>
            source.AsRead().Except(item, comparer);
        public static IEnumerable<T> Except<T>(this Slice<T>.Read source, T item, IEqualityComparer<T> comparer = null)
        {
            comparer ??= EqualityComparer<T>.Default;
            if (source.Count == 0 || comparer.Equals(source[0], item)) return Array.Empty<T>();
            return Enumerate(source, item, comparer);

            static IEnumerable<T> Enumerate(Slice<T>.Read source, T comparand, IEqualityComparer<T> comparer)
            {
                foreach (var item in source)
                {
                    if (comparer.Equals(item, comparand)) continue;
                    yield return item;
                }
            }
        }
    }
}