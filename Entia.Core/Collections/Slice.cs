using System;
using System.Collections;
using System.Collections.Generic;
using Entia.Core.Documentation;

namespace Entia.Core
{
    [ThreadSafe]
    public readonly struct Slice<T> : IEnumerable<Slice<T>.Enumerator, T>
    {
        [ThreadSafe]
        public readonly struct Read : IEnumerable<Read.Enumerator, T>
        {
            public struct Enumerator : IEnumerator<T>
            {
                /// <inheritdoc cref="IEnumerator{T}.Current"/>
                public ref readonly T Current => ref _slice[_index];
                T IEnumerator<T>.Current => Current;
                object IEnumerator.Current => Current;

                readonly Read _slice;
                int _index;

                public Enumerator(in Read slice, int index = -1)
                {
                    _slice = slice;
                    _index = index;
                }

                /// <inheritdoc cref="IEnumerator.MoveNext"/>
                public bool MoveNext() => ++_index < _slice.Count;
                /// <inheritdoc cref="IDisposable.Dispose"/>
                public void Dispose() => this = default;
                /// <inheritdoc cref="IEnumerator.Reset"/>
                public void Reset() => _index = -1;
            }

            public static implicit operator Read(T[] array) => new(array, 0, (uint)array.Length, 1u);

            public ref readonly T this[int index] => ref this[(uint)index];
            public ref readonly T this[uint index] => ref Array[Index * Step + index];

            public readonly uint Count;
            internal readonly uint Index;
            internal readonly uint Step;
            internal readonly T[] Array;

            public Read(T[] array, uint index, uint count, uint step)
            {
                Array = array;
                Index = index;
                Step = step;
                Count = count;
            }

            public T[] ToArray()
            {
                var array = new T[Count];
                for (var i = 0u; i < Count; i++) array[i] = Array[i * Step + Index];
                return array;
            }

            /// <inheritdoc cref="IEnumerable{T}.GetEnumerator"/>
            public Enumerator GetEnumerator() => new(this);
            IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct Enumerator : IEnumerator<T>
        {
            public static implicit operator Read.Enumerator(in Enumerator enumerator) => new(enumerator._slice, enumerator._index);

            /// <inheritdoc cref="IEnumerator{T}.Current"/>
            public ref T Current => ref _slice[_index];
            T IEnumerator<T>.Current => Current;
            object IEnumerator.Current => Current;

            readonly Slice<T> _slice;
            int _index;

            public Enumerator(in Slice<T> slice, int index = -1)
            {
                _slice = slice;
                _index = index;
            }

            /// <inheritdoc cref="IEnumerator.MoveNext"/>
            public bool MoveNext() => ++_index < _slice.Count;
            /// <inheritdoc cref="IDisposable.Dispose"/>
            public void Dispose() => this = default;
            /// <inheritdoc cref="IEnumerator.Reset"/>
            public void Reset() => _index = -1;
        }

        public static implicit operator Slice<T>(T[] array) => new(array, 0, (uint)array.Length, 1u);
        public static implicit operator Read(in Slice<T> slice) => new(slice.Array, slice.Index, slice.Count, slice.Step);

        public ref T this[int index] => ref this[(uint)index];
        public ref T this[uint index] => ref Array[Index * Step + index];

        public readonly uint Count;
        internal readonly uint Index;
        internal readonly uint Step;
        internal readonly T[] Array;

        public Slice(T[] array, uint index, uint count, uint step)
        {
            Array = array;
            Index = index;
            Step = step;
            Count = count;
        }

        public T[] ToArray()
        {
            var array = new T[Count];
            for (var i = 0u; i < Count; i++) array[i] = Array[i * Step + Index];
            return array;
        }

        /// <inheritdoc cref="IEnumerable{T}.GetEnumerator"/>
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
