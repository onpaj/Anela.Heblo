using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Xcc.Services.Concurrency;

internal sealed class KeyedAsyncLock : IKeyedAsyncLock, IDisposable
{
    private readonly MemoryCache _entries = new(new MemoryCacheOptions());

    public async Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        while (true)
        {
            var entry = _entries.GetOrCreate(key, e =>
            {
                e.SlidingExpiration = ttl;
                var le = new LockEntry();
                le.AddRef(); // cache holds a ref — balanced by ReleaseRef in eviction callback
                e.RegisterPostEvictionCallback((_, value, _, _) =>
                {
                    if (value is LockEntry ev)
                    {
                        ev.MarkEvicted();
                        ev.ReleaseRef(); // release the cache's ref
                    }
                });
                return le;
            })!;

            entry.AddRef(); // caller's ref
            if (_entries.TryGetValue<LockEntry>(key, out var current) && ReferenceEquals(current, entry))
            {
                try
                {
                    await entry.Sem.WaitAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    entry.ReleaseRef();
                    throw;
                }
                return new Handle(entry);
            }
            entry.ReleaseRef(); // entry no longer in cache, retry
        }
    }

    public void Dispose() => _entries.Dispose();

    private sealed class LockEntry
    {
        public readonly SemaphoreSlim Sem = new(1, 1);
        private int _refCount;
        private int _evicted;
        private int _semDisposed;

        public void AddRef() => Interlocked.Increment(ref _refCount);

        public void MarkEvicted()
        {
            Interlocked.Exchange(ref _evicted, 1);
            TryDispose();
        }

        public void ReleaseRef()
        {
            Interlocked.Decrement(ref _refCount);
            TryDispose();
        }

        private void TryDispose()
        {
            if (Volatile.Read(ref _evicted) == 1 &&
                Volatile.Read(ref _refCount) <= 0 &&
                Interlocked.CompareExchange(ref _semDisposed, 1, 0) == 0)
            {
                Sem.Dispose();
            }
        }
    }

    private sealed class Handle : IAsyncDisposable
    {
        private readonly LockEntry _entry;
        private int _disposed;

        public Handle(LockEntry entry) => _entry = entry;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _entry.Sem.Release();
                _entry.ReleaseRef();
            }
            return ValueTask.CompletedTask;
        }
    }
}
