using Anela.Heblo.Application.Features.WeatherForecast.UseCases.GetWeatherForecast;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Weather;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.WeatherForecast;

public class GetWeatherForecastHandlerTests
{
    private readonly Mock<IWeatherForecastClient> _clientMock = new();

    private GetWeatherForecastHandler CreateHandler() =>
        new(_clientMock.Object, NullLogger<GetWeatherForecastHandler>.Instance);

    private static CityForecast City(string name, params (string date, double minTemp, double maxTemp, int code)[] days) =>
        new(name, days
            .Select(d => new CityForecastDay(DateOnly.Parse(d.date), d.minTemp, d.maxTemp, d.code))
            .ToList());

    [Fact]
    public async Task Handle_SelectsHottestCityPerDay()
    {
        _clientMock.Setup(c => c.GetForecastAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CityForecast>
            {
                City("Praha",
                    ("2024-06-01", 18.0, 28.5, 0),
                    ("2024-06-02", 15.0, 25.0, 3)),
                City("Brno",
                    ("2024-06-01", 17.0, 27.0, 1),
                    ("2024-06-02", 16.0, 26.5, 2)),
            });

        var result = await CreateHandler().Handle(new GetWeatherForecastRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Days.Count);

        var day1 = result.Days.Single(d => d.Date == new DateOnly(2024, 6, 1));
        Assert.Equal("Praha", day1.CityName);
        Assert.Equal(18.0, day1.MinTemperatureCelsius);
        Assert.Equal(28.5, day1.MaxTemperatureCelsius);
        Assert.Equal(0, day1.WeatherCode);

        var day2 = result.Days.Single(d => d.Date == new DateOnly(2024, 6, 2));
        Assert.Equal("Brno", day2.CityName);
        Assert.Equal(26.5, day2.MaxTemperatureCelsius);
    }

    [Fact]
    public async Task Handle_ReturnsDaysOrderedChronologically()
    {
        _clientMock.Setup(c => c.GetForecastAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CityForecast>
            {
                City("Praha",
                    ("2024-06-03", 20.0, 30.0, 0),
                    ("2024-06-01", 18.0, 28.0, 1),
                    ("2024-06-02", 15.0, 25.0, 2)),
            });

        var result = await CreateHandler().Handle(new GetWeatherForecastRequest(), CancellationToken.None);

        var dates = result.Days.Select(d => d.Date).ToList();
        Assert.Equal(dates.OrderBy(d => d).ToList(), dates);
    }

    [Fact]
    public async Task Handle_ClientThrows_ReturnsUnsuccessfulResponse()
    {
        _clientMock.Setup(c => c.GetForecastAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        var result = await CreateHandler().Handle(new GetWeatherForecastRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.WeatherForecastUnavailable, result.ErrorCode);
        Assert.Empty(result.Days);
    }
}
