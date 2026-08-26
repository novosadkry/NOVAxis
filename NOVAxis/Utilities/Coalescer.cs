using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NOVAxis.Utilities
{
    public sealed class Coalescer<TKey, TValue>
    {
        private readonly ConcurrentDictionary<TKey, Entry> _running = new();

        public async Task<TValue> RunAsync(
            TKey key,
            Func<CancellationToken, Task<TValue>> work,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);

            var entry = Join(key, work);

            try
            {
                return await entry.Work.WaitAsync(cancellationToken);
            }
            finally
            {
                if (entry.Leave())
                    Abandon(key, entry);
            }
        }

        private Entry Join(TKey key, Func<CancellationToken, Task<TValue>> work)
        {
            while (true)
            {
                if (_running.TryGetValue(key, out var running))
                {
                    if (running.TryJoin())
                        return running;

                    _running.TryRemove(new KeyValuePair<TKey, Entry>(key, running));
                    continue;
                }

                var candidate = new Entry(work);

                if (_running.TryAdd(key, candidate))
                    return candidate;

                candidate.Dispose();
            }
        }

        private void Abandon(TKey key, Entry entry)
        {
            _running.TryRemove(new KeyValuePair<TKey, Entry>(key, entry));
            entry.Cancel();
        }

        private sealed class Entry : IDisposable
        {
            private readonly CancellationTokenSource _lifetime = new();
            private readonly Lazy<Task<TValue>> _work;

            private int _callers = 1;

            public Entry(Func<CancellationToken, Task<TValue>> work)
            {
                _work = new Lazy<Task<TValue>>(
                    () => Start(work), LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public Task<TValue> Work => _work.Value;

            public bool TryJoin()
            {
                while (true)
                {
                    var callers = Volatile.Read(ref _callers);

                    if (callers == 0)
                        return false;

                    if (Interlocked.CompareExchange(ref _callers, callers + 1, callers) == callers)
                        return true;
                }
            }

            public bool Leave() => Interlocked.Decrement(ref _callers) == 0;

            public void Cancel()
            {
                Observe(_work);

                try
                {
                    _lifetime.Cancel();
                }
                catch (ObjectDisposedException) { }
            }

            public void Dispose()
            {
                if (!_work.IsValueCreated)
                    _lifetime.Dispose();
            }

            private Task<TValue> Start(Func<CancellationToken, Task<TValue>> work)
            {
                try
                {
                    return work(_lifetime.Token) ?? Task.FromResult(default(TValue));
                }
                catch (Exception e)
                {
                    return Task.FromException<TValue>(e);
                }
            }

            private static void Observe(Lazy<Task<TValue>> work)
            {
                if (!work.IsValueCreated)
                    return;

                _ = work.Value.ContinueWith(
                    static t => _ = t.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
        }
    }
}
