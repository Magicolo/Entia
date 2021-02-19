using System;

namespace Entia.Experiment.V4
{
    public static class Buffer
    {
        static class Cache<TKey, TValue>
        {
            [ThreadStatic] public static TValue[] Buffer;
        }

        public static bool Ensure<TKey, TValue>(ref TValue[] buffer, int size)
        {
            if (buffer == null) { buffer = Get<TKey, TValue>(size); return true; }
            if (buffer.Length >= size) return false;

            var local = buffer;
            buffer = Get<TKey, TValue>(size);
            Array.Copy(local, 0, buffer, 0, local.Length);
            return true;
        }

        public static T[] Get<T>(ref T[] buffer, int size) =>
            buffer == null || buffer.Length < size ? buffer = new T[size] : buffer;
        public static T[] Get<T>(ref T[] buffer, uint size) =>
            buffer == null || buffer.Length < size ? buffer = new T[size] : buffer;
        public static T[] Get<T>(int size) => Get(ref Cache<T, T>.Buffer, size);
        public static TValue[] Get<TKey, TValue>(int size) => Get(ref Cache<TKey, TValue>.Buffer, size);
        public static T[] Get<T>(uint size) => Get(ref Cache<T, T>.Buffer, size);
        public static TValue[] Get<TKey, TValue>(uint size) => Get(ref Cache<TKey, TValue>.Buffer, size);
    }
}