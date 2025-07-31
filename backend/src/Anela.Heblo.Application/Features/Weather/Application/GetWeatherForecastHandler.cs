using Anela.Heblo.Application.Features.Weather.Contracts;
using Anela.Heblo.Application.Features.Weather.Domain;
using Microsoft.Extensions.Logging;
using MediatR;

namespace Anela.Heblo.Application.Features.Weather.Application;

public class GetWeatherForecastHandler : IRequestHandler<GetWeatherForecastRequest, IEnumerable<GetWeatherForecastResponse>>
{
    private readonly ILogger<GetWeatherForecastHandler> _logger;

    public GetWeatherForecastHandler(ILogger<GetWeatherForecastHandler> logger)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<GetWeatherForecastResponse>> Handle(GetWeatherForecastRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling GetWeatherForecast request - generating forecast for {Days} days", WeatherConstants.FORECAST_DAYS);

        var forecast = Enumerable.Range(1, WeatherConstants.FORECAST_DAYS).Select(index =>
            new GetWeatherForecastResponse
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = Random.Shared.Next(WeatherConstants.MIN_TEMPERATURE, WeatherConstants.MAX_TEMPERATURE),
                Summary = WeatherConstants.WEATHER_SUMMARIES[Random.Shared.Next(WeatherConstants.WEATHER_SUMMARIES.Length)]
            });

        return await Task.FromResult(forecast);
    }
}