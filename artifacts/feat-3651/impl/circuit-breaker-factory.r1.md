# Implementation: circuit-breaker-factory

## What was implemented
`HomeAssistantCircuitBreakerOptionsFactory.Create(settings, logger)` returning `HttpCircuitBreakerStrategyOptions` with clamped values (ratio 0.01..1, throughput >= 2, durations >= 1s), `ShouldHandle` delegating to `HomeAssistantTransientErrorPredicate.IsTransient`, and OnOpened (Warning, break duration seconds) / OnHalfOpened / OnClosed (Information) callbacks returning `default`. No exception objects are logged.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Resilience/HomeAssistantCircuitBreakerOptionsFactory.cs` (new)
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj` (InternalsVisibleTo for the test assembly; none existed)
- `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantCircuitBreakerOptionsFactoryTests.cs` (new, 4 tests)

## Tests
4 tests pass: settings mapping, clamping without throwing, ShouldHandle true for IOException/SocketException/HttpRequestException/500, false for BrokenCircuitException/200/caller-cancelled OCE.

## Status
DONE
