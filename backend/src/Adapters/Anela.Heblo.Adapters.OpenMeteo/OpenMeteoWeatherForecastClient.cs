using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.Logistics.Weather;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.OpenMeteo;

public class OpenMeteoWeatherForecastClient : IWeatherForecastClient
{
    public const string CacheKey = "OpenMeteo_Forecast";

    private readonly HttpClient _httpClient;
    private readonly WeatherForecastOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OpenMeteoWeatherForecastClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public OpenMeteoWeatherForecastClient(
        HttpClient httpClient,
        IOptions<WeatherForecastOptions> options,
        IMemoryCache cache,
        ILogger<OpenMeteoWeatherForecastClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CityForecast>> GetForecastAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<CityForecast>? cached) && cached is not null)
            return cached;

        var lats = string.Join(",", _options.Cities.Select(c => c.Latitude.ToString(CultureInfo.InvariantCulture)));
        var lons = string.Join(",", _options.Cities.Select(c => c.Longitude.ToString(CultureInfo.InvariantCulture)));
        var url = $"/v1/forecast?latitude={lats}&longitude={lons}&daily=temperature_2m_max,temperature_2m_min,weather_code&forecast_days=7&timezone=Europe%2FPrague";

        _logger.LogInformation("Fetching weather forecast from Open-Meteo for {CityCount} cities", _options.Cities.Count);

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var locations = JsonSerializer.Deserialize<List<OpenMeteoLocationResponse>>(json, JsonOptions)
            ?? throw new InvalidOperationException("Open-Meteo returned null response body");

        if (locations.Count != _options.Cities.Count)
            throw new InvalidOperationException(
                $"Open-Meteo returned {locations.Count} locations but {_options.Cities.Count} were requested");

        // Open-Meteo preserves input order in batch responses, so index i corresponds to Cities[i].
        // The count guard above ensures no out-of-bounds access if this assumption ever breaks.
        var forecasts = locations
            .Select((loc, i) =>
            {
                var dayCount = loc.Daily.Time.Count;
                if (loc.Daily.TemperatureMax.Count != dayCount
                    || loc.Daily.TemperatureMin.Count != dayCount
                    || loc.Daily.WeatherCode.Count != dayCount)
                    throw new InvalidOperationException(
                        $"Open-Meteo daily arrays for '{_options.Cities[i].Name}' have inconsistent lengths");

                return new CityForecast(
                    CityName: _options.Cities[i].Name,
                    Days: loc.Daily.Time
                        .Select((time, j) => new CityForecastDay(
                            Date: DateOnly.Parse(time),
                            MinTemperatureCelsius: loc.Daily.TemperatureMin[j],
                            MaxTemperatureCelsius: loc.Daily.TemperatureMax[j],
                            WeatherCode: loc.Daily.WeatherCode[j]))
                        .ToList());
            })
            .ToList();

        _cache.Set(CacheKey, (IReadOnlyList<CityForecast>)forecasts,
            TimeSpan.FromMinutes(_options.CacheDurationMinutes));

        return forecasts;
    }

    private sealed class OpenMeteoLocationResponse
    {
        [JsonPropertyName("daily")]
        public OpenMeteoDailyData Daily { get; init; } = new();
    }

    private sealed class OpenMeteoDailyData
    {
        [JsonPropertyName("time")]
        public List<string> Time { get; init; } = new();

        [JsonPropertyName("temperature_2m_max")]
        public List<double> TemperatureMax { get; init; } = new();

        [JsonPropertyName("temperature_2m_min")]
        public List<double> TemperatureMin { get; init; } = new();

        [JsonPropertyName("weather_code")]
        public List<int> WeatherCode { get; init; } = new();
    }
}
