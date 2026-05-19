using System.Text.Json;
using Anela.Heblo.Application.Features.WeatherForecast.DashboardTiles;
using Anela.Heblo.Domain.Features.Logistics.Weather;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.WeatherForecast.DashboardTiles;

public sealed class WeatherForecastTileTests
{
    private readonly Mock<IWeatherForecastClient> _clientMock;
    private readonly Mock<ILogger<WeatherForecastTile>> _loggerMock;
    private readonly WeatherForecastTile _tile;

    public WeatherForecastTileTests()
    {
        _clientMock = new Mock<IWeatherForecastClient>();
        _loggerMock = new Mock<ILogger<WeatherForecastTile>>();
        _tile = new WeatherForecastTile(_clientMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void Metadata_HasCorrectValues()
    {
        _tile.Title.Should().Be("Předpověď počasí");
        _tile.Description.Should().Be("5denní předpověď počasí — nejteplejší místo v ČR");
        _tile.Size.Should().Be(TileSize.Large);
        _tile.Category.Should().Be(TileCategory.Manufacture);
        _tile.DefaultEnabled.Should().BeFalse();
        _tile.AutoShow.Should().BeFalse();
        _tile.ComponentType.Should().Be(typeof(object));
        _tile.RequiredPermissions.Should().BeEmpty();
    }

    [Fact]
    public void TileId_IsWeatherForecast()
    {
        TileExtensions.GetTileId<WeatherForecastTile>().Should().Be("weatherforecast");
    }

    [Fact]
    public async Task LoadDataAsync_WithMultipleCities_ReturnsHottestCityPerDay()
    {
        var forecasts = new List<CityForecast>
        {
            new("Praha", new[]
            {
                new CityForecastDay(new DateOnly(2026, 5, 19), 14.0, 22.0, 1),
            }),
            new("Brno", new[]
            {
                new CityForecastDay(new DateOnly(2026, 5, 19), 16.0, 28.0, 0),
            }),
        };

        _clientMock.Setup(x => x.GetForecastAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(forecasts);

        var result = await _tile.LoadDataAsync();

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("status").GetString().Should().Be("success");
        var days = json.GetProperty("data").GetProperty("days");
        days.GetArrayLength().Should().Be(1);
        var day = days[0];
        day.GetProperty("cityName").GetString().Should().Be("Brno");
        day.GetProperty("maxTemperatureCelsius").GetDouble().Should().Be(28.0);
        day.GetProperty("minTemperatureCelsius").GetDouble().Should().Be(16.0);
        day.GetProperty("date").GetString().Should().Be("2026-05-19");
    }

    [Fact]
    public async Task LoadDataAsync_WithEmptyForecast_ReturnsSuccessWithEmptyDays()
    {
        _clientMock.Setup(x => x.GetForecastAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CityForecast>());

        var result = await _tile.LoadDataAsync();

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("status").GetString().Should().Be("success");
        json.GetProperty("data").GetProperty("days").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_WhenClientThrows_ReturnsErrorStatus()
    {
        _clientMock.Setup(x => x.GetForecastAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Open-Meteo unreachable"));

        var result = await _tile.LoadDataAsync();

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("status").GetString().Should().Be("error");
        json.GetProperty("error").GetString().Should().Be("Předpověď počasí není dostupná.");
    }

    [Fact]
    public async Task LoadDataAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _clientMock
            .Setup(x => x.GetForecastAsync(It.Is<CancellationToken>(t => t.IsCancellationRequested)))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _tile.LoadDataAsync(cancellationToken: cts.Token));
    }
}
