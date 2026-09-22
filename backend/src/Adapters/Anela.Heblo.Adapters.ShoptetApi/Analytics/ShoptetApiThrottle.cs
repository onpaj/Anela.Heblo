namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// Paces calls to the Shoptet REST API. Measured on the live store on 2026-09-22: ~4 requests/s
/// sequentially is served without a single 429, while 4 parallel connections (~16 req/s) starts
/// returning them immediately. The backfill walks ~97k orders, so it has to stay under that
/// ceiling for hours without tripping the limiter — and the same token serves the warehouse's
/// packing and expedition flows.
/// </summary>
public sealed class ShoptetApiThrottle
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minInterval;

    /// <summary>
    /// UTC ticks of the next allowed request. Held as a long so <see cref="Penalize"/> can push it
    /// out from outside the gate without tearing.
    /// </summary>
    private long _nextAllowedTicks;

    public ShoptetApiThrottle(double requestsPerSecond)
    {
        if (requestsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestsPerSecond));

        _minInterval = TimeSpan.FromSeconds(1d / requestsPerSecond);
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var nextAllowed = new DateTimeOffset(Interlocked.Read(ref _nextAllowedTicks), TimeSpan.Zero);

            var delay = nextAllowed - now;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct);
                now = DateTimeOffset.UtcNow;
            }

            Interlocked.Exchange(ref _nextAllowedTicks, (now + _minInterval).UtcTicks);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Pushes the next allowed slot out after a 429, so the whole client backs off together.</summary>
    public void Penalize(TimeSpan backoff)
    {
        var until = (DateTimeOffset.UtcNow + backoff).UtcTicks;

        long current;
        do
        {
            current = Interlocked.Read(ref _nextAllowedTicks);
            if (current >= until)
                return;
        }
        while (Interlocked.CompareExchange(ref _nextAllowedTicks, until, current) != current);
    }
}
