using System;

namespace Entia.Experiment.V4
{
    public static class Buffer<TKey, TValue>
    {
        [ThreadStatic] static TValue[] _buffer;

        public static TValue[] Get(int size) =>
            _buffer == null || _buffer.Length < size ?
            _buffer = new TValue[size] : _buffer;
    }
}