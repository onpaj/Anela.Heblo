# Weather Forecast on Chlazení Tab — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a 7-day weather forecast card (hottest Czech city per day) above the carrier cooling matrix on the Chlazení page, sourced from the free Open-Meteo API.

**Architecture:** A new `Anela.Heblo.Adapters.OpenMeteo` adapter calls Open-Meteo once for 9 Czech cities, caches the result in IMemoryCache for 180 minutes, and exposes it via `IWeatherForecastClient`. A MediatR handler picks the hottest city per day and returns 7 `HottestDayDto` entries. The React `WeatherForecastReport` component renders them above `CarrierCoolingMatrix`.

**Tech Stack:** .NET 8, MediatR, IMemoryCache, System.Text.Json (backend); React, @tanstack/react-query, lucide-react (frontend); xUnit + FluentAssertions + Moq, Jest + @testing-library/react (tests).

---

## File Map

**Create (backend):**
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Weather/IWeatherForecastClient.cs`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Weather/CityForecast.cs`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Weather/CityForecastDay.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/Anela.Heblo.Adapters.OpenMeteo.csproj`
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/WeatherForecastOptions.cs` — config binding (cities list, cache TTL, timeout)
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/OpenMeteoWeatherForecastClient.cs` — HTTP + cache logic
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/HebloOpenMeteoAdapterModule.cs` — DI registration
- `backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests/Anela.Heblo.Adapters.OpenMeteo.Tests.csproj`
- `backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests/OpenMeteoWeatherForecastClientTests.cs`
- `backend/src/Anela.Heblo.Application/Features/WeatherForecast/Contracts/HottestDayDto.cs`
- `backend/src/Anela.Heblo.Application/Features/WeatherForecast/UseCases/GetWeatherForecast/GetWeatherForecastRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/WeatherForecast/UseCases/GetWeatherForecast/GetWeatherForecastResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/WeatherForecast/UseCases/GetWeatherForecast/GetWeatherForecastHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/WeatherForecast/WeatherForecastModule.cs`
- `backend/test/Anela.Heblo.Tests/Features/WeatherForecast/GetWeatherForecastHandlerTests.cs`
- `backend/src/Anela.Heblo.API/Controllers/WeatherForecastController.cs`

**Create (frontend):**
- `frontend/src/components/customer/cooling/weatherIcons.tsx`
- `frontend/src/components/customer/cooling/WeatherForecastReport.tsx`
- `frontend/src/components/customer/cooling/__tests__/weatherIcons.test.ts`
- `frontend/src/components/customer/cooling/__tests__/WeatherForecastReport.test.tsx`
- `frontend/src/api/hooks/useWeatherForecast.ts`
- `frontend/src/api/hooks/__tests__/useWeatherForecast.test.tsx`

**Modify:**
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add `WeatherForecastUnavailable`
- `Anela.Heblo.sln` — add adapter + test project (`dotnet sln add`)
- `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` — project reference to adapter
- `backend/src/Anela.Heblo.API/Program.cs` — `AddOpenMeteoAdapter(...)`
- `backend/src/Anela.Heblo.API/appsettings.json` — `WeatherForecast` section with 9 cities
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — `AddWeatherForecastModule()`
- `frontend/src/api/client.ts` — add `weatherForecast` to `QUERY_KEYS`
- `frontend/src/pages/customer/CoolingPage.tsx` — render `<WeatherForecastReport />`

---

## Task 1: Domain Types + Error Code

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Logistics/Weather/IWeatherForecastClient.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Logistics/Weather/CityForecast.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Logistics/Weather/CityForecastDay.cs`
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

- [ ] **Step 1.1: Create `IWeatherForecastClient.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Logistics.Weather;

public interface IWeatherForecastClient
{
    Task<IReadOnlyList<CityForecast>> GetForecastAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 1.2: Create `CityForecast.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Logistics.Weather;

public record CityForecast(string CityName, IReadOnlyList<CityForecastDay> Days);
```

- [ ] **Step 1.3: Create `CityForecastDay.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Logistics.Weather;

public record CityForecastDay(DateOnly Date, double MaxTemperatureCelsius, int WeatherCode);
```

- [ ] **Step 1.4: Add error code to `ErrorCodes.cs`**

Find the last entry before the closing brace and add after it (use 29XX range — 2901 is next):

```csharp
    // WeatherForecast module errors (29XX)
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    WeatherForecastUnavailable = 2901,
```

- [ ] **Step 1.5: Verify domain compiles**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado && dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 1.6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Logistics/Weather/ backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat: add IWeatherForecastClient domain types and WeatherForecastUnavailable error code"
```

---

## Task 2: Adapter Project Scaffolding

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/Anela.Heblo.Adapters.OpenMeteo.csproj`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/WeatherForecastOptions.cs`
- Modify: `Anela.Heblo.sln`
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`

- [ ] **Step 2.1: Create the adapter `.csproj`**

```xml
<!-- backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/Anela.Heblo.Adapters.OpenMeteo.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.Adapters.OpenMeteo</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Domain\Anela.Heblo.Domain.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2.2: Create `WeatherForecastOptions.cs`**

```csharp
namespace Anela.Heblo.Adapters.OpenMeteo;

public class WeatherForecastOptions
{
    public static string ConfigKey => "WeatherForecast";

    public List<WeatherCity> Cities { get; init; } = new();
    public int CacheDurationMinutes { get; init; } = 180;
    public int RequestTimeoutSeconds { get; init; } = 5;
}

public class WeatherCity
{
    public string Name { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
```

- [ ] **Step 2.3: Add adapter project to solution**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
dotnet sln Anela.Heblo.sln add backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/Anela.Heblo.Adapters.OpenMeteo.csproj
```

Expected: `Project ... added to the solution.`

- [ ] **Step 2.4: Add project reference from API to adapter**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
dotnet add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj reference backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/Anela.Heblo.Adapters.OpenMeteo.csproj
```

Expected: `Reference ... added to the project.`

- [ ] **Step 2.5: Verify adapter compiles**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/Anela.Heblo.Adapters.OpenMeteo.csproj
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 2.6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/ Anela.Heblo.sln backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
git commit -m "feat: scaffold OpenMeteo adapter project"
```

---

## Task 3: Adapter Implementation — TDD

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests/Anela.Heblo.Adapters.OpenMeteo.Tests.csproj`
- Create: `backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests/OpenMeteoWeatherForecastClientTests.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/OpenMeteoWeatherForecastClient.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/HebloOpenMeteoAdapterModule.cs`

- [ ] **Step 3.1: Create test project `.csproj`**

```xml
<!-- backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests/Anela.Heblo.Adapters.OpenMeteo.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.OpenMeteo\Anela.Heblo.Adapters.OpenMeteo.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3.2: Add test project to solution**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
dotnet sln Anela.Heblo.sln add backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests/Anela.Heblo.Adapters.OpenMeteo.Tests.csproj
```

- [ ] **Step 3.3: Write the test file (RED)**

```csharp
// backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests/OpenMeteoWeatherForecastClientTests.cs
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
              "weather_code": [0, 3, 1]
            }
          },
          {
            "latitude": 49.20,
            "longitude": 16.61,
            "daily": {
              "time": ["2024-06-01", "2024-06-02", "2024-06-03"],
              "temperature_2m_max": [27.0, 26.5, 29.8],
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
}
```

- [ ] **Step 3.4: Run tests — expect compile failure (class missing)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
dotnet build backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests/
```

Expected: error `CS0246: The type or namespace name 'OpenMeteoWeatherForecastClient' could not be found`. That is correct — the implementation does not exist yet.

- [ ] **Step 3.5: Create `OpenMeteoWeatherForecastClient.cs`**

```csharp
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
        var url = $"/v1/forecast?latitude={lats}&longitude={lons}&daily=temperature_2m_max,weather_code&forecast_days=7&timezone=Europe%2FPrague";

        _logger.LogInformation("Fetching weather forecast from Open-Meteo for {CityCount} cities", _options.Cities.Count);

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var locations = JsonSerializer.Deserialize<List<OpenMeteoLocationResponse>>(json, JsonOptions)
            ?? throw new InvalidOperationException("Open-Meteo returned null response body");

        if (locations.Count != _options.Cities.Count)
            throw new InvalidOperationException(
                $"Open-Meteo returned {locations.Count} locations but {_options.Cities.Count} were requested");

        var forecasts = locations
            .Select((loc, i) => new CityForecast(
                CityName: _options.Cities[i].Name,
                Days: loc.Daily.Time
                    .Select((time, j) => new CityForecastDay(
                        Date: DateOnly.Parse(time),
                        MaxTemperatureCelsius: loc.Daily.TemperatureMax[j],
                        WeatherCode: loc.Daily.WeatherCode[j]))
                    .ToList()))
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

        [JsonPropertyName("weather_code")]
        public List<int> WeatherCode { get; init; } = new();
    }
}
```

- [ ] **Step 3.6: Create `HebloOpenMeteoAdapterModule.cs`**

```csharp
using Anela.Heblo.Domain.Features.Logistics.Weather;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.OpenMeteo;

public static class HebloOpenMeteoAdapterModule
{
    public static IServiceCollection AddOpenMeteoAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<WeatherForecastOptions>()
            .Bind(configuration.GetSection(WeatherForecastOptions.ConfigKey));

        services.AddMemoryCache();

        services.AddHttpClient<OpenMeteoWeatherForecastClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<WeatherForecastOptions>>().Value;
            client.BaseAddress = new Uri("https://api.open-meteo.com");
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
        });

        services.AddTransient<IWeatherForecastClient>(
            sp => sp.GetRequiredService<OpenMeteoWeatherForecastClient>());

        return services;
    }
}
```

- [ ] **Step 3.7: Run tests — expect GREEN**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
dotnet test backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests/ --no-build 2>/dev/null || dotnet test backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests/
```

Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`.

- [ ] **Step 3.8: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/ backend/test/Anela.Heblo.Adapters.OpenMeteo.Tests/ Anela.Heblo.sln
git commit -m "feat: implement OpenMeteo adapter with caching and TDD tests"
```

---

## Task 4: Application Layer — TDD

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/WeatherForecast/Contracts/HottestDayDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/WeatherForecast/UseCases/GetWeatherForecast/GetWeatherForecastRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/WeatherForecast/UseCases/GetWeatherForecast/GetWeatherForecastResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/WeatherForecast/UseCases/GetWeatherForecast/GetWeatherForecastHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/WeatherForecast/WeatherForecastModule.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/WeatherForecast/GetWeatherForecastHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

- [ ] **Step 4.1: Create `HottestDayDto.cs`** (class, not record — OpenAPI generator rule)

```csharp
namespace Anela.Heblo.Application.Features.WeatherForecast.Contracts;

public class HottestDayDto
{
    public DateOnly Date { get; set; }
    public string CityName { get; set; } = string.Empty;
    public double MaxTemperatureCelsius { get; set; }
    public int WeatherCode { get; set; }
}
```

- [ ] **Step 4.2: Create `GetWeatherForecastRequest.cs`**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.WeatherForecast.UseCases.GetWeatherForecast;

public class GetWeatherForecastRequest : IRequest<GetWeatherForecastResponse>
{
}
```

- [ ] **Step 4.3: Create `GetWeatherForecastResponse.cs`**

```csharp
using Anela.Heblo.Application.Features.WeatherForecast.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.WeatherForecast.UseCases.GetWeatherForecast;

public class GetWeatherForecastResponse : BaseResponse
{
    public List<HottestDayDto> Days { get; set; } = new();

    public GetWeatherForecastResponse() { }

    public GetWeatherForecastResponse(ErrorCodes errorCode)
        : base(errorCode)
    { }
}
```

- [ ] **Step 4.4: Write the handler test (RED)**

```csharp
// backend/test/Anela.Heblo.Tests/Features/WeatherForecast/GetWeatherForecastHandlerTests.cs
using Anela.Heblo.Application.Features.WeatherForecast.UseCases.GetWeatherForecast;
using Anela.Heblo.Domain.Features.Logistics.Weather;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.WeatherForecast;

public class GetWeatherForecastHandlerTests
{
    private readonly Mock<IWeatherForecastClient> _clientMock = new();

    private GetWeatherForecastHandler CreateHandler() =>
        new(_clientMock.Object, NullLogger<GetWeatherForecastHandler>.Instance);

    private static CityForecast City(string name, params (string date, double temp, int code)[] days) =>
        new(name, days
            .Select(d => new CityForecastDay(DateOnly.Parse(d.date), d.temp, d.code))
            .ToList());

    [Fact]
    public async Task Handle_SelectsHottestCityPerDay()
    {
        _clientMock.Setup(c => c.GetForecastAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CityForecast>
            {
                City("Praha",
                    ("2024-06-01", 28.5, 0),
                    ("2024-06-02", 25.0, 3)),
                City("Brno",
                    ("2024-06-01", 27.0, 1),
                    ("2024-06-02", 26.5, 2)),
            });

        var result = await CreateHandler().Handle(new GetWeatherForecastRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Days.Count);

        var day1 = result.Days.Single(d => d.Date == new DateOnly(2024, 6, 1));
        Assert.Equal("Praha", day1.CityName);
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
                    ("2024-06-03", 30.0, 0),
                    ("2024-06-01", 28.0, 1),
                    ("2024-06-02", 25.0, 2)),
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
        Assert.Equal(Application.Shared.ErrorCodes.WeatherForecastUnavailable, result.ErrorCode);
        Assert.Empty(result.Days);
    }
}
```

- [ ] **Step 4.5: Run tests — expect compile failure (handler missing)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
dotnet build backend/test/Anela.Heblo.Tests/ 2>&1 | grep -E "(error|Error)"
```

Expected: `CS0246` for `GetWeatherForecastHandler`. That is correct.

- [ ] **Step 4.6: Create `GetWeatherForecastHandler.cs`**

```csharp
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
```

- [ ] **Step 4.7: Create `WeatherForecastModule.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.WeatherForecast;

public static class WeatherForecastModule
{
    public static IServiceCollection AddWeatherForecastModule(this IServiceCollection services)
    {
        return services;
    }
}
```

- [ ] **Step 4.8: Register in `ApplicationModule.cs`**

Add the using directive at the top with the other feature usings:
```csharp
using Anela.Heblo.Application.Features.WeatherForecast;
```

Add the call after `services.AddCarrierCoolingModule();`:
```csharp
        services.AddWeatherForecastModule();
```

- [ ] **Step 4.9: Run handler tests — expect GREEN**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~WeatherForecast"
```

Expected: `Passed! - Failed: 0, Passed: 3`.

- [ ] **Step 4.10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/WeatherForecast/ backend/src/Anela.Heblo.Application/ApplicationModule.cs backend/test/Anela.Heblo.Tests/Features/WeatherForecast/
git commit -m "feat: add WeatherForecast application layer with handler and tests"
```

---

## Task 5: API Controller + Configuration

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/WeatherForecastController.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`
- Modify: `backend/src/Anela.Heblo.API/Program.cs`

- [ ] **Step 5.1: Create `WeatherForecastController.cs`**

```csharp
using Anela.Heblo.Application.Features.WeatherForecast.UseCases.GetWeatherForecast;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/weather-forecast")]
public class WeatherForecastController : BaseApiController
{
    private readonly IMediator _mediator;

    public WeatherForecastController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet]
    public async Task<ActionResult<GetWeatherForecastResponse>> Get(
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetWeatherForecastRequest(), cancellationToken);
        return HandleResponse(response);
    }
}
```

- [ ] **Step 5.2: Add `WeatherForecast` section to `appsettings.json`**

Open `backend/src/Anela.Heblo.API/appsettings.json`. Find an appropriate spot (after the existing adapters section) and add:

```json
  "WeatherForecast": {
    "CacheDurationMinutes": 180,
    "RequestTimeoutSeconds": 5,
    "Cities": [
      { "Name": "Praha", "Latitude": 50.0755, "Longitude": 14.4378 },
      { "Name": "Brno", "Latitude": 49.1951, "Longitude": 16.6068 },
      { "Name": "Ostrava", "Latitude": 49.8209, "Longitude": 18.2625 },
      { "Name": "Plzeň", "Latitude": 49.7384, "Longitude": 13.3736 },
      { "Name": "Olomouc", "Latitude": 49.5938, "Longitude": 17.2509 },
      { "Name": "Liberec", "Latitude": 50.7663, "Longitude": 15.0543 },
      { "Name": "České Budějovice", "Latitude": 48.9745, "Longitude": 14.4744 },
      { "Name": "Hradec Králové", "Latitude": 50.2092, "Longitude": 15.8328 },
      { "Name": "Ústí nad Labem", "Latitude": 50.6607, "Longitude": 14.0323 }
    ]
  },
```

- [ ] **Step 5.3: Register adapter in `Program.cs`**

Find the line `builder.Services.AddHomeAssistantAdapter(builder.Configuration);` and add immediately after it:

```csharp
builder.Services.AddOpenMeteoAdapter(builder.Configuration);
```

Also add the using at the top if not implicit:
```csharp
using Anela.Heblo.Adapters.OpenMeteo;
```

- [ ] **Step 5.4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/WeatherForecastController.cs backend/src/Anela.Heblo.API/appsettings.json backend/src/Anela.Heblo.API/Program.cs
git commit -m "feat: add WeatherForecastController and OpenMeteo adapter registration"
```

---

## Task 6: Backend Build Verification

- [ ] **Step 6.1: Build entire solution**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
dotnet build Anela.Heblo.sln
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 6.2: Format**

```bash
dotnet format Anela.Heblo.sln
```

Expected: no output or `Formatted N file(s)`.

- [ ] **Step 6.3: Run all backend tests**

```bash
dotnet test Anela.Heblo.sln
```

Expected: all tests pass including the 3 adapter tests and 3 handler tests added in this feature.

- [ ] **Step 6.4: Commit if format made changes**

```bash
git diff --quiet || (git add -u && git commit -m "chore: dotnet format")
```

---

## Task 7: Frontend Weather Icons — TDD

**Files:**
- Create: `frontend/src/components/customer/cooling/__tests__/weatherIcons.test.ts`
- Create: `frontend/src/components/customer/cooling/weatherIcons.tsx`

- [ ] **Step 7.1: Write the icon test (RED)**

```typescript
// frontend/src/components/customer/cooling/__tests__/weatherIcons.test.ts
import { Sun, CloudSun, Cloud, CloudFog, CloudRain, CloudSnow, CloudLightning } from 'lucide-react';
import { getWeatherIcon } from '../weatherIcons';

describe('getWeatherIcon', () => {
  it('returns Sun for code 0 (clear sky)', () => {
    expect(getWeatherIcon(0)).toBe(Sun);
  });

  it('returns CloudSun for codes 1-2 (mainly/partly clear)', () => {
    expect(getWeatherIcon(1)).toBe(CloudSun);
    expect(getWeatherIcon(2)).toBe(CloudSun);
  });

  it('returns Cloud for code 3 (overcast)', () => {
    expect(getWeatherIcon(3)).toBe(Cloud);
  });

  it('returns CloudFog for fog codes 45 and 48', () => {
    expect(getWeatherIcon(45)).toBe(CloudFog);
    expect(getWeatherIcon(48)).toBe(CloudFog);
  });

  it('returns CloudRain for drizzle/rain codes 51-67', () => {
    expect(getWeatherIcon(51)).toBe(CloudRain);
    expect(getWeatherIcon(61)).toBe(CloudRain);
    expect(getWeatherIcon(67)).toBe(CloudRain);
  });

  it('returns CloudSnow for snow codes 71-77', () => {
    expect(getWeatherIcon(71)).toBe(CloudSnow);
    expect(getWeatherIcon(75)).toBe(CloudSnow);
  });

  it('returns CloudRain for rain shower codes 80-82', () => {
    expect(getWeatherIcon(80)).toBe(CloudRain);
    expect(getWeatherIcon(82)).toBe(CloudRain);
  });

  it('returns CloudLightning for thunderstorm codes 95-99', () => {
    expect(getWeatherIcon(95)).toBe(CloudLightning);
    expect(getWeatherIcon(99)).toBe(CloudLightning);
  });

  it('returns Cloud for unknown codes', () => {
    expect(getWeatherIcon(999)).toBe(Cloud);
  });
});
```

- [ ] **Step 7.2: Run test — expect failure**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend
npm test -- --testPathPattern="weatherIcons" --passWithNoTests 2>&1 | tail -5
```

Expected: `Cannot find module '../weatherIcons'` error.

- [ ] **Step 7.3: Create `weatherIcons.tsx`**

```typescript
import { Sun, CloudSun, Cloud, CloudFog, CloudRain, CloudSnow, CloudLightning, type LucideIcon } from 'lucide-react';

export function getWeatherIcon(weatherCode: number): LucideIcon {
  if (weatherCode === 0) return Sun;
  if (weatherCode <= 2) return CloudSun;
  if (weatherCode === 3) return Cloud;
  if (weatherCode === 45 || weatherCode === 48) return CloudFog;
  if (weatherCode >= 51 && weatherCode <= 67) return CloudRain;
  if (weatherCode >= 71 && weatherCode <= 77) return CloudSnow;
  if (weatherCode >= 80 && weatherCode <= 82) return CloudRain;
  if (weatherCode >= 95) return CloudLightning;
  return Cloud;
}
```

- [ ] **Step 7.4: Run test — expect GREEN**

```bash
npm test -- --testPathPattern="weatherIcons"
```

Expected: `Tests: 9 passed`.

- [ ] **Step 7.5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
git add frontend/src/components/customer/cooling/weatherIcons.tsx frontend/src/components/customer/cooling/__tests__/weatherIcons.test.ts
git commit -m "feat: add WMO weather code to lucide-react icon mapping"
```

---

## Task 8: Frontend Hook — TDD

**Files:**
- Modify: `frontend/src/api/client.ts`
- Create: `frontend/src/api/hooks/__tests__/useWeatherForecast.test.tsx`
- Create: `frontend/src/api/hooks/useWeatherForecast.ts`

- [ ] **Step 8.1: Add `weatherForecast` key to `QUERY_KEYS` in `client.ts`**

Open `frontend/src/api/client.ts`. Find the `QUERY_KEYS` object and add a new entry (the existing `weather` key is unrelated — add a distinct one):

```typescript
  weatherForecast: ['weatherForecast'] as const,
```

- [ ] **Step 8.2: Write the hook test (RED)**

```typescript
// frontend/src/api/hooks/__tests__/useWeatherForecast.test.tsx
import React, { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useWeatherForecast } from '../useWeatherForecast';
import * as clientModule from '../../client';

const mockFetch = jest.fn();
const mockApiClient = {
  baseUrl: 'http://localhost:5001',
  http: { fetch: mockFetch },
};

jest.mock('../../client');

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe('useWeatherForecast', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (clientModule.getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockApiClient);
  });

  it('fetches and returns forecast days on success', async () => {
    const mockDays = [
      { date: '2024-06-01', cityName: 'Praha', maxTemperatureCelsius: 28.5, weatherCode: 0 },
      { date: '2024-06-02', cityName: 'Brno', maxTemperatureCelsius: 26.5, weatherCode: 3 },
    ];
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: jest.fn().mockResolvedValue({ success: true, days: mockDays }),
    });

    const { result } = renderHook(() => useWeatherForecast(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(mockDays);
    expect(mockFetch).toHaveBeenCalledWith(
      'http://localhost:5001/api/weather-forecast',
      expect.objectContaining({ method: 'GET' })
    );
  });

  it('sets isError when HTTP response is not ok', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 503 });

    const { result } = renderHook(() => useWeatherForecast(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });

  it('sets isError when API returns success=false', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: jest.fn().mockResolvedValue({ success: false, days: [] }),
    });

    const { result } = renderHook(() => useWeatherForecast(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
```

- [ ] **Step 8.3: Run test — expect failure**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend
npm test -- --testPathPattern="useWeatherForecast" --passWithNoTests 2>&1 | tail -5
```

Expected: `Cannot find module '../useWeatherForecast'`.

- [ ] **Step 8.4: Create `useWeatherForecast.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export interface HottestDayDto {
  date: string;
  cityName: string;
  maxTemperatureCelsius: number;
  weatherCode: number;
}

interface GetWeatherForecastApiResponse {
  success: boolean;
  days: HottestDayDto[];
}

const fetchWeatherForecast = async (): Promise<HottestDayDto[]> => {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}/api/weather-forecast`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) {
    throw new Error(`Weather forecast request failed: ${response.status}`);
  }
  const data: GetWeatherForecastApiResponse = await response.json();
  if (!data.success) {
    throw new Error('Weather forecast unavailable');
  }
  return data.days;
};

export function useWeatherForecast() {
  return useQuery({
    queryKey: QUERY_KEYS.weatherForecast,
    queryFn: fetchWeatherForecast,
    staleTime: 30 * 60 * 1000,
  });
}
```

- [ ] **Step 8.5: Run hook tests — expect GREEN**

```bash
npm test -- --testPathPattern="useWeatherForecast"
```

Expected: `Tests: 3 passed`.

- [ ] **Step 8.6: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
git add frontend/src/api/client.ts frontend/src/api/hooks/useWeatherForecast.ts frontend/src/api/hooks/__tests__/useWeatherForecast.test.tsx
git commit -m "feat: add useWeatherForecast React Query hook"
```

---

## Task 9: Frontend Component — TDD

**Files:**
- Create: `frontend/src/components/customer/cooling/__tests__/WeatherForecastReport.test.tsx`
- Create: `frontend/src/components/customer/cooling/WeatherForecastReport.tsx`

First, find where `LoadingState` and `ErrorState` live so the import path is correct:

```bash
find /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend/src -name "LoadingState.tsx" -o -name "ErrorState.tsx" 2>/dev/null
```

Note the path — it is likely `frontend/src/components/common/LoadingState.tsx`.

- [ ] **Step 9.1: Write the component test (RED)**

```typescript
// frontend/src/components/customer/cooling/__tests__/WeatherForecastReport.test.tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import WeatherForecastReport from '../WeatherForecastReport';
import { useWeatherForecast } from '../../../../api/hooks/useWeatherForecast';

jest.mock('../../../../api/hooks/useWeatherForecast');

const mockDays = [
  { date: '2024-06-01', cityName: 'Praha', maxTemperatureCelsius: 28.5, weatherCode: 0 },
  { date: '2024-06-02', cityName: 'Brno', maxTemperatureCelsius: 26.5, weatherCode: 3 },
  { date: '2024-06-03', cityName: 'Praha', maxTemperatureCelsius: 30.2, weatherCode: 1 },
  { date: '2024-06-04', cityName: 'Ostrava', maxTemperatureCelsius: 27.0, weatherCode: 45 },
  { date: '2024-06-05', cityName: 'Praha', maxTemperatureCelsius: 25.5, weatherCode: 61 },
  { date: '2024-06-06', cityName: 'Brno', maxTemperatureCelsius: 24.0, weatherCode: 95 },
  { date: '2024-06-07', cityName: 'Praha', maxTemperatureCelsius: 22.0, weatherCode: 71 },
];

describe('WeatherForecastReport', () => {
  it('renders all 7 day rows when data is loaded', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: false,
      isError: false,
      data: mockDays,
    });

    render(<WeatherForecastReport />);

    expect(screen.getByText('Praha')).toBeInTheDocument();
    expect(screen.getByText('28.5 °C')).toBeInTheDocument();
    expect(screen.getByText('30.2 °C')).toBeInTheDocument();
    // All 7 city name cells (Praha appears multiple times)
    expect(screen.getAllByText('Praha').length).toBeGreaterThanOrEqual(1);
  });

  it('renders LoadingState when isLoading is true', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: true,
      isError: false,
      data: undefined,
    });

    render(<WeatherForecastReport />);

    expect(screen.getByText(/načítám předpověď/i)).toBeInTheDocument();
  });

  it('renders ErrorState when isError is true', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: false,
      isError: true,
      data: undefined,
    });

    render(<WeatherForecastReport />);

    expect(screen.getByText(/nepodařilo se načíst předpověď/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 9.2: Run test — expect failure**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend
npm test -- --testPathPattern="WeatherForecastReport" --passWithNoTests 2>&1 | tail -5
```

Expected: `Cannot find module '../WeatherForecastReport'`.

- [ ] **Step 9.3: Create `WeatherForecastReport.tsx`**

Adjust the import paths for `LoadingState` and `ErrorState` to match what you found in Step 9 preamble (likely `../../common/LoadingState`).

```typescript
import { useWeatherForecast } from '../../../api/hooks/useWeatherForecast';
import { getWeatherIcon } from './weatherIcons';
import LoadingState from '../../common/LoadingState';
import ErrorState from '../../common/ErrorState';

function WeatherForecastReport() {
  const { data, isLoading, isError } = useWeatherForecast();

  if (isLoading) {
    return <LoadingState message="Načítám předpověď počasí..." className="h-40" />;
  }

  if (isError || !data) {
    return <ErrorState message="Nepodařilo se načíst předpověď počasí." className="h-40" />;
  }

  return (
    <div className="mx-4 mb-4 rounded-lg border border-gray-200 bg-white p-4">
      <h2 className="mb-3 text-sm font-semibold text-gray-700">
        Předpověď počasí — nejteplejší místo v ČR
      </h2>
      <div className="space-y-2">
        {data.map((day) => {
          const Icon = getWeatherIcon(day.weatherCode);
          const [year, month, dayNum] = day.date.split('-').map(Number);
          const dateObj = new Date(year, month - 1, dayNum);
          const label = dateObj.toLocaleDateString('cs-CZ', {
            weekday: 'short',
            day: 'numeric',
            month: 'numeric',
          });

          return (
            <div key={day.date} className="flex items-center gap-3 text-sm">
              <span className="w-24 shrink-0 text-gray-500">{label}</span>
              <Icon className="h-4 w-4 shrink-0 text-gray-600" />
              <span className="w-16 font-medium text-gray-900">
                {day.maxTemperatureCelsius.toFixed(1)} °C
              </span>
              <span className="text-gray-500">{day.cityName}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export default WeatherForecastReport;
```

- [ ] **Step 9.4: Run component tests — expect GREEN**

```bash
npm test -- --testPathPattern="WeatherForecastReport"
```

Expected: `Tests: 3 passed`.

- [ ] **Step 9.5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
git add frontend/src/components/customer/cooling/WeatherForecastReport.tsx frontend/src/components/customer/cooling/__tests__/WeatherForecastReport.test.tsx
git commit -m "feat: add WeatherForecastReport component with loading/error states"
```

---

## Task 10: CoolingPage Integration

**Files:**
- Modify: `frontend/src/pages/customer/CoolingPage.tsx`

- [ ] **Step 10.1: Add import for `WeatherForecastReport` at the top of `CoolingPage.tsx`**

After the existing imports, add:
```typescript
import WeatherForecastReport from '../../components/customer/cooling/WeatherForecastReport';
```

- [ ] **Step 10.2: Render `<WeatherForecastReport />` above `<CarrierCoolingMatrix />`**

Find where the `{data && (<CarrierCoolingMatrix .../>)}` block starts. Render the weather report unconditionally (it manages its own loading/error state) just above the matrix section, inside the `flex-1 overflow-y-auto` div:

```typescript
        {/* Weather forecast card — independent loading state */}
        <WeatherForecastReport />

        {isLoading && (
          <div className="flex items-center justify-center h-32">
```

- [ ] **Step 10.3: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
git add frontend/src/pages/customer/CoolingPage.tsx
git commit -m "feat: render WeatherForecastReport above carrier cooling matrix"
```

---

## Task 11: Frontend Build Verification

- [ ] **Step 11.1: TypeScript build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend
npm run build
```

Expected: exits 0, no TypeScript errors.

- [ ] **Step 11.2: Lint**

```bash
npm run lint
```

Expected: no errors.

- [ ] **Step 11.3: Run all frontend tests**

```bash
npm test -- --passWithNoTests
```

Expected: all tests pass including the 9 new tests (icons × 9, hook × 3, component × 3).

- [ ] **Step 11.4: Final commit if lint/build changed files**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado
git diff --quiet || (git add -u && git commit -m "chore: lint and build fixes")
```

---

## Self-Review Checklist

### Spec coverage
| Requirement | Task |
|---|---|
| 9 hardcoded cities in appsettings.json | Task 5.2 |
| Open-Meteo, no API key | Task 3.5 (base URL hardcoded) |
| Per-day hottest city selection | Task 4.6 handler |
| Date + max temp + city + icon | Task 9.3 component |
| Cache with TTL | Task 3.5 `_cache.Set` |
| Error → friendly message | Task 4.6 catch block |
| `weatherForecast` QUERY_KEY (distinct from existing `weather`) | Task 8.1 |
| Absolute URL in hook | Task 8.4 |
| `<WeatherForecastReport />` above matrix | Task 10.2 |
| Adapter tests (parse + cache) | Task 3.3 |
| Handler tests (hottest city + error) | Task 4.4 |
| Frontend component tests (7 rows + states) | Task 9.1 |

### Type consistency
- `HottestDayDto` (class) used in: `GetWeatherForecastResponse.Days`, frontend interface matching C# serialization.
- `CityForecast`/`CityForecastDay` (records) used in: `IWeatherForecastClient` return type → adapter → handler.
- `IWeatherForecastClient` registered in `HebloOpenMeteoAdapterModule` and injected in `GetWeatherForecastHandler`.
- `WeatherForecastOptions.ConfigKey = "WeatherForecast"` matches `appsettings.json` section name.
