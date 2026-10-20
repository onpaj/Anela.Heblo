# Implementation: add-settings

## What was implemented
Added five circuit breaker settings to HomeAssistantSettings with XML docs and defaults.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantSettings.cs` — CircuitBreakerEnabled=true, FailureRatio=0.5, MinimumThroughput=8, SamplingDurationSeconds=60, BreakDurationSeconds=60

## Tests
None required (pure settings).

## How to verify
`dotnet build backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant` -- 0 errors.

## Notes
None.

## PR Summary
Added circuit breaker configuration properties to HomeAssistantSettings.

### Changes
- `HomeAssistantSettings.cs` — five new init properties with defaults

## Status
DONE
