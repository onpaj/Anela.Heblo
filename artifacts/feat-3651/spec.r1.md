# Specification: HomeAssistant HTTP circuit breaker

## Summary
Add a Polly circuit breaker to the HomeAssistant HTTP client so that, when the Tailscale tunnel to `homeassistant.tail0cdb23.ts.net` is dead (SSL handshake EOF / IOException / SocketException), the adapter fails fast for a cooldown window instead of repeatedly retrying every sensor request. This bounds the retry amplification seen on 2026-07-14 (240 exceptions from 48 faulted calls) and reduces telemetry noise and latency.

## Background
Issue #3651 is the 4th recurrence of `dep-fail:HTTP:homeassistant.tail0cdb23.ts.net` (previous: #2997, #3288, #3396). The root cause (Tailscale tunnel instability) is outside the repo. A circuit breaker was recommended in #3288 and #3396 but never implemented. Current pipeline (`HomeAssistantAdapterServiceCollectionExtensions`): resilience handler `ha-conditions` = retry (RetryCount=2, exponential 200ms..2s, jitter) -> per-attempt timeout; plus `HomeAssistantRetryActivityTaggingHandler` (tags failed attempts so `HomeAssistantDependencyTelemetryFilter` drops them). Each snapshot fetch issues 4 parallel sensor GETs, each up to 3 attempts = up to 12 failing calls per fetch. `HomeAssistantConditionsReadingProvider` catches all exceptions per sensor and degrades to Partial/Stale/Unavailable, so user impact is already contained; the breaker is a load/noise/latency improvement, not a correctness fix.

## Functional Requirements

### FR-1: Circuit breaker strategy in the resilience pipeline
Add a circuit breaker (`AddCircuitBreaker` with `HttpCircuitBreakerStrategyOptions`) to the `ha-conditions` pipeline, ordered retry (outer) -> circuit breaker -> timeout (inner). The breaker counts per-attempt outcomes, so per-attempt timeouts count as failures.
**Acceptance criteria:**
- Failures counted are exactly those classified by `HomeAssistantTransientErrorPredicate.IsTransient` (IOException, SocketException, HttpRequestException, timeouts, 5xx); caller cancellation never counts.
- After the failure threshold is met within the sampling window, the circuit opens and subsequent calls throw `BrokenCircuitException` immediately without touching the network.
- After the break duration the circuit goes half-open, allows a probe; success closes it, failure re-opens it.
- The breaker state is shared across all sensor requests of the named client (one circuit for the HA host).

### FR-2: Retry does not amplify an open circuit
`BrokenCircuitException` must not be retried (must not be treated as transient by the retry `ShouldHandle`) so an open circuit costs one fast exception per sensor request, not RetryCount+1.
**Acceptance criteria:**
- With the circuit open, a snapshot fetch makes zero network calls to the handler and completes in well under the retry back-off time.

### FR-3: Graceful degradation unchanged
An open circuit must surface through the existing path: per-sensor null -> Unavailable, or Stale fallback within `StaleSnapshotMaxAgeMinutes`. No new exception type may escape `GetCurrentSnapshotAsync`.
**Acceptance criteria:**
- With circuit open and a prior Live snapshot < 60 min old, source is `Stale`; otherwise `Unavailable`.
- Health check remains Degraded (never Unhealthy) while the circuit is open.

### FR-4: Configurable via HomeAssistantSettings
New settings (with defaults, bound from `HomeAssistant` section): `CircuitBreakerEnabled` (true), `CircuitBreakerFailureRatio` (0.5), `CircuitBreakerMinimumThroughput` (8), `CircuitBreakerSamplingDurationSeconds` (60), `CircuitBreakerBreakDurationSeconds` (60). Setting `CircuitBreakerEnabled=false` omits the strategy.
**Acceptance criteria:**
- Defaults apply with no config; overrides in tests via in-memory configuration take effect.
- Note: Polly v8 has no "N consecutive failures" mode; failure-ratio over minimum throughput is the equivalent and replaces the issue's wording. MinimumThroughput 8 is two full sensor sweeps' worth of first-attempt failures with retries (a single dead fetch yields up to 12 failed attempts, opening the circuit during the first outage fetch).

### FR-5: Observability
Log (not exception-trace) circuit state transitions at Warning (opened, with break duration), Information (half-open, closed) using the adapter logger category. Do not pass exception objects to the logger (per existing convention to avoid AI exception traces).
**Acceptance criteria:**
- State-change log messages are emitted; no new Application Insights exceptions arise from `BrokenCircuitException` being swallowed by the provider (the provider's existing catch logs a warning without the exception object).
- Short-circuited calls do not generate dependency telemetry (they never reach the HTTP stack).

### FR-6: Documentation
Update `docs/processes/sync-manufacture-conditions.md` (config table, Logic section, Known quirks, bump `verified_at`) and run `python3 scripts/process-docs/check.py index`, as the adapter is owned by that doc.

## Non-Functional Requirements

### NFR-1: Performance
With the circuit open, `GetCurrentSnapshotAsync` returns in < 100 ms when no cached/stale data needs waiting. Healthy-path overhead negligible.

### NFR-2: Security
No new secrets or endpoints. No logging of the access token or URL query data.

### NFR-3: Compatibility
Uses existing `Microsoft.Extensions.Http.Resilience` 8.0.0 / Polly 8; no new packages. DTO/contract rules unaffected (no API change).

## Data Model
None. In-memory breaker state only (resets on app restart; not shared across instances).

## API / Interface Design
No public API change. New `HomeAssistantSettings` properties. Optionally a small internal helper in `Resilience/` (e.g. `HomeAssistantCircuitBreakerOptionsFactory`) building the `HttpCircuitBreakerStrategyOptions` from settings so it can be unit tested.

## Dependencies
Polly / Microsoft.Extensions.Http.Resilience (already referenced). Existing `HomeAssistantTransientErrorPredicate`.

## Out of Scope
- Fixing the Tailscale tunnel or investigating HA host logs / burst timestamps (11:00, 16:00, 20:00 UTC) - an ops follow-up for the issue owner.
- Changing retry counts/timeouts, the gate logic, or cache durations.
- Distributed (cross-instance) circuit state.
- Frontend changes.

## Open Questions
- Assumption: ratio-based breaker (0.5 / min throughput 8 / 60s sampling / 60s break) is acceptable in place of "N consecutive"; values tunable via config.
- Assumption: existing 5-minute snapshot cache means a 60s break is shorter than the typical re-fetch interval, so the breaker mainly helps concurrent bursts (dashboard + order confirmations); defaults can be tuned later.

## Status: COMPLETE
