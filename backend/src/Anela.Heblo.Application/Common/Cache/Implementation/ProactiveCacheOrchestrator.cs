using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Common.Cache.Implementation;

public class ProactiveCacheOrchestrator : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProactiveCacheOrchestrator> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly ConcurrentDictionary<string, CacheRegistration> _cacheRegistrations = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastRefreshTimes = new();
    private readonly List<string> _dependencyOrder = new();

    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _refreshTask;

    public ProactiveCacheOrchestrator(
        IServiceProvider serviceProvider,
        ILogger<ProactiveCacheOrchestrator> logger,
        TimeProvider timeProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void RegisterCache(CacheRegistration registration)
    {
        _cacheRegistrations[registration.Name] = registration;
        _logger.LogDebug("Registered cache: {CacheName}", registration.Name);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Proactive Cache Orchestrator");

        _cancellationTokenSource = new CancellationTokenSource();

        // Build dependency order
        BuildDependencyOrder();

        // Perform initial load
        await PerformInitialLoad(_cancellationTokenSource.Token);

        // Start periodic refresh timer
        _refreshTimer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        _refreshTask = RefreshLoop(_cancellationTokenSource.Token);

        _logger.LogInformation("Proactive Cache Orchestrator started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Proactive Cache Orchestrator");

        _cancellationTokenSource?.Cancel();

        if (_refreshTask != null)
        {
            try
            {
                await _refreshTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _refreshTimer?.Dispose();
        _cancellationTokenSource?.Dispose();

        _logger.LogInformation("Proactive Cache Orchestrator stopped");
    }

    private void BuildDependencyOrder()
    {
        _dependencyOrder.Clear();
        var processed = new HashSet<string>();
        var visiting = new HashSet<string>();

        foreach (var cacheName in _cacheRegistrations.Keys)
        {
            if (!processed.Contains(cacheName))
            {
                BuildDependencyOrderRecursive(cacheName, processed, visiting);
            }
        }

        _logger.LogInformation("Dependency order built: {DependencyOrder}", string.Join(" -> ", _dependencyOrder));
    }

    private void BuildDependencyOrderRecursive(string cacheName, HashSet<string> processed, HashSet<string> visiting)
    {
        if (visiting.Contains(cacheName))
        {
            throw new InvalidOperationException($"Circular dependency detected involving cache: {cacheName}");
        }

        if (processed.Contains(cacheName))
        {
            return;
        }

        visiting.Add(cacheName);

        if (_cacheRegistrations.TryGetValue(cacheName, out var registration))
        {
            // Process dependencies first
            foreach (var dependency in registration.Configuration.Dependencies)
            {
                if (_cacheRegistrations.ContainsKey(dependency))
                {
                    BuildDependencyOrderRecursive(dependency, processed, visiting);
                }
            }
        }

        visiting.Remove(cacheName);
        processed.Add(cacheName);
        _dependencyOrder.Add(cacheName);
    }

    private async Task PerformInitialLoad(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting initial cache load");

        // Group by priority (higher priority = earlier loading)
        var priorityGroups = _cacheRegistrations.Values
            .Where(r => r.Configuration.Enabled)
            .GroupBy(r => r.Configuration.Priority)
            .OrderByDescending(g => g.Key);

        foreach (var priorityGroup in priorityGroups)
        {
            // Within same priority, respect dependency order
            var cachesInGroup = priorityGroup.ToList();
            var orderedCaches = _dependencyOrder
                .Where(name => cachesInGroup.Any(c => c.Name == name))
                .Select(name => cachesInGroup.First(c => c.Name == name))
                .ToList();

            foreach (var cache in orderedCaches)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Wait for initial delay
                if (cache.Configuration.InitialDelay > TimeSpan.Zero)
                {
                    _logger.LogDebug("Waiting {InitialDelay} before loading {CacheName}",
                        cache.Configuration.InitialDelay, cache.Name);
                    await Task.Delay(cache.Configuration.InitialDelay, cancellationToken);
                }

                await RefreshSingleCache(cache, cancellationToken, isInitialLoad: true);
            }
        }

        _logger.LogInformation("Initial cache load completed");
    }

    private async Task RefreshLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (_refreshTimer != null && await _refreshTimer.WaitForNextTickAsync(cancellationToken))
            {
                await PerformPeriodicRefresh(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in cache refresh loop");
        }
    }

    private async Task PerformPeriodicRefresh(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().DateTime;

        foreach (var cacheName in _dependencyOrder)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (!_cacheRegistrations.TryGetValue(cacheName, out var cache))
                continue;

            if (!cache.Configuration.Enabled)
                continue;

            // Check if refresh is needed
            var lastRefresh = _lastRefreshTimes.GetValueOrDefault(cacheName, DateTime.MinValue);
            if (now - lastRefresh >= cache.Configuration.RefreshInterval)
            {
                await RefreshSingleCache(cache, cancellationToken, isInitialLoad: false);
            }
        }
    }

    private async Task RefreshSingleCache(CacheRegistration cache, CancellationToken cancellationToken, bool isInitialLoad)
    {
        try
        {
            _logger.LogDebug("Refreshing cache: {CacheName} (initial: {IsInitialLoad})", cache.Name, isInitialLoad);

            // Use reflection to call RefreshAsync method
            var refreshMethod = cache.CacheServiceType.GetMethod("RefreshAsync");
            if (refreshMethod != null)
            {
                var refreshTask = (Task<bool>?)refreshMethod.Invoke(cache.CacheService, new object[] { cancellationToken });
                if (refreshTask != null)
                {
                    var success = await refreshTask;
                    if (success)
                    {
                        _lastRefreshTimes[cache.Name] = _timeProvider.GetUtcNow().DateTime;
                        _logger.LogDebug("Successfully refreshed cache: {CacheName}", cache.Name);
                    }
                    else
                    {
                        _logger.LogWarning("Cache refresh returned false for: {CacheName}", cache.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh cache: {CacheName}", cache.Name);
        }
    }

    public IReadOnlyDictionary<string, object> GetCacheStatuses()
    {
        var statuses = new Dictionary<string, object>();

        foreach (var (name, registration) in _cacheRegistrations)
        {
            try
            {
                var statusProperty = registration.CacheServiceType.GetProperty("Status");
                var lastRefreshProperty = registration.CacheServiceType.GetProperty("LastRefreshTime");
                var isReadyProperty = registration.CacheServiceType.GetProperty("IsReady");

                statuses[name] = new
                {
                    Status = statusProperty?.GetValue(registration.CacheService),
                    LastRefreshTime = lastRefreshProperty?.GetValue(registration.CacheService),
                    IsReady = isReadyProperty?.GetValue(registration.CacheService),
                    Configuration = new
                    {
                        registration.Configuration.Enabled,
                        registration.Configuration.RefreshInterval,
                        registration.Configuration.Priority,
                        Dependencies = registration.Configuration.Dependencies
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get status for cache: {CacheName}", name);
                statuses[name] = new { Error = ex.Message };
            }
        }

        return statuses;
    }

    public async Task<bool> ForceRefreshAsync(string cacheName, CancellationToken cancellationToken = default)
    {
        if (!_cacheRegistrations.TryGetValue(cacheName, out var cache))
        {
            _logger.LogWarning("Cache not found for force refresh: {CacheName}", cacheName);
            return false;
        }

        try
        {
            var forceRefreshMethod = cache.CacheServiceType.GetMethod("ForceRefreshAsync");
            if (forceRefreshMethod != null)
            {
                var refreshTask = (Task<bool>?)forceRefreshMethod.Invoke(cache.CacheService, new object[] { cancellationToken });
                if (refreshTask != null)
                {
                    var success = await refreshTask;
                    if (success)
                    {
                        _lastRefreshTimes[cacheName] = _timeProvider.GetUtcNow().DateTime;
                        _logger.LogInformation("Successfully force refreshed cache: {CacheName}", cacheName);
                    }
                    return success;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to force refresh cache: {CacheName}", cacheName);
        }

        return false;
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        _cancellationTokenSource?.Dispose();
    }
}