using System.Globalization;
using System.Text.Json;
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
    private readonly ILogger<HomeAssistantConditionsReadingProvider> _logger;

    public HomeAssistantConditionsReadingProvider(
        HttpClient httpClient,
        IOptions<HomeAssistantSettings> options,
        IMemoryCache cache,
        ILogger<HomeAssistantConditionsReadingProvider> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ConditionsSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_cache.TryGetValue(CacheKey, out ConditionsSnapshot? cached) && cached is not null)
            return cached;

        var innerTempTask = FetchSensorValueAsync(_settings.InnerTemperatureEntityId, cancellationToken);
        var innerHumidityTask = FetchSensorValueAsync(_settings.InnerHumidityEntityId, cancellationToken);
        var outerTempTask = FetchSensorValueAsync(_settings.OuterTemperatureEntityId, cancellationToken);
        var outerHumidityTask = FetchSensorValueAsync(_settings.OuterHumidityEntityId, cancellationToken);

        await Task.WhenAll(innerTempTask, innerHumidityTask, outerTempTask, outerHumidityTask);

        var innerTemp = innerTempTask.Result;
        var innerHumidity = innerHumidityTask.Result;
        var outerTemp = outerTempTask.Result;
        var outerHumidity = outerHumidityTask.Result;

        var nonNullCount = new[] { innerTemp, innerHumidity, outerTemp, outerHumidity }.Count(v => v.HasValue);
        var source = nonNullCount == 4 ? ConditionsReadingSource.Live
            : nonNullCount == 0 ? ConditionsReadingSource.Unavailable
            : ConditionsReadingSource.Partial;

        var snapshot = new ConditionsSnapshot(
            InnerTemperature: innerTemp,
            InnerHumidity: innerHumidity,
            OuterTemperature: outerTemp,
            OuterHumidity: outerHumidity,
            RecordedAt: DateTime.UtcNow,
            Source: source);

        _cache.Set(CacheKey, snapshot, TimeSpan.FromMinutes(_settings.ConditionsCacheDurationMinutes));

        return snapshot;
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch HomeAssistant entity {EntityId}", entityId);
            return null;
        }
    }
}
