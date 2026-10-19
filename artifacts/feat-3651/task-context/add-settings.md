### task: add-settings

**Files:** Modify `HomeAssistantSettings.cs`.

- [ ] Add `init` properties with XML docs and defaults: `CircuitBreakerEnabled=true`, `CircuitBreakerFailureRatio=0.5` (double), `CircuitBreakerMinimumThroughput=8`, `CircuitBreakerSamplingDurationSeconds=60`, `CircuitBreakerBreakDurationSeconds=60`.
- [ ] `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant` succeeds.
- [ ] Commit: `feat(ha): add circuit breaker settings`.

