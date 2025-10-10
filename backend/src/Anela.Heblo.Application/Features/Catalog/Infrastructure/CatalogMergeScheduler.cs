using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

public class CatalogMergeScheduler : ICatalogMergeScheduler
{
    private readonly ILogger<CatalogMergeScheduler> _logger;
    private readonly CatalogCacheOptions _options;
    private readonly CancellationToken _applicationStopping;

    private readonly SemaphoreSlim _mergeSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> _invalidationTimes = new();
    private readonly object _timerLock = new();

    private Timer? _debounceTimer;
    private DateTime _lastMergeCompleted = DateTime.MinValue;
    private DateTime _firstPendingInvalidation = DateTime.MinValue;
    private bool _mergeScheduled = false;
    private volatile bool _disposed = false;

    private Func<CancellationToken, Task>? _mergeCallback;

    public CatalogMergeScheduler(
        ILogger<CatalogMergeScheduler> logger,
        IOptions<CatalogCacheOptions> options,
        IHostApplicationLifetime applicationLifetime)
    {
        _logger = logger;
        _options = options.Value;
        _applicationStopping = applicationLifetime.ApplicationStopping;
    }

    public void SetMergeCallback(Func<CancellationToken, Task> mergeCallback)
    {
        _mergeCallback = mergeCallback;
    }

    public bool IsMergeInProgress => _mergeSemaphore.CurrentCount == 0;

    public void ScheduleMerge(string dataSource)
    {
        if (_disposed || _applicationStopping.IsCancellationRequested) return;

        var now = DateTime.UtcNow;
        _invalidationTimes.TryAdd(dataSource, now);

        lock (_timerLock)
        {
            if (_disposed || _applicationStopping.IsCancellationRequested) return;

            // Track first invalidation time for max interval enforcement
            if (_firstPendingInvalidation == DateTime.MinValue)
            {
                _firstPendingInvalidation = now;
            }

            // Check if we need to force merge due to max interval
            var timeSinceFirstInvalidation = now - _firstPendingInvalidation;
            if (timeSinceFirstInvalidation >= _options.MaxMergeInterval)
            {
                _logger.LogInformation("Force executing merge due to max interval {MaxInterval}ms reached",
                    _options.MaxMergeInterval.TotalMilliseconds);

                // Execute immediately
                _ = Task.Run(async () => await ExecuteMergeAsync(), _applicationStopping);
                return;
            }

            // Reset debounce timer
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(async _ => await ExecuteMergeAsync(),
                null, _options.DebounceDelay, Timeout.InfiniteTimeSpan);

            _mergeScheduled = true;

            _logger.LogDebug("Merge scheduled for source {DataSource}, debounce delay {Delay}ms",
                dataSource, _options.DebounceDelay.TotalMilliseconds);
        }
    }

    private async Task ExecuteMergeAsync()
    {
        if (_disposed || _applicationStopping.IsCancellationRequested || !_mergeScheduled || _mergeCallback == null) return;

        if (!await _mergeSemaphore.WaitAsync(100)) // Don't block if merge already running
        {
            _logger.LogDebug("Merge already in progress, skipping");
            return;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Starting background merge operation");

            await _mergeCallback(_applicationStopping);

            _lastMergeCompleted = DateTime.UtcNow;
            _mergeScheduled = false;
            _firstPendingInvalidation = DateTime.MinValue;
            _invalidationTimes.Clear();

            _logger.LogInformation("Background merge completed in {Duration}ms",
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background merge failed after {Duration}ms",
                stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            if (!_disposed)
            {
                _mergeSemaphore.Release();
            }
            stopwatch.Stop();
        }
    }

    public DateTime GetLastMergeTime() => _lastMergeCompleted;

    public bool HasPendingMerge() => _mergeScheduled;

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        _mergeSemaphore?.Dispose();
    }
}