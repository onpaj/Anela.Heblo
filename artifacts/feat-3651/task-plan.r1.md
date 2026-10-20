# HomeAssistant Circuit Breaker Implementation Plan

**Goal:** Add a Polly circuit breaker (retry -> breaker -> timeout) to the HomeAssistant HTTP client so a dead Tailscale tunnel fails fast instead of amplifying retries.

**Architecture:** Extend `ha-conditions` resilience pipeline in `AddHomeAssistantAdapter`; options built by an internal factory from new `HomeAssistantSettings` properties; provider's generic catch already degrades `BrokenCircuitException` to Stale/Unavailable.

**Tech Stack:** .NET 8, Microsoft.Extensions.Http.Resilience 8.0.0 (Polly v8), xUnit, FluentAssertions.

Paths: adapter `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/`, tests `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/`.

### task: add-settings

**Files:** Modify `HomeAssistantSettings.cs`.

- [ ] Add `init` properties with XML docs and defaults: `CircuitBreakerEnabled=true`, `CircuitBreakerFailureRatio=0.5` (double), `CircuitBreakerMinimumThroughput=8`, `CircuitBreakerSamplingDurationSeconds=60`, `CircuitBreakerBreakDurationSeconds=60`.
- [ ] `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant` succeeds.
- [ ] Commit: `feat(ha): add circuit breaker settings`.

### task: circuit-breaker-factory

**Files:** Create `Resilience/HomeAssistantCircuitBreakerOptionsFactory.cs`; create test `HomeAssistantCircuitBreakerOptionsFactoryTests.cs`. Check `InternalsVisibleTo` for the test assembly exists (predicate is internal; add to csproj only if missing).

- [ ] Write failing tests first: (1) values map from settings; (2) out-of-range values are clamped (ratio 0.01..1, throughput >= 2, durations >= 1s) and do not throw; (3) `ShouldHandle` returns true for IOException/SocketException/HttpRequestException/500, false for `BrokenCircuitException`, 200 and caller-cancelled `OperationCanceledException`.
- [ ] Implement `internal static HttpCircuitBreakerStrategyOptions Create(HomeAssistantSettings s, ILogger logger)`: `FailureRatio`, `MinimumThroughput`, `SamplingDuration`, `BreakDuration`, `ShouldHandle` via `HomeAssistantTransientErrorPredicate.IsTransient(args.Outcome.Exception, args.Outcome.Result)`, `OnOpened` (LogWarning with break duration seconds), `OnHalfOpened`, `OnClosed` (LogInformation). Never pass exception objects to the logger. Callbacks return `default(ValueTask)`.
- [ ] Run the new tests; pass.
- [ ] Commit: `feat(ha): circuit breaker options factory`.

### task: wire-pipeline

**Files:** Modify `HomeAssistantAdapterServiceCollectionExtensions.cs`.

- [ ] In the `ha-conditions` callback, after `.AddRetry(...)` and before `.AddTimeout(...)`, add `if (settings.CircuitBreakerEnabled)` inserting `AddCircuitBreaker(HomeAssistantCircuitBreakerOptionsFactory.Create(settings, logger))` where logger = `context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Anela.Heblo.Adapters.HomeAssistant.CircuitBreaker")`. Restructure the fluent chain into statements (`builder.AddRetry(...); if (...) builder.AddCircuitBreaker(...); builder.AddTimeout(...);`). Update the comment above to "retry + circuit breaker + per-attempt timeout".
- [ ] Confirm order is retry (outer) -> breaker -> timeout so timeouts count and open circuit is not retried.
- [ ] Run existing `HomeAssistantRetryPipelineTests`, `HomeAssistantConditionsReadingProviderTests`: all still pass (call-count assertions of 8 / >4 stay under min throughput 8 + ratio). If one trips the breaker, set `HomeAssistant:CircuitBreakerEnabled=false` in that test's config only.
- [ ] Commit: `feat(ha): wire circuit breaker into resilience pipeline`.

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

### task: update-process-doc

**Files:** Modify `docs/processes/sync-manufacture-conditions.md`.

- [ ] Logic section: add circuit breaker bullet (retry -> breaker -> timeout; ratio 0.5, min throughput 8, sampling 60 s, break 60 s; open circuit => no network calls, degrades to Stale/Unavailable).
- [ ] Configuration table: add the five `HomeAssistant:CircuitBreaker*` keys with defaults.
- [ ] Known quirks / Runtime facts: Tailscale tunnel instability history (#2997, #3288, #3396, #3651), SSL handshake EOF signature, breaker state is per-instance in memory and resets on restart, worst-case wait note unchanged for closed circuit.
- [ ] Update Code entry points description (retry, breaker, timeout); bump `verified_at` to current HEAD short SHA.
- [ ] Run `python3 scripts/process-docs/check.py index`; passes.
- [ ] Commit: `docs(processes): HomeAssistant circuit breaker`.

### task: final-validation

- [ ] `dotnet build` (solution under `backend/`), `dotnet format --verify-no-changes` on touched projects (run `dotnet format` if needed), `dotnet test` for the HomeAssistant test project and `Anela.Heblo.Tests --filter ManufactureConditions`.
- [ ] Ensure no changes outside the adapter, its tests, and the process doc; no DTO/API changes (no OpenAPI regeneration).
- [ ] Note in PR description: ops follow-up (Tailscale/HA logs for 11:00/16:00/20:00 UTC bursts) remains out of scope.
