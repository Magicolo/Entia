using System.Collections.Concurrent;
using System.Threading;
using Entia.Core;

namespace Entia.Experiment.V4
{
    struct Messages<T> { public ConcurrentQueue<T> Queue; public int Capacity; }

    public readonly struct Emitter<T>
    {
        readonly Segment _segment;
        public Emitter(World world) => _segment = world.Segment(new[] { world.Meta(typeof(Messages<T>)) }, 8);
        public void Emit(in T message)
        {
            foreach (var chunk in _segment.Chunks)
            {
                var store = (Messages<T>[])chunk.Stores[0];
                for (int i = 0; i < chunk.Count; i++)
                {
                    ref var messages = ref store[i];
                    if (messages.Capacity != 0) messages.Queue.Enqueue(message);
                    while (messages.Capacity >= 0 && messages.Queue.Count > messages.Capacity)
                        messages.Queue.TryDequeue(out _);
                }
            }
        }
    }

    public readonly struct Receiver<T>
    {
        readonly State<Messages<T>> _state;
        public Receiver(World world, int capacity = -1) => _state = new(new() { Queue = new(), Capacity = capacity }, world, 8);
        public bool TryReceive(out T message) => _state.Value.Queue.TryDequeue(out message);
    }

    public readonly struct Emitter2<T>
    {
        internal struct Messages
        {
            public T[] Buffer;
            public int Count;
        }

        readonly Resource<Messages> _messages;
        public Emitter2(World world) => _messages = world.Resource<Messages>();

        public void Emit(in T message)
        {
            ref var messages = ref _messages.Value;
            var index = Interlocked.Increment(ref messages.Count);
            var buffer = messages.Buffer;
            while (index >= buffer.Length)
            {
                Interlocked.CompareExchange(ref messages.Buffer, buffer.Resized(buffer.Length * 2), buffer);
                buffer = messages.Buffer;
            }
            buffer[index] = message;
        }
    }

    public readonly struct Receiver2<T>
    {
        public Slice<T>.Read Messages => _messages.Value.Buffer.Slice(count: _messages.Value.Count);
        readonly Resource<Emitter2<T>.Messages> _messages;
        public Receiver2(World world) => _messages = world.Resource<Emitter2<T>.Messages>();
    }
}