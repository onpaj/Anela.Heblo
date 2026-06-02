using Anela.Heblo.Domain.Features.Logistics.Weather;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.WeatherForecast.DashboardTiles;

[TileId("weatherforecast")]
public class WeatherForecastTile : ITile
{
    private readonly IWeatherForecastClient _weatherClient;
    private readonly ILogger<WeatherForecastTile> _logger;

    public string Title => "Předpověď počasí";
    public string Description => "5denní předpověď počasí — nejteplejší místo v ČR";
    public TileSize Size => TileSize.Large;
    public TileCategory Category => TileCategory.Manufacture;
    public bool DefaultEnabled => false;
    public bool AutoShow => false;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public WeatherForecastTile(IWeatherForecastClient weatherClient, ILogger<WeatherForecastTile> logger)
    {
        _weatherClient = weatherClient;
        _logger = logger;
    }

    public async Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var forecasts = await _weatherClient.GetForecastAsync(cancellationToken);

            var days = forecasts
                .SelectMany(city => city.Days.Select(day => (CityName: city.CityName, Day: day)))
                .GroupBy(x => x.Day.Date)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var hottest = g.MaxBy(x => x.Day.MaxTemperatureCelsius)!;
                    return new
                    {
                        date = hottest.Day.Date.ToString("yyyy-MM-dd"),
                        cityName = hottest.CityName,
                        minTemperatureCelsius = hottest.Day.MinTemperatureCelsius,
                        maxTemperatureCelsius = hottest.Day.MaxTemperatureCelsius,
                        weatherCode = hottest.Day.WeatherCode,
                    };
                })
                .ToList();

            return new { status = "success", data = new { days } };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load weather forecast for dashboard tile");
            return new { status = "error", error = "Předpověď počasí není dostupná." };
        }
    }
}
