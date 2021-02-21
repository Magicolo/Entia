using System;
using System.Threading;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public static class Concurrent
    {
        public static int Push<T>(ref T[] items, ref int count, T value)
        {
            var capacity = Interlocked.Increment(ref count);
            var index = capacity - 1;
            Ensure(ref items, capacity);
            Set(ref items, index, value);
            return index;
        }

        public static int Push<T>(ref T[] items, ref int count, ReadOnlySpan<T> values) where T : unmanaged
        {
            var capacity = Interlocked.Add(ref count, values.Length);
            var index = capacity - values.Length;
            Ensure(ref items, capacity);
            Set(ref items, index, values);
            return index;
        }

        public static bool Ensure<T>(ref T[] items, int count)
        {
            var local = items;
            while (count > local.Length)
            {
                var resized = local.Resized(MathUtility.NextPowerOfTwo(count));
                if (Interlocked.CompareExchange(ref items, resized, local) == local) return true;
                local = items;
            }
            return false;
        }

        public static bool Set<T>(ref T[] items, int index, T value)
        {
            if (index < 0) return false;
            while (true)
            {
                var local = items;
                if (index >= local.Length) return false;
                local[index] = value;
                if (local == items) return true;
            }
        }

        public static bool Set<T>(ref T[] items, int index, ReadOnlySpan<T> values) where T : unmanaged
        {
            if (index < 0) return false;
            while (true)
            {
                var local = items;
                if (index >= local.Length) return false;
                values.CopyTo(local.AsSpan(index));
                if (local == items) return true;
            }
        }
    }
}