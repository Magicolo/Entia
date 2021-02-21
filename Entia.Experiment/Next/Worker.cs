using System;
using System.Threading;

namespace Entia.Experiment.V4
{
    public sealed class Worker : IDisposable
    {
        public enum States { Ready, Working, Disposed }

        public States State => _disposed ? States.Disposed : _ready == _threads.Length ? States.Ready : States.Working;
        readonly object _lock = new();
        int _index;
        int _ready;
        bool _disposed;
        Action[] _jobs = Array.Empty<Action>();
        Thread[] _threads = Array.Empty<Thread>();

        public Worker()
        {
            _threads = new Thread[Math.Max(Environment.ProcessorCount / 4, 1)];
            for (int i = 0; i < _threads.Length; i++)
            {
                var thread = new Thread(Work) { IsBackground = true };
                thread.Start();
                _threads[i] = thread;
            }
            Ready();
        }

        public bool Do(Action[] jobs)
        {
            if (_disposed || _ready < _threads.Length) return false;
            if (jobs.Length == 0) return true;
            if (jobs.Length == 1) { jobs[0](); return true; }

            _index = 0;
            _jobs = jobs;
            lock (_lock) Monitor.PulseAll(_lock);
            while (TryWork()) { }
            Ready();
            return true;
        }

        public void Dispose() { _disposed = true; lock (_lock) Monitor.PulseAll(_lock); }

        bool TryWork() => TryWork(Interlocked.Increment(ref _index) - 1);
        bool TryWork(int index)
        {
            if (index < _jobs.Length)
            {
                try { _jobs[index](); } catch { }
                return true;
            }
            return false;
        }

        void Work()
        {
            while (true)
            {
                Wait();
                if (_disposed) return;
                while (TryWork()) { }
            }
        }

        void Ready()
        {
            var spin = new SpinWait();
            while (_ready < _threads.Length) spin.SpinOnce();
        }

        void Wait() { lock (_lock) { _ready++; Monitor.Wait(_lock); _ready--; } }
    }
}