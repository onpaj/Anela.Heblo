### task: pipeline-tests

**Files:** Create `HomeAssistantCircuitBreakerPipelineTests.cs` (reuse the `BuildProvider` + handler pattern of `HomeAssistantRetryPipelineTests`; configure `RetryBaseDelayMilliseconds=10`, `CircuitBreakerMinimumThroughput=4` or 8, `CircuitBreakerBreakDurationSeconds=1`, `CircuitBreakerSamplingDurationSeconds=30`).

- [ ] Test: handler always throws `IOException("SSL EOF")`; first `GetCurrentSnapshotAsync` -> Unavailable and circuit opens; record `CallCount`.
- [ ] Test: second call (clear the cache is unnecessary since Unavailable is not cached) with circuit open -> Unavailable, handler `CallCount` unchanged (zero network calls), returns quickly (< 500 ms).
- [ ] Test: with a prior Live snapshot < 60 min (prime via a successful fetch, then expire cache by `ConditionsCacheDurationMinutes=0` or `TimeProvider`/cache removal), open circuit yields `Stale`.
- [ ] Test: after break duration (await ~1.2 s) handler starts succeeding -> half-open probe succeeds, circuit closes, snapshot Live.
- [ ] Test: `CircuitBreakerEnabled=false` keeps legacy behavior (handler called on every attempt on each fetch: 12 calls per fetch for 4 sensors x 3 attempts).
- [ ] Test: `BrokenCircuitException` is not retried (call count does not grow during open state).
- [ ] Run whole test project: `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests`; green. Also run `ManufactureConditionsTileTests` in `Anela.Heblo.Tests`.
- [ ] Commit: `test(ha): circuit breaker pipeline tests`.

