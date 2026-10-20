using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.HomeAssistant.Resilience;

/// <summary>
/// Builds the circuit breaker strategy options for the HomeAssistant HTTP client.
/// Out-of-range settings are clamped rather than rejected so a bad config value
/// can never prevent the adapter from starting.
/// </summary>
internal static class HomeAssistantCircuitBreakerOptionsFactory
{
    public static HttpCircuitBreakerStrategyOptions Create(HomeAssistantSettings settings, ILogger logger)
    {
        var failureRatio = Math.Clamp(settings.CircuitBreakerFailureRatio, 0.01, 1.0);
        var minimumThroughput = Math.Max(2, settings.CircuitBreakerMinimumThroughput);
        var samplingDuration = TimeSpan.FromSeconds(Math.Max(1, settings.CircuitBreakerSamplingDurationSeconds));
        var breakDuration = TimeSpan.FromSeconds(Math.Max(1, settings.CircuitBreakerBreakDurationSeconds));

        return new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = failureRatio,
            MinimumThroughput = minimumThroughput,
            SamplingDuration = samplingDuration,
            BreakDuration = breakDuration,
            ShouldHandle = args => ValueTask.FromResult(
                HomeAssistantTransientErrorPredicate.IsTransient(
                    args.Outcome.Exception,
                    args.Outcome.Result)),
            OnOpened = args =>
            {
                logger.LogWarning(
                    "HomeAssistant circuit breaker opened; skipping calls for {BreakDurationSeconds}s",
                    args.BreakDuration.TotalSeconds);
                return default;
            },
            OnHalfOpened = _ =>
            {
                logger.LogInformation("HomeAssistant circuit breaker half-open; allowing a trial call");
                return default;
            },
            OnClosed = _ =>
            {
                logger.LogInformation("HomeAssistant circuit breaker closed");
                return default;
            },
        };
    }
}
