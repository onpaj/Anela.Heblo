using System.Collections.Concurrent;
using Anela.Heblo.Application.Common.Cache.Abstractions;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Common.Cache.Implementation;

public class ProactiveCacheDecorator<TSource, TData> : IProactiveCacheService<TData>
    where TData : class
{
    private readonly TSource _dataSource;
    private readonly Func<TSource, CancellationToken, Task<TData>> _refreshMethod;
    private readonly ICacheRefreshConfiguration _configuration;
    private readonly ILogger<ProactiveCacheDecorator<TSource, TData>> _logger;
    private readonly TimeProvider _timeProvider;

    private volatile TData? _currentData;
    private volatile TData? _previousData;
    private volatile CacheStatus _status = CacheStatus.NotLoaded;
    private DateTime? _lastRefreshTime;
    private DateTime? _lastAttemptTime;
    private volatile Exception? _lastException;

    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly object _statusLock = new();
    private readonly object _timeLock = new();

    public ProactiveCacheDecorator(
        TSource dataSource,
        Func<TSource, CancellationToken, Task<TData>> refreshMethod,
        ICacheRefreshConfiguration configuration,
        ILogger<ProactiveCacheDecorator<TSource, TData>> logger,
        TimeProvider timeProvider)
    {
        _dataSource = dataSource;
        _refreshMethod = refreshMethod;
        _configuration = configuration;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public TData? GetCurrent()
    {
        return _currentData;
    }

    public DateTime? LastRefreshTime
    {
        get
        {
            lock (_timeLock)
            {
                return _lastRefreshTime;
            }
        }
    }

    public bool IsReady => _status == CacheStatus.Ready || _status == CacheStatus.Stale;

    public CacheStatus Status => _status;

    public async Task<bool> ForceRefreshAsync(CancellationToken ct = default)
    {
        if (!_configuration.Enabled)
        {
            _logger.LogDebug("Cache refresh for {CacheName} is disabled", _configuration.Name);
            return false;
        }

        return await RefreshInternalAsync(ct, isForced: true);
    }

    internal async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        if (!_configuration.Enabled)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().DateTime;

        // Check if refresh is needed
        DateTime? lastRefresh;
        lock (_timeLock)
        {
            lastRefresh = _lastRefreshTime;
        }

        if (lastRefresh.HasValue &&
            now - lastRefresh.Value < _configuration.RefreshInterval)
        {
            return false;
        }

        return await RefreshInternalAsync(ct, isForced: false);
    }

    private async Task<bool> RefreshInternalAsync(CancellationToken ct, bool isForced)
    {
        if (!await _refreshSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
        {
            _logger.LogWarning("Cache refresh for {CacheName} timed out waiting for semaphore", _configuration.Name);
            return false;
        }

        try
        {
            lock (_statusLock)
            {
                if (_status == CacheStatus.NotLoaded || _status == CacheStatus.Failed)
                {
                    _status = CacheStatus.Loading;
                }
            }

            lock (_timeLock)
            {
                _lastAttemptTime = _timeProvider.GetUtcNow().DateTime;
            }

            _logger.LogInformation("Starting cache refresh for {CacheName} (forced: {IsForced})", _configuration.Name, isForced);

            var newData = await ExecuteWithRetryAsync(ct);

            if (newData != null)
            {
                // Atomic hot-swap
                _previousData = _currentData;
                _currentData = newData;
                lock (_timeLock)
                {
                    _lastRefreshTime = _timeProvider.GetUtcNow().DateTime;
                }
                _lastException = null;

                lock (_statusLock)
                {
                    _status = CacheStatus.Ready;
                }

                _logger.LogInformation("Successfully refreshed cache for {CacheName}", _configuration.Name);
                return true;
            }
            else
            {
                HandleRefreshFailure(new InvalidOperationException("Refresh method returned null"));
                return false;
            }
        }
        catch (Exception ex)
        {
            HandleRefreshFailure(ex);
            return false;
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    private async Task<TData?> ExecuteWithRetryAsync(CancellationToken ct)
    {
        var retryPolicy = _configuration.RetryPolicy;
        var delay = retryPolicy.BaseDelay;

        for (int attempt = 0; attempt <= retryPolicy.MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    _logger.LogWarning("Retrying cache refresh for {CacheName}, attempt {Attempt}/{MaxRetries}",
                        _configuration.Name, attempt, retryPolicy.MaxRetries);

                    await Task.Delay(delay, ct);
                    delay = TimeSpan.FromMilliseconds(Math.Min(
                        delay.TotalMilliseconds * retryPolicy.BackoffMultiplier,
                        retryPolicy.MaxDelay.TotalMilliseconds));
                }

                return await _refreshMethod(_dataSource, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache refresh attempt {Attempt} failed for {CacheName}",
                    attempt + 1, _configuration.Name);

                if (attempt == retryPolicy.MaxRetries)
                {
                    throw;
                }
            }
        }

        return null;
    }

    private void HandleRefreshFailure(Exception ex)
    {
        _lastException = ex;
        _logger.LogError(ex, "Failed to refresh cache for {CacheName}", _configuration.Name);

        lock (_statusLock)
        {
            switch (_configuration.FailureMode)
            {
                case CacheFailureMode.KeepStale:
                    if (_currentData != null)
                    {
                        _status = CacheStatus.Stale;
                    }
                    else
                    {
                        _status = CacheStatus.Failed;
                    }
                    break;

                case CacheFailureMode.ThrowException:
                    _status = CacheStatus.Failed;
                    break;

                case CacheFailureMode.ReturnNull:
                    _currentData = null;
                    _status = CacheStatus.Failed;
                    break;
            }
        }
    }

    public Exception? GetLastException() => _lastException;

    public DateTime? GetLastAttemptTime()
    {
        lock (_timeLock)
        {
            return _lastAttemptTime;
        }
    }
}