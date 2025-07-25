using Anela.Heblo.Application.Interfaces;
using Anela.Heblo.Domain.Constants;
using Anela.Heblo.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Services;

public class WeatherService : IWeatherService
{
    private readonly ILogger<WeatherService> _logger;

    public WeatherService(ILogger<WeatherService> logger)
    {
        _logger = logger;
    }

    public Task<IEnumerable<WeatherForecast>> GetForecastAsync()
    {
        _logger.LogInformation("Generating weather forecast for {Days} days", WeatherConstants.FORECAST_DAYS);

        var forecast = Enumerable.Range(1, WeatherConstants.FORECAST_DAYS).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(WeatherConstants.MIN_TEMPERATURE, WeatherConstants.MAX_TEMPERATURE),
            Summary = WeatherConstants.WEATHER_SUMMARIES[Random.Shared.Next(WeatherConstants.WEATHER_SUMMARIES.Length)]
        });

        return Task.FromResult(forecast);
    }
}