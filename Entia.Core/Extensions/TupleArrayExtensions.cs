using System;
using System.Collections.Generic;

namespace Entia.Core
{
    public static class TupleArrayExtensions
    {
        public static bool Set<T>(ref this (T[] items, int count) source, int index, T item)
        {
            var resized = source.Ensure(source.count = Math.Max(source.count, index + 1));
            source.items[index] = item;
            return resized;
        }

        public static Option<T> Get<T>(this (T[] items, int count) source, int index) =>
            source.TryGet(index, out var value) ? value : Option.None();

        public static bool TryGet<T>(this (T[] items, int count) source, int index, out T value)
        {
            if (index >= 0 || index < source.count)
            {
                value = source.items[index];
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryGet<T>(this (T[] items, uint count) source, uint index, out T value)
        {
            if (index < source.count)
            {
                value = source.items[index];
                return true;
            }
            value = default;
            return false;
        }

        public static T[] ToArray<T>(this (T[] items, int count) source) => source.items.Take(source.count);

        public static bool Ensure<T>(ref this (T[] items, int count) source) => ArrayUtility.Ensure(ref source.items, source.count);
        public static bool Ensure<T>(ref this (T[] items, uint count) source) => ArrayUtility.Ensure(ref source.items, source.count);
        public static bool Ensure<T>(ref this (T[] items, int count) source, uint size) => ArrayUtility.Ensure(ref source.items, size);
        public static bool Ensure<T>(ref this (T[] items, int count) source, int size) => ArrayUtility.Ensure(ref source.items, size);
        public static bool Ensure<T>(ref this (T[] items, uint count) source, int size) => ArrayUtility.Ensure(ref source.items, size);
        public static bool Ensure<T>(ref this (T[] items, uint count) source, uint size) => ArrayUtility.Ensure(ref source.items, size);

        public static void Push<T>(ref this (T[] items, int count) source, params T[] items) => source.Insert(source.count, items);
        public static void Push<T>(ref this (T[] items, int count) source, T item) => source.Insert(source.count, item);

        public static T Pop<T>(ref this (T[] items, int count) source) => source.items[--source.count];
        public static bool TryPop<T>(ref this (T[] items, int count) source, out T item)
        {
            if (source.count > 0)
            {
                item = source.Pop();
                return true;
            }

            item = default;
            return false;
        }

        public static ref T Peek<T>(ref this (T[] items, int count) source) => ref source.items[source.count - 1];
        public static bool TryPeek<T>(ref this (T[] items, int count) source, out T item)
        {
            if (source.count > 0)
            {
                item = source.Peek();
                return true;
            }

            item = default;
            return false;
        }

        public static bool Contains<T>(this (T[] items, int count) source, T item) => source.IndexOf(item).IsSome();

        public static bool Insert<T>(ref this (T[] items, int count) source, int index, params T[] items)
        {
            if (items.Length == 1) return source.Insert(index, items[0]);
            if (index < 0 || index > source.count || items.Length == 0) return false;

            var count = source.count;
            source.Ensure(source.count += items.Length);
            if (index < count) Array.Copy(source.items, index, source.items, index + items.Length, count - index);
            Array.Copy(items, 0, source.items, index, items.Length);
            return true;
        }

        public static bool Insert<T>(ref this (T[] items, int count) source, int index, T item)
        {
            if (index < 0 || index > source.count) return false;

            var count = source.count;
            source.Ensure(source.count += 1);
            if (index < count) Array.Copy(source.items, index, source.items, index + 1, count - index);
            source.items[index] = item;
            return true;
        }

        public static bool Remove<T>(ref this (T[] items, int count) source, T item) =>
            source.IndexOf(item).TryValue(out var index) && source.RemoveAt(index);

        public static bool RemoveAt<T>(ref this (T[] items, int count) source, int index)
        {
            if (index < 0 || index >= source.count || source.items == null) return false;
            source.count--;
            if (index < source.count) Array.Copy(source.items, index + 1, source.items, index, source.count - index);
            else source.items[index] = default;
            return true;
        }

        public static Option<int> IndexOf<T>(this (T[] items, int count) source, T item) =>
            Array.IndexOf(source.items, item, 0, source.count) is var index && index >= 0 ?
            Option.Some(index) : Option.None();

        public static IEnumerable<T> Except<T>(this (T[] items, int count) source, T item, IEqualityComparer<T> comparer = null)
        {
            comparer ??= EqualityComparer<T>.Default;
            if (source.count == 0 || comparer.Equals(source.items[0], item)) return Array.Empty<T>();
            return Enumerate(source, item, comparer);

            static IEnumerable<T> Enumerate((T[] items, int count) source, T comparand, IEqualityComparer<T> comparer)
            {
                for (int i = 0; i < source.count; i++)
                {
                    var item = source.items[i];
                    if (comparer.Equals(item, comparand)) continue;
                    yield return item;
                }
            }
        }

        public static bool Clear<T>(ref this (T[] items, int count) source)
        {
            if (source.count <= 0) return false;
            Array.Clear(source.items, 0, source.count);
            source.count = 0;
            return true;
        }

        public static (T[] items, int count) Clone<T>(this (T[] items, int count) source) => ((T[])source.items.Clone(), source.count);
    }
}