using System;
using System.Threading;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public static class Concurrent
    {
        public static int Push<T>(ref T[] location, ref int count, T value)
        {
            var capacity = Interlocked.Increment(ref count);
            var index = capacity - 1;
            Ensure(ref location, MathUtility.NextPowerOfTwo(capacity));
            Set(ref location, index, value);
            return index;
        }

        public static int Push<T>(ref T[] location, ref int count, ReadOnlySpan<T> values) where T : unmanaged
        {
            var capacity = Interlocked.Add(ref count, values.Length);
            var index = capacity - values.Length;
            Ensure(ref location, MathUtility.NextPowerOfTwo(capacity));
            Set(ref location, index, values);
            return index;
        }

        public static bool Ensure<T>(ref T[] location, int count) =>
            MutateUntilValid(ref location, count,
                static (items, count) => items.Length > count,
                static (items, count) => items.Resized(count));

        public static bool Extend<T>(ref T[] location, int count, Func<int, T> provide) =>
            MutateUntilValid(ref location, (count, provide),
                static (items, pair) => items.Length > pair.count,
                static (items, pair) => items.Extended(pair.count, pair.provide));

        public static bool Set<T>(ref T[] location, int index, T value)
        {
            while (true)
            {
                var local = location;
                if (index >= local.Length) return false;
                local[index] = value;
                if (local == location) return true;
            }
        }

        public static bool Set<T>(ref T[] location, int index, ReadOnlySpan<T> values) where T : unmanaged
        {
            while (true)
            {
                var local = location;
                if (index >= local.Length) return false;
                values.CopyTo(local.AsSpan(index));
                if (local == location) return true;
            }
        }

        public static bool TryMutate<T, TState>(ref T location, TState state, Func<T, TState, T> mutate, out T value) where T : class
        {
            var local = location;
            value = mutate(local, state);
            return Interlocked.CompareExchange(ref location, value, local) == local;
        }

        public static bool MutateUntilValid<T, TState>(ref T location, TState state, Func<T, TState, bool> validate, Func<T, TState, T> mutate) where T : class
        {
            while (true)
            {
                var local = location;
                if (validate(local, state)) return false;
                if (Interlocked.CompareExchange(ref location, mutate(local, state), local) == local) return true;
            }
        }

        public static TTarget MutateUntilGet<TSource, TTarget, TState>(ref TSource location, TState state, Func<TSource, TState, Option<TTarget>> get, Func<TSource, TState, TSource> mutate) where TSource : class
        {
            while (true)
            {
                var local = location;
                if (get(local, state).TryValue(out var value)) return value;
                Interlocked.CompareExchange(ref location, mutate(local, state), local);
            }
        }
    }
}