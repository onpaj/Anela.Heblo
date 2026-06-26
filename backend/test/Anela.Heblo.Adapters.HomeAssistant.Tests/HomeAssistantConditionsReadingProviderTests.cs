using System.Net;
using System.Text.Json;
using Anela.Heblo.Adapters.HomeAssistant;
using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Adapters.HomeAssistant.Telemetry;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Anela.Heblo.Adapters.HomeAssistant.Tests;

public class HomeAssistantConditionsReadingProviderTests
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HomeAssistantSettings _settings;

    public HomeAssistantConditionsReadingProviderTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _settings = new HomeAssistantSettings
        {
            BaseUrl = "http://ha.test:8123",
            AccessToken = "test-token",
            InnerTemperatureEntityId = "sensor.inner_temp",
            InnerHumidityEntityId = "sensor.inner_humidity",
            OuterTemperatureEntityId = "sensor.outer_temp",
            OuterHumidityEntityId = "sensor.outer_humidity",
            RequestTimeoutSeconds = 5,
            ConditionsCacheDurationMinutes = 5,
            StaleSnapshotMaxAgeMinutes = 60,
        };
    }

    private HomeAssistantConditionsReadingProvider CreateProvider(
        IMemoryCache? cache = null,
        HomeAssistantSnapshotCoordinator? coordinator = null,
        HomeAssistantSnapshotMetrics? metrics = null)
    {
        var httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri(_settings.BaseUrl),
            Timeout = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds),
        };
        var options = Options.Create(_settings);
        cache ??= new MemoryCache(new MemoryCacheOptions());
        coordinator ??= new HomeAssistantSnapshotCoordinator();
        metrics ??= new HomeAssistantSnapshotMetrics();
        return new HomeAssistantConditionsReadingProvider(
            httpClient, options, cache, coordinator, metrics,
            NullLogger<HomeAssistantConditionsReadingProvider>.Instance);
    }

    private void SetupSensorResponse(string entityId, string stateValue, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(new { state = stateValue, entity_id = entityId });
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.PathAndQuery.Contains(entityId)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private void SetupSensorFailure(string entityId, HttpStatusCode status)
    {
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.PathAndQuery.Contains(entityId)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent("error"),
            });
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_AllSensorsReturnNumericValues_ReturnsLiveSourceWithAllValues()
    {
        // Arrange
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Live);
        result.InnerTemperature.Should().Be(21.5m);
        result.InnerHumidity.Should().Be(55.0m);
        result.OuterTemperature.Should().Be(18.2m);
        result.OuterHumidity.Should().Be(72.3m);
        result.RecordedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_OneSensorReturns404_ReturnsPartialSourceWithNullForThatSensor()
    {
        // Arrange
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.NotFound);
        SetupSensorResponse("sensor.outer_humidity", "72.3");
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Partial);
        result.InnerTemperature.Should().Be(21.5m);
        result.InnerHumidity.Should().Be(55.0m);
        result.OuterTemperature.Should().BeNull();
        result.OuterHumidity.Should().Be(72.3m);
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_AllSensorsReturn500_ReturnsUnavailableWithAllNulls()
    {
        // Arrange
        SetupSensorFailure("sensor.inner_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.inner_humidity", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_humidity", HttpStatusCode.InternalServerError);
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Unavailable);
        result.InnerTemperature.Should().BeNull();
        result.InnerHumidity.Should().BeNull();
        result.OuterTemperature.Should().BeNull();
        result.OuterHumidity.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_OneSensorReturnsUnavailableState_ThatValueIsNull()
    {
        // Arrange
        SetupSensorResponse("sensor.inner_temp", "unavailable");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Partial);
        result.InnerTemperature.Should().BeNull();
        result.InnerHumidity.Should().Be(55.0m);
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_OneSensorReturnsUnknownState_ThatValueIsNull()
    {
        // Arrange
        SetupSensorResponse("sensor.inner_temp", "unknown");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Partial);
        result.InnerTemperature.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_OneSensorReturnsNonNumericState_ThatValueIsNull()
    {
        // Arrange
        SetupSensorResponse("sensor.inner_temp", "error_text");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Partial);
        result.InnerTemperature.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_401Unauthorized_ReturnsUnavailable()
    {
        // Arrange
        SetupSensorFailure("sensor.inner_temp", HttpStatusCode.Unauthorized);
        SetupSensorFailure("sensor.inner_humidity", HttpStatusCode.Unauthorized);
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.Unauthorized);
        SetupSensorFailure("sensor.outer_humidity", HttpStatusCode.Unauthorized);
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Unavailable);
        result.InnerTemperature.Should().BeNull();
        result.OuterTemperature.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = CreateProvider();

        // Act
        var act = async () => await provider.GetCurrentSnapshotAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_WithinCacheDuration_ReturnsCachedSnapshotWithoutHttpCalls()
    {
        // Arrange
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(cache);

        var firstResult = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Act — second call is within cache window
        var secondResult = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        secondResult.Should().Be(firstResult);
        _handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(4), // only 4 HTTP calls from the first fetch, not 8
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_AfterCacheInvalidation_FetchesFreshData()
    {
        // Arrange
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(cache);

        await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        cache.Remove(HomeAssistantConditionsReadingProvider.CacheKey);

        // Act — second call with no cache entry
        await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert — 8 total HTTP calls (4 per fetch × 2 fetches)
        _handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(8),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_UnavailableLive_WithFreshLkg_ReturnsStaleFromLkg()
    {
        // Arrange — first call succeeds (populates LKG), second call all fail.
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new HomeAssistantSnapshotCoordinator();
        var provider = CreateProvider(cache, coordinator);

        var live = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        live.Source.Should().Be(ConditionsReadingSource.Live);

        // Invalidate live cache and reconfigure all to fail.
        cache.Remove(HomeAssistantConditionsReadingProvider.CacheKey);
        _handlerMock.Reset();
        SetupSensorFailure("sensor.inner_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.inner_humidity", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_humidity", HttpStatusCode.InternalServerError);

        // Act
        var stale = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        stale.Source.Should().Be(ConditionsReadingSource.Stale);
        stale.InnerTemperature.Should().Be(21.5m);
        stale.RecordedAt.Should().Be(live.RecordedAt, "stale snapshot carries the LKG timestamp");
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_UnavailableLive_WithExpiredLkg_ReturnsUnavailable()
    {
        // Arrange — populate LKG manually with an old snapshot.
        var coordinator = new HomeAssistantSnapshotCoordinator();
        coordinator.RecordLive(new ConditionsSnapshot(
            21m, 55m, 18m, 72m,
            DateTime.UtcNow.AddMinutes(-_settings.StaleSnapshotMaxAgeMinutes - 1),
            ConditionsReadingSource.Live));

        SetupSensorFailure("sensor.inner_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.inner_humidity", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_humidity", HttpStatusCode.InternalServerError);

        var provider = CreateProvider(coordinator: coordinator);

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Unavailable);
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_ColdStart_NoCache_AllFail_ReturnsUnavailable()
    {
        SetupSensorFailure("sensor.inner_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.inner_humidity", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_humidity", HttpStatusCode.InternalServerError);

        var provider = CreateProvider();

        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        result.Source.Should().Be(ConditionsReadingSource.Unavailable);
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_PartialLive_DoesNotOverwriteLkg()
    {
        // Arrange — first call all live to populate LKG.
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new HomeAssistantSnapshotCoordinator();
        var provider = CreateProvider(cache, coordinator);

        var live = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        coordinator.LastKnownGoodLive.Should().NotBeNull();

        // Invalidate cache, make one sensor fail (partial result).
        cache.Remove(HomeAssistantConditionsReadingProvider.CacheKey);
        _handlerMock.Reset();
        SetupSensorResponse("sensor.inner_temp", "22.0");
        SetupSensorResponse("sensor.inner_humidity", "56.0");
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.InternalServerError);
        SetupSensorResponse("sensor.outer_humidity", "73.0");

        // Act
        var partial = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        partial.Source.Should().Be(ConditionsReadingSource.Partial);
        coordinator.LastKnownGoodLive.Should().Be(live, "Partial result must not overwrite LKG");
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_RecordsLastObservedOnCoordinator()
    {
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");

        var coordinator = new HomeAssistantSnapshotCoordinator();
        var provider = CreateProvider(coordinator: coordinator);

        var snapshot = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        coordinator.LastObservedSnapshot.Should().Be(snapshot);
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_ConcurrentCallers_ProduceOnlyOneBurstOfHttpCalls()
    {
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new HomeAssistantSnapshotCoordinator();
        var provider = CreateProvider(cache, coordinator);

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetCurrentSnapshotAsync(CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tasks);

        // Assert — only 4 outbound HTTP calls, not 40.
        _handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(4),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_PreCancelledToken_ThrowsBeforeAcquiringGate()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = CreateProvider();

        var act = async () => await provider.GetCurrentSnapshotAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
