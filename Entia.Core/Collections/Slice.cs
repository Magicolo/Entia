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

                public Enumerator(Read slice, int index = -1)
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

            public static implicit operator Read(T[] array) => array.Slice();

            public ref readonly T this[int index] => ref this[(uint)index];
            public ref readonly T this[uint index] => ref Array[index * Step + Index];

            public readonly uint Count;
            internal readonly uint Index;
            internal readonly uint Step;
            internal readonly T[] Array;

            internal Read(T[] array, uint index, uint count, uint step)
            {
                Array = array;
                Index = index;
                Step = step;
                Count = count;
            }

            /// <inheritdoc cref="IEnumerable{T}.GetEnumerator"/>
            public Enumerator GetEnumerator() => new(this);
            IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct Enumerator : IEnumerator<T>
        {
            public static implicit operator Read.Enumerator(Enumerator enumerator) => new(enumerator._slice, enumerator._index);

            /// <inheritdoc cref="IEnumerator{T}.Current"/>
            public ref T Current => ref _slice[_index];
            T IEnumerator<T>.Current => Current;
            object IEnumerator.Current => Current;

            readonly Slice<T> _slice;
            int _index;

            public Enumerator(Slice<T> slice, int index = -1)
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

        public static implicit operator Slice<T>(T[] array) => array.Slice();
        public static implicit operator Read(Slice<T> slice) => new(slice.Array, slice.Index, slice.Count, slice.Step);

        public ref T this[int index] => ref this[(uint)index];
        public ref T this[uint index] => ref Array[index * Step + Index];

        public readonly uint Count;
        internal readonly uint Index;
        internal readonly uint Step;
        internal readonly T[] Array;

        internal Slice(T[] array, uint index, uint count, uint step)
        {
            Array = array;
            Index = index;
            Step = step;
            Count = count;
        }

        /// <inheritdoc cref="IEnumerable{T}.GetEnumerator"/>
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
