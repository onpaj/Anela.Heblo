using Anela.Heblo.Application.Features.WeatherForecast.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Weather;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.WeatherForecast.UseCases.GetWeatherForecast;

public class GetWeatherForecastHandler : IRequestHandler<GetWeatherForecastRequest, GetWeatherForecastResponse>
{
    private readonly IWeatherForecastClient _weatherClient;
    private readonly ILogger<GetWeatherForecastHandler> _logger;

    public GetWeatherForecastHandler(IWeatherForecastClient weatherClient, ILogger<GetWeatherForecastHandler> logger)
    {
        _weatherClient = weatherClient ?? throw new ArgumentNullException(nameof(weatherClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GetWeatherForecastResponse> Handle(
        GetWeatherForecastRequest request,
        CancellationToken cancellationToken)
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
                    return new HottestDayDto
                    {
                        Date = hottest.Day.Date,
                        CityName = hottest.CityName,
                        MinTemperatureCelsius = hottest.Day.MinTemperatureCelsius,
                        MaxTemperatureCelsius = hottest.Day.MaxTemperatureCelsius,
                        WeatherCode = hottest.Day.WeatherCode,
                    };
                })
                .ToList();

            return new GetWeatherForecastResponse { Days = days };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch weather forecast from Open-Meteo");
            return new GetWeatherForecastResponse(ErrorCodes.WeatherForecastUnavailable);
        }
    }
}
