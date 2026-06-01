using System.Collections.Concurrent;

namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

public sealed class UserDashboardSettingsLock : IUserDashboardSettingsLock
{
    // Unbounded growth is acceptable — same behavior as previous static dictionary.
    // Lock keys are user IDs; eviction is out of scope (single-instance deployment).
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable> AcquireAsync(string userId, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new LockHandle(semaphore);
    }

    private sealed class LockHandle : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;

        public LockHandle(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
