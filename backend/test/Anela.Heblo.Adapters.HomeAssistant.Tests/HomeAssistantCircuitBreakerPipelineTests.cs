using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.HomeAssistant.Tests;

public class HomeAssistantCircuitBreakerPipelineTests
{
    private static ServiceProvider BuildProvider(HttpMessageHandler handler, bool circuitBreakerEnabled = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HomeAssistant:BaseUrl"] = "http://ha.test:8123",
                ["HomeAssistant:AccessToken"] = "tok",
                ["HomeAssistant:InnerTemperatureEntityId"] = "sensor.inner_temp",
                ["HomeAssistant:InnerHumidityEntityId"] = "sensor.inner_humidity",
                ["HomeAssistant:OuterTemperatureEntityId"] = "sensor.outer_temp",
                ["HomeAssistant:OuterHumidityEntityId"] = "sensor.outer_humidity",
                ["HomeAssistant:RequestTimeoutSeconds"] = "5",
                ["HomeAssistant:RetryCount"] = "2",
                ["HomeAssistant:RetryBaseDelayMilliseconds"] = "10",
                ["HomeAssistant:RetryMaxDelaySeconds"] = "1",
                ["HomeAssistant:StaleSnapshotMaxAgeMinutes"] = "60",
                ["HomeAssistant:LiveSnapshotMaxAgeMinutes"] = "15",
                ["HomeAssistant:ConditionsCacheDurationMinutes"] = "5",
                ["HomeAssistant:CircuitBreakerEnabled"] = circuitBreakerEnabled.ToString(),
                ["HomeAssistant:CircuitBreakerMinimumThroughput"] = "4",
                ["HomeAssistant:CircuitBreakerBreakDurationSeconds"] = "1",
                ["HomeAssistant:CircuitBreakerSamplingDurationSeconds"] = "30",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHomeAssistantAdapter(config);
        services.ConfigureHttpClientDefaults(b =>
            b.ConfigurePrimaryHttpMessageHandler(() => handler));

        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage OkResponse(HttpRequestMessage request)
    {
        var entityId = request.RequestUri!.Segments.Last();
        var state = entityId switch
        {
            "sensor.inner_temp" => "21.5",
            "sensor.inner_humidity" => "55.0",
            "sensor.outer_temp" => "18.2",
            "sensor.outer_humidity" => "72.3",
            _ => "0",
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { state, entity_id = entityId })),
        };
    }

    private static void ExpireCache(ServiceProvider sp) =>
        sp.GetRequiredService<IMemoryCache>().Remove(HomeAssistantConditionsReadingProvider.CacheKey);

    [Fact]
    public async Task CircuitBreaker_PersistentSslFailure_ReturnsUnavailableAndOpensCircuit()
    {
        var handler = new FuncHandler(_ => throw new IOException("SSL EOF"));
        using var sp = BuildProvider(handler);
        var provider = sp.GetRequiredService<HomeAssistantConditionsReadingProvider>();

        var snapshot = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        snapshot.Source.Should().Be(ConditionsReadingSource.Unavailable);
        handler.CallCount.Should().BeGreaterThanOrEqualTo(4);

        // Circuit is now open: a follow-up call must not reach the network.
        var callsAfterFirst = handler.CallCount;
        await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        handler.CallCount.Should().Be(callsAfterFirst);
    }

    [Fact]
    public async Task CircuitBreaker_OpenCircuit_SecondCallMakesNoNetworkCallsAndReturnsQuickly()
    {
        var handler = new FuncHandler(_ => throw new IOException("SSL EOF"));
        using var sp = BuildProvider(handler);
        var provider = sp.GetRequiredService<HomeAssistantConditionsReadingProvider>();

        await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        var callsAfterFirst = handler.CallCount;

        var sw = Stopwatch.StartNew();
        var second = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        sw.Stop();

        second.Source.Should().Be(ConditionsReadingSource.Unavailable);
        handler.CallCount.Should().Be(callsAfterFirst, "open circuit short-circuits before the handler");
        sw.ElapsedMilliseconds.Should().BeLessThan(500);
    }

    [Fact]
    public async Task CircuitBreaker_OpenCircuitWithRecentLiveSnapshot_ReturnsStale()
    {
        var failing = false;
        var handler = new FuncHandler(request =>
        {
            if (Volatile.Read(ref failing))
                throw new IOException("SSL EOF");
            return OkResponse(request);
        });
        using var sp = BuildProvider(handler);
        var provider = sp.GetRequiredService<HomeAssistantConditionsReadingProvider>();

        var live = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        live.Source.Should().Be(ConditionsReadingSource.Live);

        Volatile.Write(ref failing, true);
        ExpireCache(sp);

        var duringFailure = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        duringFailure.Source.Should().Be(ConditionsReadingSource.Stale);

        // Circuit open now; still Stale, without network calls.
        var calls = handler.CallCount;
        var again = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        again.Source.Should().Be(ConditionsReadingSource.Stale);
        handler.CallCount.Should().Be(calls);
    }

    [Fact]
    public async Task CircuitBreaker_AfterBreakDuration_HalfOpenProbeSucceedsAndCircuitCloses()
    {
        var failing = true;
        var handler = new FuncHandler(request =>
        {
            if (Volatile.Read(ref failing))
                throw new IOException("SSL EOF");
            return OkResponse(request);
        });
        using var sp = BuildProvider(handler);
        var provider = sp.GetRequiredService<HomeAssistantConditionsReadingProvider>();

        var first = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        first.Source.Should().Be(ConditionsReadingSource.Unavailable);

        Volatile.Write(ref failing, false);
        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        var recovered = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        recovered.Source.Should().Be(ConditionsReadingSource.Live);

        ExpireCache(sp);
        var closed = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        closed.Source.Should().Be(ConditionsReadingSource.Live);
    }

    [Fact]
    public async Task CircuitBreaker_Disabled_KeepsLegacyRetryBehavior()
    {
        var handler = new FuncHandler(_ => throw new IOException("SSL EOF"));
        using var sp = BuildProvider(handler, circuitBreakerEnabled: false);
        var provider = sp.GetRequiredService<HomeAssistantConditionsReadingProvider>();

        var first = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        first.Source.Should().Be(ConditionsReadingSource.Unavailable);
        handler.CallCount.Should().Be(12, "4 sensors x 3 attempts");

        var second = await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        second.Source.Should().Be(ConditionsReadingSource.Unavailable);
        handler.CallCount.Should().Be(24, "no breaker: every fetch hits the handler on every attempt");
    }

    [Fact]
    public async Task CircuitBreaker_BrokenCircuitException_IsNotRetried()
    {
        var handler = new FuncHandler(_ => throw new IOException("SSL EOF"));
        using var sp = BuildProvider(handler);
        var provider = sp.GetRequiredService<HomeAssistantConditionsReadingProvider>();

        await provider.GetCurrentSnapshotAsync(CancellationToken.None);
        var callsWhenOpen = handler.CallCount;

        for (var i = 0; i < 3; i++)
            await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        handler.CallCount.Should().Be(callsWhenOpen, "BrokenCircuitException must not trigger retries or reach the handler");
    }

    private sealed class FuncHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            try { return Task.FromResult(_handler(request)); }
            catch (Exception ex) { return Task.FromException<HttpResponseMessage>(ex); }
        }
    }
}
