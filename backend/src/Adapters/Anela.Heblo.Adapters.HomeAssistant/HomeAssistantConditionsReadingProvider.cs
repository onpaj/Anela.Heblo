using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.HomeAssistant;

public class HomeAssistantConditionsReadingProvider : IConditionsReadingProvider
{
    private readonly HttpClient _httpClient;
    private readonly HomeAssistantSettings _settings;
    private readonly ILogger<HomeAssistantConditionsReadingProvider> _logger;

    public HomeAssistantConditionsReadingProvider(
        HttpClient httpClient,
        IOptions<HomeAssistantSettings> options,
        ILogger<HomeAssistantConditionsReadingProvider> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<ConditionsSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tasks = new[]
        {
            FetchSensorValueAsync(_settings.InnerTemperatureEntityId, cancellationToken),
            FetchSensorValueAsync(_settings.InnerHumidityEntityId, cancellationToken),
            FetchSensorValueAsync(_settings.OuterTemperatureEntityId, cancellationToken),
            FetchSensorValueAsync(_settings.OuterHumidityEntityId, cancellationToken),
        };

        var values = await Task.WhenAll(tasks);

        var innerTemp = values[0];
        var innerHumidity = values[1];
        var outerTemp = values[2];
        var outerHumidity = values[3];

        var nonNullCount = values.Count(v => v.HasValue);
        var source = nonNullCount == 4 ? ConditionsReadingSource.Live
            : nonNullCount == 0 ? ConditionsReadingSource.Unavailable
            : ConditionsReadingSource.Partial;

        return new ConditionsSnapshot(
            InnerTemperature: innerTemp,
            InnerHumidity: innerHumidity,
            OuterTemperature: outerTemp,
            OuterHumidity: outerHumidity,
            RecordedAt: DateTime.UtcNow,
            Source: source);
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
