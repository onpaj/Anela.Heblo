using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Adapters.HomeAssistant.Telemetry;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.HomeAssistant;

public class HomeAssistantConditionsReadingProvider : IConditionsReadingProvider
{
    public const string CacheKey = "HomeAssistant_ConditionsSnapshot";

    private readonly HttpClient _httpClient;
    private readonly HomeAssistantSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly HomeAssistantSnapshotCoordinator _coordinator;
    private readonly HomeAssistantSnapshotMetrics _metrics;
    private readonly ILogger<HomeAssistantConditionsReadingProvider> _logger;

    public HomeAssistantConditionsReadingProvider(
        HttpClient httpClient,
        IOptions<HomeAssistantSettings> options,
        IMemoryCache cache,
        HomeAssistantSnapshotCoordinator coordinator,
        HomeAssistantSnapshotMetrics metrics,
        ILogger<HomeAssistantConditionsReadingProvider> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _cache = cache;
        _coordinator = coordinator;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ConditionsSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryGetCachedLive(out var cached))
            return cached!;

        var gateTimeout = ComputeGateTimeout();
        if (!await _coordinator.Gate.WaitAsync(gateTimeout, cancellationToken))
        {
            return ServeStaleOrUnavailable(duration: TimeSpan.Zero, gateTimedOut: true);
        }

        try
        {
            if (TryGetCachedLive(out var cachedAfterGate))
                return cachedAfterGate!;

            var stopwatch = Stopwatch.StartNew();

            var innerTempTask = FetchSensorValueAsync(_settings.InnerTemperatureEntityId, cancellationToken);
            var innerHumidityTask = FetchSensorValueAsync(_settings.InnerHumidityEntityId, cancellationToken);
            var outerTempTask = FetchSensorValueAsync(_settings.OuterTemperatureEntityId, cancellationToken);
            var outerHumidityTask = FetchSensorValueAsync(_settings.OuterHumidityEntityId, cancellationToken);

            await Task.WhenAll(innerTempTask, innerHumidityTask, outerTempTask, outerHumidityTask);

            stopwatch.Stop();

            var innerTemp = innerTempTask.Result;
            var innerHumidity = innerHumidityTask.Result;
            var outerTemp = outerTempTask.Result;
            var outerHumidity = outerHumidityTask.Result;

            var nonNullCount = new[] { innerTemp, innerHumidity, outerTemp, outerHumidity }.Count(v => v.HasValue);
            var source = nonNullCount == 4 ? ConditionsReadingSource.Live
                : nonNullCount == 0 ? ConditionsReadingSource.Unavailable
                : ConditionsReadingSource.Partial;

            if (source == ConditionsReadingSource.Unavailable)
            {
                return ServeStaleOrUnavailable(duration: stopwatch.Elapsed, gateTimedOut: false);
            }

            var snapshot = new ConditionsSnapshot(
                InnerTemperature: innerTemp,
                InnerHumidity: innerHumidity,
                OuterTemperature: outerTemp,
                OuterHumidity: outerHumidity,
                RecordedAt: DateTime.UtcNow,
                Source: source);

            _cache.Set(CacheKey, snapshot, TimeSpan.FromMinutes(_settings.ConditionsCacheDurationMinutes));

            if (source == ConditionsReadingSource.Live)
                _coordinator.RecordLive(snapshot);
            else
                _coordinator.RecordObserved(snapshot);

            _metrics.RecordSnapshot(source);
            LogSummary(source, nonNullCount, stopwatch.Elapsed);

            return snapshot;
        }
        finally
        {
            _coordinator.Gate.Release();
        }
    }

    private bool TryGetCachedLive(out ConditionsSnapshot? snapshot)
    {
        if (_cache.TryGetValue(CacheKey, out ConditionsSnapshot? cached) && cached is not null)
        {
            snapshot = cached;
            return true;
        }
        snapshot = null;
        return false;
    }

    private ConditionsSnapshot ServeStaleOrUnavailable(TimeSpan duration, bool gateTimedOut)
    {
        var lkg = _coordinator.LastKnownGoodLive;
        var staleMaxAge = TimeSpan.FromMinutes(_settings.StaleSnapshotMaxAgeMinutes);

        if (lkg is not null
            && _settings.StaleSnapshotMaxAgeMinutes > 0
            && (DateTime.UtcNow - lkg.RecordedAt) <= staleMaxAge)
        {
            var stale = lkg with { Source = ConditionsReadingSource.Stale };
            _coordinator.RecordObserved(stale);
            _metrics.RecordSnapshot(ConditionsReadingSource.Stale);
            LogSummary(ConditionsReadingSource.Stale, liveSensorCount: 0, duration);
            return stale;
        }

        var unavailable = new ConditionsSnapshot(null, null, null, null, DateTime.UtcNow, ConditionsReadingSource.Unavailable);
        _coordinator.RecordObserved(unavailable);
        _metrics.RecordSnapshot(ConditionsReadingSource.Unavailable);

        if (gateTimedOut)
        {
            _logger.LogWarning(
                "HomeAssistant snapshot fetch timed out waiting for single-flight gate ({GateTimeoutSeconds}s)",
                ComputeGateTimeout().TotalSeconds);
        }

        LogSummary(ConditionsReadingSource.Unavailable, liveSensorCount: 0, duration);
        return unavailable;
    }

    private void LogSummary(ConditionsReadingSource source, int liveSensorCount, TimeSpan duration)
    {
        _logger.LogInformation(
            "HomeAssistant snapshot {Source}, sensors={LiveSensorCount}, durationMs={DurationMs}",
            source, liveSensorCount, duration.TotalMilliseconds);
    }

    private TimeSpan ComputeGateTimeout()
    {
        var perAttempt = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds);
        var attempts = Math.Max(1, _settings.RetryCount + 1);
        return perAttempt * attempts + TimeSpan.FromSeconds(1);
    }

    private async Task<decimal?> FetchSensorValueAsync(string entityId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/states/{entityId}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "HomeAssistant returned {StatusCode} for entity {EntityId}",
                    response.StatusCode, entityId);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("state", out var stateProp))
            {
                _logger.LogWarning("HomeAssistant response for {EntityId} has no 'state' field", entityId);
                return null;
            }

            var stateStr = stateProp.GetString();
            if (string.IsNullOrEmpty(stateStr) ||
                stateStr.Equals("unavailable", StringComparison.OrdinalIgnoreCase) ||
                stateStr.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("HomeAssistant entity {EntityId} has non-numeric state: {State}", entityId, stateStr);
                return null;
            }

            if (!decimal.TryParse(stateStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                _logger.LogWarning("HomeAssistant entity {EntityId} returned unparseable state: {State}", entityId, stateStr);
                return null;
            }

            return value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Exception object NOT passed to LogWarning to avoid AI ILogger recording an exception trace.
            _logger.LogWarning(
                "HomeAssistant fetch exhausted retries for {EntityId} after {Attempts} attempts: {ExceptionType} {ExceptionMessage}",
                entityId, _settings.RetryCount + 1, ex.GetType().Name, ex.Message);
            return null;
        }
    }
}
