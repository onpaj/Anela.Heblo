using System.Net;
using System.Net.Sockets;
using Anela.Heblo.Adapters.HomeAssistant.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.CircuitBreaker;

namespace Anela.Heblo.Adapters.HomeAssistant.Tests;

public class HomeAssistantCircuitBreakerOptionsFactoryTests
{
    private static async Task<bool> ShouldHandleAsync(
        Polly.CircuitBreaker.CircuitBreakerStrategyOptions<HttpResponseMessage> options,
        Outcome<HttpResponseMessage> outcome)
    {
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            return await options.ShouldHandle(new CircuitBreakerPredicateArguments<HttpResponseMessage>(context, outcome));
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    [Fact]
    public void Create_MapsValuesFromSettings()
    {
        var settings = new HomeAssistantSettings
        {
            CircuitBreakerFailureRatio = 0.7,
            CircuitBreakerMinimumThroughput = 10,
            CircuitBreakerSamplingDurationSeconds = 30,
            CircuitBreakerBreakDurationSeconds = 45,
        };

        var options = HomeAssistantCircuitBreakerOptionsFactory.Create(settings, NullLogger.Instance);

        options.FailureRatio.Should().Be(0.7);
        options.MinimumThroughput.Should().Be(10);
        options.SamplingDuration.Should().Be(TimeSpan.FromSeconds(30));
        options.BreakDuration.Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Create_ClampsOutOfRangeValuesWithoutThrowing()
    {
        var settings = new HomeAssistantSettings
        {
            CircuitBreakerFailureRatio = 0,
            CircuitBreakerMinimumThroughput = 0,
            CircuitBreakerSamplingDurationSeconds = 0,
            CircuitBreakerBreakDurationSeconds = -5,
        };

        var options = HomeAssistantCircuitBreakerOptionsFactory.Create(settings, NullLogger.Instance);

        options.FailureRatio.Should().Be(0.01);
        options.MinimumThroughput.Should().Be(2);
        options.SamplingDuration.Should().Be(TimeSpan.FromSeconds(1));
        options.BreakDuration.Should().Be(TimeSpan.FromSeconds(1));

        var high = HomeAssistantCircuitBreakerOptionsFactory.Create(
            new HomeAssistantSettings { CircuitBreakerFailureRatio = 5 }, NullLogger.Instance);
        high.FailureRatio.Should().Be(1);
    }

    [Fact]
    public async Task ShouldHandle_TransientFailures_ReturnsTrue()
    {
        var options = HomeAssistantCircuitBreakerOptionsFactory.Create(new HomeAssistantSettings(), NullLogger.Instance);

        (await ShouldHandleAsync(options, Outcome.FromException<HttpResponseMessage>(new IOException()))).Should().BeTrue();
        (await ShouldHandleAsync(options, Outcome.FromException<HttpResponseMessage>(new SocketException()))).Should().BeTrue();
        (await ShouldHandleAsync(options, Outcome.FromException<HttpResponseMessage>(new HttpRequestException()))).Should().BeTrue();
        (await ShouldHandleAsync(options, Outcome.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)))).Should().BeTrue();
    }

    [Fact]
    public async Task ShouldHandle_NonTransient_ReturnsFalse()
    {
        var options = HomeAssistantCircuitBreakerOptionsFactory.Create(new HomeAssistantSettings(), NullLogger.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        (await ShouldHandleAsync(options, Outcome.FromException<HttpResponseMessage>(new BrokenCircuitException()))).Should().BeFalse();
        (await ShouldHandleAsync(options, Outcome.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))).Should().BeFalse();
        (await ShouldHandleAsync(options, Outcome.FromException<HttpResponseMessage>(new OperationCanceledException(cts.Token)))).Should().BeFalse();
    }
}
