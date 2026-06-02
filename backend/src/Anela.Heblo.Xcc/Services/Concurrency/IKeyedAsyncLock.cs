namespace Anela.Heblo.Xcc.Services.Concurrency;

// Process-local per-key async mutex. Not safe across multiple Web App instances.
// Use a distinct key prefix per consumer to avoid collisions.
public interface IKeyedAsyncLock
{
    Task<IAsyncDisposable> AcquireAsync(
        string key,
        TimeSpan slidingExpiration,
        CancellationToken cancellationToken = default);
}
