# Design: HomeAssistant HTTP circuit breaker

## Component Design
- `HomeAssistantSettings` gains: `CircuitBreakerEnabled` (bool, true), `CircuitBreakerFailureRatio` (double, 0.5), `CircuitBreakerMinimumThroughput` (int, 8), `CircuitBreakerSamplingDurationSeconds` (int, 60), `CircuitBreakerBreakDurationSeconds` (int, 60). All `init` properties with XML doc comments like the existing retry ones.
- `Resilience/HomeAssistantCircuitBreakerOptionsFactory` (internal static): `Create(settings, logger)` returns `HttpCircuitBreakerStrategyOptions` with clamped values (ratio 0.01..1, throughput >= 2, durations >= 1s), `ShouldHandle` delegating to `HomeAssistantTransientErrorPredicate.IsTransient`, and `OnOpened` (Warning, includes break duration), `OnHalfOpened` / `OnClosed` (Information) callbacks. Logs use message templates only, never the exception object.
- `AddHomeAssistantAdapter`: inside the `ha-conditions` callback, build chain `AddRetry(...)`, then `if (settings.CircuitBreakerEnabled) builder.AddCircuitBreaker(factory.Create(...))`, then `AddTimeout(...)`. Logger from `context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Anela.Heblo.Adapters.HomeAssistant.CircuitBreaker")`.
- Provider, health check, telemetry filter, tagging handler: unchanged. `BrokenCircuitException` is absorbed by the provider's generic catch.

## Data Schemas
No DB, API, or event changes. Configuration shape (all optional):
```json
"HomeAssistant": {
  "CircuitBreakerEnabled": true,
  "CircuitBreakerFailureRatio": 0.5,
  "CircuitBreakerMinimumThroughput": 8,
  "CircuitBreakerSamplingDurationSeconds": 60,
  "CircuitBreakerBreakDurationSeconds": 60
}
```
