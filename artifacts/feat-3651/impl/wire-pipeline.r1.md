# Implementation: wire-pipeline

## What was implemented
In `HomeAssistantAdapterServiceCollectionExtensions`, the `ha-conditions` resilience handler is now statement-based: `AddRetry` (outer), then `AddCircuitBreaker(HomeAssistantCircuitBreakerOptionsFactory.Create(settings, logger))` when `settings.CircuitBreakerEnabled`, then `AddTimeout` (inner). Logger is `ILoggerFactory.CreateLogger("Anela.Heblo.Adapters.HomeAssistant.CircuitBreaker")`. Comment updated to "retry + circuit breaker + per-attempt timeout".

## Files modified
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantAdapterServiceCollectionExtensions.cs`
- `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantRetryPipelineTests.cs` (BuildProvider gets `circuitBreakerEnabled` param; only `Resilience_ExhaustedRetries_LeadsToUnavailable` disables it, because its 12 calls exceed min throughput 8 and trip the breaker)

## Tests
All 38 tests in the HomeAssistant test project pass. `dotnet build` 0 errors; `dotnet format --verify-no-changes` clean.

## Status
DONE
