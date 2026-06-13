using System.Net;
using System.Text.Json;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.HomeAssistant.Tests;

public class HomeAssistantRetryPipelineTests
{
    private static IServiceProvider BuildProvider(HttpMessageHandler handler)
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
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHomeAssistantAdapter(config);
        services.ConfigureHttpClientDefaults(b =>
            b.ConfigurePrimaryHttpMessageHandler(() => handler));

        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage Json(string state) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { state, entity_id = "x" })),
        };

    [Fact]
    public async Task Resilience_AllSucceed_ProducesLiveSnapshot()
    {
        HttpResponseMessage Handler(HttpRequestMessage request)
        {
            var entityId = request.RequestUri?.LocalPath.Split('/').Last() ?? "unknown";
            return Json(entityId switch
            {
                "sensor.inner_temp" => "21.5",
                "sensor.inner_humidity" => "55.0",
                "sensor.outer_temp" => "18.2",
                "sensor.outer_humidity" => "72.3",
                _ => "0"
            });
        }

        var handler = new StatefulHandler(Handler);

        using var sp = (ServiceProvider)BuildProvider((HttpMessageHandler)handler);
        var provider = sp.GetRequiredService<HomeAssistantConditionsReadingProvider>();

        var snapshot = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        snapshot.Source.Should().Be(ConditionsReadingSource.Live);
        snapshot.InnerTemperature.Should().Be(21.5m);
        snapshot.InnerHumidity.Should().Be(55.0m);
        snapshot.OuterTemperature.Should().Be(18.2m);
        snapshot.OuterHumidity.Should().Be(72.3m);
        handler.CallCount.Should().Be(4, "each of 4 sensors succeeds on first attempt");
    }

    [Fact]
    public async Task Resilience_RetryPolicyIsActive_AllowsMoreAttempts()
    {
        // Verify Polly retry is active: fail first 2 attempts globally, succeed after
        int callCount = 0;
        var lockObj = new object();

        HttpResponseMessage Handler(HttpRequestMessage request)
        {
            lock (lockObj)
            {
                callCount++;
                // First 2 calls globally fail, then all succeed
                if (callCount <= 2)
                    throw new IOException($"transient attempt #{callCount}");
            }

            var path = request.RequestUri?.LocalPath ?? "/unknown";
            var sensorName = path.Contains("inner_temp") ? "inner_temp"
                : path.Contains("inner_humidity") ? "inner_humidity"
                : path.Contains("outer_temp") ? "outer_temp"
                : path.Contains("outer_humidity") ? "outer_humidity"
                : "unknown";

            return Json(sensorName switch
            {
                "inner_temp" => "21.5",
                "inner_humidity" => "55.0",
                "outer_temp" => "18.2",
                "outer_humidity" => "72.3",
                _ => "0"
            });
        }

        var handler = new StatefulHandler(Handler);

        using var sp = (ServiceProvider)BuildProvider((HttpMessageHandler)handler);
        var provider = sp.GetRequiredService<HomeAssistantConditionsReadingProvider>();

        var snapshot = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // With RetryCount=2 (meaning up to 3 total attempts per sensor),
        // we expect to eventually succeed if only the first 2 global calls fail
        snapshot.Source.Should().Be(ConditionsReadingSource.Live);
        snapshot.InnerTemperature.Should().Be(21.5m);
        snapshot.InnerHumidity.Should().Be(55.0m);
        snapshot.OuterTemperature.Should().Be(18.2m);
        snapshot.OuterHumidity.Should().Be(72.3m);
        // With 4 sensors and up to 3 attempts each, we expect up to 12 calls,
        // but we only fail the first 2, so we should use slightly more than 4 calls
        handler.CallCount.Should().BeGreaterThan(4, "retry policy should make additional attempts");
    }

    private sealed class StatefulHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public int CallCount { get; private set; }

        public StatefulHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            try
            {
                return Task.FromResult(_handler(request));
            }
            catch (Exception ex)
            {
                return Task.FromException<HttpResponseMessage>(ex);
            }
        }
    }

    [Fact]
    public async Task Resilience_ExhaustedRetries_LeadsToUnavailable()
    {
        // All calls fail - 4 sensors × 3 attempts each (RetryCount=2 means 2 retries = 3 total attempts)
        var handler = new StatefulHandler(request =>
            throw new IOException("transient failure"));

        using var sp = (ServiceProvider)BuildProvider((HttpMessageHandler)handler);
        var provider = sp.GetRequiredService<HomeAssistantConditionsReadingProvider>();

        var snapshot = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        snapshot.Source.Should().Be(ConditionsReadingSource.Unavailable);
        handler.CallCount.Should().Be(12, "4 sensors × (RetryCount+1) = 4 × 3 attempts");
    }
}
