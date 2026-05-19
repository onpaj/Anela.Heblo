using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Anela.Heblo.Adapters.OpenMeteo.Tests;

public class OpenMeteoWeatherForecastClientTests
{
    private static readonly string TwoCityThreeDayJson = """
        [
          {
            "latitude": 50.08,
            "longitude": 14.44,
            "daily": {
              "time": ["2024-06-01", "2024-06-02", "2024-06-03"],
              "temperature_2m_max": [28.5, 25.0, 30.2],
              "temperature_2m_min": [15.0, 12.0, 17.0],
              "weather_code": [0, 3, 1]
            }
          },
          {
            "latitude": 49.20,
            "longitude": 16.61,
            "daily": {
              "time": ["2024-06-01", "2024-06-02", "2024-06-03"],
              "temperature_2m_max": [27.0, 26.5, 29.8],
              "temperature_2m_min": [14.0, 11.0, 16.0],
              "weather_code": [1, 2, 3]
            }
          }
        ]
        """;

    private static readonly WeatherForecastOptions TwoCityOptions = new()
    {
        CacheDurationMinutes = 5,
        RequestTimeoutSeconds = 5,
        Cities = new List<WeatherCity>
        {
            new() { Name = "Praha", Latitude = 50.0755, Longitude = 14.4378 },
            new() { Name = "Brno", Latitude = 49.1951, Longitude = 16.6068 },
        },
    };

    private OpenMeteoWeatherForecastClient CreateClient(
        Mock<HttpMessageHandler> handlerMock,
        WeatherForecastOptions? options = null,
        IMemoryCache? cache = null)
    {
        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://api.open-meteo.com"),
        };
        var opts = Options.Create(options ?? TwoCityOptions);
        var memCache = cache ?? new MemoryCache(new MemoryCacheOptions());
        return new OpenMeteoWeatherForecastClient(httpClient, opts, memCache, NullLogger<OpenMeteoWeatherForecastClient>.Instance);
    }

    private static Mock<HttpMessageHandler> SetupOkHandler(string json)
    {
        var mock = new Mock<HttpMessageHandler>();
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        return mock;
    }

    [Fact]
    public async Task GetForecastAsync_ParsesTwoCitiesAndThreeDays()
    {
        var handler = SetupOkHandler(TwoCityThreeDayJson);
        var client = CreateClient(handler);

        var result = await client.GetForecastAsync(CancellationToken.None);

        result.Should().HaveCount(2);

        var praha = result.First(c => c.CityName == "Praha");
        praha.Days.Should().HaveCount(3);
        praha.Days[0].Date.Should().Be(new DateOnly(2024, 6, 1));
        praha.Days[0].MaxTemperatureCelsius.Should().BeApproximately(28.5, 0.01);
        praha.Days[0].MinTemperatureCelsius.Should().BeApproximately(15.0, 0.01);
        praha.Days[0].WeatherCode.Should().Be(0);
        praha.Days[2].MaxTemperatureCelsius.Should().BeApproximately(30.2, 0.01);

        var brno = result.First(c => c.CityName == "Brno");
        brno.Days[1].MaxTemperatureCelsius.Should().BeApproximately(26.5, 0.01);
    }

    [Fact]
    public async Task GetForecastAsync_SecondCallUsesCache_HttpCalledOnce()
    {
        var handler = SetupOkHandler(TwoCityThreeDayJson);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var client = CreateClient(handler, cache: cache);

        await client.GetForecastAsync(CancellationToken.None);
        await client.GetForecastAsync(CancellationToken.None);

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetForecastAsync_HttpError_ThrowsException()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.ServiceUnavailable });
        var client = CreateClient(handler);

        await client.Invoking(c => c.GetForecastAsync(CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetForecastAsync_NullResponseBody_ThrowsInvalidOperationException()
    {
        var handler = SetupOkHandler("null");
        var client = CreateClient(handler);

        await client.Invoking(c => c.GetForecastAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*null*");
    }

    [Fact]
    public async Task GetForecastAsync_LocationCountMismatch_ThrowsInvalidOperationException()
    {
        var singleCityJson = """
            [
              {
                "latitude": 50.08,
                "longitude": 14.44,
                "daily": {
                  "time": ["2024-06-01"],
                  "temperature_2m_max": [28.5],
                  "weather_code": [0]
                }
              }
            ]
            """;
        var handler = SetupOkHandler(singleCityJson);
        var client = CreateClient(handler);

        await client.Invoking(c => c.GetForecastAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*1 locations*2*");
    }

    [Fact]
    public async Task GetForecastAsync_InconsistentDailyArrayLengths_ThrowsInvalidOperationException()
    {
        var inconsistentJson = """
            [
              {
                "latitude": 50.08,
                "longitude": 14.44,
                "daily": {
                  "time": ["2024-06-01", "2024-06-02"],
                  "temperature_2m_max": [28.5],
                  "temperature_2m_min": [15.0, 12.0],
                  "weather_code": [0, 3]
                }
              },
              {
                "latitude": 49.20,
                "longitude": 16.61,
                "daily": {
                  "time": ["2024-06-01", "2024-06-02"],
                  "temperature_2m_max": [27.0, 26.5],
                  "temperature_2m_min": [14.0, 11.0],
                  "weather_code": [1, 2]
                }
              }
            ]
            """;
        var handler = SetupOkHandler(inconsistentJson);
        var client = CreateClient(handler);

        await client.Invoking(c => c.GetForecastAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*inconsistent lengths*");
    }
}
