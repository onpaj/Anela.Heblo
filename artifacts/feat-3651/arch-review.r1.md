# Architecture Review: HomeAssistant HTTP circuit breaker

## Skip Design: true

## Architectural Fit Assessment
Fits cleanly. The adapter already centralises resilience in `AddHomeAssistantAdapter` via `AddResilienceHandler("ha-conditions", ...)` (retry + timeout). Adding a circuit breaker strategy to the same builder is the idiomatic Polly v8 / `Microsoft.Extensions.Http.Resilience` extension. No Application/Domain/API layers change; `HomeAssistantConditionsReadingProvider.FetchSensorValueAsync` already catches generic `Exception` per sensor and returns null, so `BrokenCircuitException` degrades to Partial/Stale/Unavailable with no code change. Backend-only; no UI. Consistent with docs/architecture (adapter isolation, settings in adapter project).

## Proposed Architecture

### Component Overview
```
HttpClient<HomeAssistantConditionsReadingProvider>
  -> ResilienceHandler "ha-conditions"  (one pipeline instance, shared state)
        Retry (outer)  -> CircuitBreaker -> Timeout (per attempt, inner)
  -> HomeAssistantRetryActivityTaggingHandler
  -> primary handler (SocketsHttpHandler)
```
Retry wraps the breaker, so every attempt is sampled by the breaker, and when the breaker is open the retry sees `BrokenCircuitException` (not transient -> no retry).

### Key Design Decisions

#### Decision 1: Strategy order retry -> breaker -> timeout
**Options:** (a) breaker outside retry (counts one outcome per logical call); (b) breaker inside retry, outside timeout.
**Chosen:** (b). **Rationale:** per-attempt sampling opens the breaker quickly during an outage (one dead sweep = up to 12 attempts) and counts per-attempt timeouts as failures; opening mid-retry-loop stops further amplification immediately. The timeout stays innermost to keep its existing per-attempt semantics.

#### Decision 2: Ratio-based breaker, not consecutive-count
**Options:** custom consecutive-failure counter via a DelegatingHandler; Polly `AddCircuitBreaker` (failure ratio + minimum throughput + sampling duration + break duration).
**Chosen:** Polly built-in with defaults FailureRatio 0.5, MinimumThroughput 8, SamplingDuration 60s, BreakDuration 60s. **Rationale:** Polly v8 has no consecutive mode; do not hand-roll state. Amend wording of the issue accordingly (recorded in spec FR-4).

#### Decision 3: Reuse `HomeAssistantTransientErrorPredicate` for breaker `ShouldHandle`
Single source of truth for what counts as a failure; caller cancellation excluded (predicate only matches `OperationCanceledException` with default token). `BrokenCircuitException` and `TimeoutRejectedException` handling: `BrokenCircuitException` is not matched by the predicate (it derives from `ExecutionRejectedException`, not IO/Http), so retry will not retry it - add an explicit unit test to lock this in.

#### Decision 4: Configuration through `HomeAssistantSettings`
Five new properties with defaults, plus `CircuitBreakerEnabled` kill switch. Settings are read inside the `AddResilienceHandler` callback (already the pattern), so no new DI registrations. Repo appsettings need no change (defaults suffice); document keys in the process doc.

#### Decision 5: Observability via logging only
`OnOpened/OnHalfOpened/OnClosed` log through `ILogger` resolved from `context.ServiceProvider` (`ILoggerFactory`, category `Anela.Heblo.Adapters.HomeAssistant.CircuitBreaker`). No exception objects logged (AI exception-trace convention). No new metrics instrument in this change.

## Implementation Guidance

### Directory / Module Structure
- Modify `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantSettings.cs` (new properties).
- Modify `.../HomeAssistantAdapterServiceCollectionExtensions.cs` (insert breaker between `AddRetry` and `AddTimeout`).
- New `.../Resilience/HomeAssistantCircuitBreakerOptionsFactory.cs` (internal static; builds `HttpCircuitBreakerStrategyOptions` from settings + logger) - keeps DI file small and unit-testable. `InternalsVisibleTo` for the test project: verify it exists (the predicate is already `internal`, so it likely does).
- Tests in `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/` (extend `HomeAssistantRetryPipelineTests` pattern or new `HomeAssistantCircuitBreakerTests.cs`).
- Docs: `docs/processes/sync-manufacture-conditions.md`.

### Interfaces and Contracts
```csharp
internal static class HomeAssistantCircuitBreakerOptionsFactory
{
    public static HttpCircuitBreakerStrategyOptions Create(HomeAssistantSettings s, ILogger logger);
}
```
Options: `FailureRatio`, `MinimumThroughput`, `SamplingDuration`, `BreakDuration` from settings; `ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome.Exception, args.Outcome.Result))`; state-change callbacks log. Note Polly validates ranges (FailureRatio 0..1, MinimumThroughput >= 2, SamplingDuration >= 500ms, BreakDuration >= 500ms): invalid config would throw at pipeline build; tests should use valid small values (e.g. BreakDuration 1s). Consider clamping in the factory so a bad config cannot take down the adapter.

### Data Flow
Outage: sensor GET -> retry -> breaker (closed) -> timeout -> SSL EOF IOException -> counted. After >=8 samples with >=50% failures in 60s the circuit opens; further attempts throw `BrokenCircuitException` instantly; retry does not retry; provider catch returns null per sensor -> Stale/Unavailable. After 60s one probe request; success closes.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Breaker opens on a brief blip and hides recovery up to 60s | Low | Short break duration; stale snapshot covers up to 60 min; configurable; kill switch |
| Single shared circuit; one bad sensor 5xx opens it for all | Low | 5xx on one entity is rare; ratio threshold requires broad failure; acceptable |
| Existing retry-pipeline tests rely on exact call counts (e.g. 8 calls) | Medium | Healthy/transient-recovery tests stay under thresholds (min throughput 8 and ratio 0.5); verify the full suite; use `CircuitBreakerEnabled=false` in unrelated tests if flaky |
| Polly option validation throws on invalid config | Low | Clamp values in factory |
| `BrokenCircuitException` logged noisily by provider (Warning per sensor) | Low | Acceptable; one line per sensor; could special-case later |
| Open circuit hides real HA failures from telemetry | Low | State-change warnings logged; health check stays Degraded |

## Specification Amendments
None beyond spec FR-4 wording (ratio instead of consecutive).

## Prerequisites
None: no migrations, packages, or infrastructure. Optional follow-up outside this change: ops check of Tailscale/HA logs.
