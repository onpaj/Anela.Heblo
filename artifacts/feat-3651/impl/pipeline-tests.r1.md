# Implementation: pipeline-tests

## What was implemented
New `HomeAssistantCircuitBreakerPipelineTests` exercising the real `ha-conditions` resilience pipeline (retry + circuit breaker + timeout) with a fake primary handler. MinimumThroughput=4, BreakDuration=1s, SamplingDuration=30s.

## Files created/modified
- `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantCircuitBreakerPipelineTests.cs`

## Tests
- Persistent SSL IOException -> Unavailable, circuit opens, follow-up call makes no handler calls
- Open circuit second call: zero network calls, < 500 ms
- Prior Live snapshot (cache removed) + open circuit -> Stale
- After break duration, probe succeeds -> Live, circuit closed
- CircuitBreakerEnabled=false -> 12 calls per fetch (24 after two)
- BrokenCircuitException not retried (call count constant while open)

## How to verify
`dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests` (44 pass); `dotnet test backend/test/Anela.Heblo.Tests --filter ManufactureConditionsTile` (8 pass); `dotnet format --verify-no-changes` clean.

## Notes
Process doc update is the next task (update-process-doc).

## PR Summary
Added pipeline-level tests for the Home Assistant circuit breaker.

### Changes
- `HomeAssistantCircuitBreakerPipelineTests.cs` - six tests covering open, short-circuit, stale fallback, recovery, disabled mode, no-retry on broken circuit

## Status
DONE
