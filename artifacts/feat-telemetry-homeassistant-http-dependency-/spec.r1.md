```markdown
# Specification: Resilient HomeAssistant dependency to reduce intermittent IOException noise

## Summary
Harden the `HomeAssistantConditionsReadingProvider` against the persistent ~4.6/day TCP-level connection failures observed against the Tailscale-tunneled Home Assistant instance. Add a per-call retry policy with jitter, serve a stale-but-usable last-known-good snapshot when Home Assistant is briefly unreachable, and suppress noisy exception/dependency telemetry for expected transient faults while still surfacing an aggregated health signal.

## Background
Application Insights telemetry for the last 7 days (ending 2026-06-12T15:12Z) shows 32 `dependency:HTTP` Faulted records against `homeassistant.tail0cdb23.ts.net`, each correlated with a `System.IO.IOException` thrown inside `HomeAssistantConditionsReadingProvider.FetchSensorValueAsync` (~4.6 failures/day, 0.85% of 3,768 total calls). Successful-call latency is healthy (p95 616 ms, p99 814 ms), so this is not a performance regression. The failure target is the on-prem Home Assistant exposed over a Tailscale tunnel — a path with known intermittent reachability (sleep/wake, NAT rebinding, brief HA restarts).

The adapter already returns `null` per-sensor on exception so callers receive a `Partial`/`Unavailable` snapshot rather than throwing, but every failure still:
1. Logs an `IOException` through `ILogger.LogWarning(ex, …)`, which the Application Insights ILogger provider records as an exception trace.
2. Is captured by the `Microsoft.ApplicationInsights.DependencyCollector` as a Faulted HTTP dependency.

These two streams produce continuous low-severity noise that masks real regressions and triggers anomaly-detection signals. Additionally, a failed sensor fetch in the middle of a 5-minute cache window degrades the *entire* snapshot to `Partial`/`Unavailable` even though the prior snapshot would have been fine to reuse — the manufacture order conditions tile and PDF protocols see avoidable gaps.

## Functional Requirements

### FR-1: Retry transient HTTP failures with bounded jittered backoff
Wrap `FetchSensorValueAsync` HTTP calls in a Polly resilience pipeline that retries on transient faults (`IOException`, `SocketException`, `HttpRequestException`, `TaskCanceledException` *not* triggered by the caller's `CancellationToken`, and HTTP 5xx). Use 2 retries with exponential backoff (200 ms, 600 ms) plus ±50% jitter. Total worst-case wall time per sensor stays under `RequestTimeoutSeconds` × 3 ≈ 9 s.

**Acceptance criteria:**
- First attempt failing with `IOException` is retried; a successful second attempt returns the live value and produces a `Live` snapshot.
- Three consecutive failures cause `FetchSensorValueAsync` to fall through to the fallback path (FR-2), not propagate the exception.
- The cumulative retry budget respects the per-sensor `HttpClient.Timeout` (no infinite stalls).
- Caller-provided `CancellationToken` cancellation is honored within ≤200 ms and does not consume retry budget.

### FR-2: Serve last-known-good snapshot as graceful fallback
Extend `GetCurrentSnapshotAsync` so that when the live fetch produces an `Unavailable` snapshot (all four sensors null) or any sensor fails after retries, the provider returns the most recent successful snapshot if one exists and is younger than `StaleSnapshotMaxAgeMinutes` (default 60).

**Acceptance criteria:**
- A fresh process with no cached snapshot still returns `Unavailable` on total failure (no regression in cold-start behavior).
- A cached snapshot ≤ `StaleSnapshotMaxAgeMinutes` old is returned with `Source = ConditionsReadingSource.Stale` (new enum value, see Data Model).
- A cached snapshot older than `StaleSnapshotMaxAgeMinutes` is discarded and `Unavailable` is returned.
- The "last-known-good" entry is independent of the existing 5-minute live cache: it is updated only when a `Live` snapshot is produced and never overwritten by `Partial` or `Stale` results.
- For a `Partial` live snapshot, the provider prefers the live `Partial` reading over a stale `Live` reading (live data is fresher even if incomplete), and stamps `Source = Partial` as today.

### FR-3: Suppress noisy telemetry for expected transient faults
Reduce log/telemetry severity for connection-level Home Assistant failures so a single brief outage no longer floods Application Insights:
- Demote `IOException` / `SocketException` / `HttpRequestException` thrown by `FetchSensorValueAsync` from `LogWarning(ex, …)` to `LogDebug` when the retry pipeline ultimately recovers.
- When all retries are exhausted for a sensor, log a single `LogWarning` *without* the exception object so AI ILogger does not record it as an exception trace; include structured properties `EntityId`, `Attempts`, `LastException.GetType().Name`, `LastException.Message`.
- Suppress the Application Insights dependency telemetry record for the retried calls by attaching a `DependencyTelemetryInitializer` (or equivalent) that filters successful-after-retry attempts. Failed-after-retry attempts MUST still produce exactly one Faulted dependency record per snapshot fetch (not one per retry).

**Acceptance criteria:**
- A 7-day window matching the original signal would produce ≤ ~1 Faulted dependency per actual outage instead of one per call (target reduction: ≥80%).
- No `IOException` exception traces appear in AI for recoverable transients.
- Final-failure telemetry is still queryable via `customDimensions.EntityId` and structured fields for alerting.

### FR-4: Emit a stable health metric and Application Insights custom metric
Expose two signals so on-call operators can monitor the integration without grepping logs:
1. An ASP.NET Core health check `homeassistant-conditions` registered with the standard health-checks pipeline (probe path unchanged; HA check is part of the existing readiness/liveness setup if any, otherwise added under `/health`).
2. A custom metric `homeassistant.snapshot.source` (counter, one increment per `GetCurrentSnapshotAsync` call) tagged with the resulting `Source` value (`Live` / `Partial` / `Stale` / `Unavailable`).

**Acceptance criteria:**
- Health check returns `Healthy` when the most recent snapshot is `Live` and ≤ `LiveSnapshotMaxAgeMinutes` (default 15) old, `Degraded` when `Partial` or `Stale`, `Unhealthy` when `Unavailable` or no snapshot exists.
- Health check does NOT trigger an additional outbound HTTP call to Home Assistant on each probe (must read cached state only).
- The custom metric is visible in AI via `customMetrics | where name == "homeassistant.snapshot.source"` and supports filtering by `source` dimension.

### FR-5: Configurability of new behavior
All new knobs ship as bound options on `HomeAssistantSettings` with safe defaults so the change is configuration-free.

**Acceptance criteria:**
- `RetryCount` (default `2`), `RetryBaseDelayMilliseconds` (default `200`), `StaleSnapshotMaxAgeMinutes` (default `60`), `LiveSnapshotMaxAgeMinutes` (default `15`) are settable via `appsettings*.json` / Key Vault.
- Setting `RetryCount = 0` disables retry (legacy behavior) without disabling stale fallback.
- Setting `StaleSnapshotMaxAgeMinutes = 0` disables the stale fallback (legacy behavior).
- Defaults are documented inline via `<summary>` XML doc comments on the option properties.

## Non-Functional Requirements

### NFR-1: Performance
- Happy-path latency for `GetCurrentSnapshotAsync` must not regress: p95 ≤ 700 ms (currently ~616 ms).
- Worst-case latency for a fully-failing snapshot fetch (all 4 sensors fail all retries) must be ≤ 12 s with default settings (4 sensors run in parallel; each capped at `RequestTimeoutSeconds` × `(RetryCount + 1)` ≈ 9 s).
- Stale-fallback path must add ≤ 5 ms vs. a cache hit (in-memory lookup only).

### NFR-2: Security
- No new credentials introduced; all HTTP auth continues to flow through the existing `Bearer` token sourced from `HomeAssistantSettings.AccessToken` (Key Vault).
- Logging must continue to avoid emitting `AccessToken` or the bearer header (verify in unit test).
- The Polly retry pipeline must not leak HTTP request/response bodies into logs.

### NFR-3: Reliability and resilience
- Single-flight protection on `GetCurrentSnapshotAsync`: concurrent callers during cache miss must not produce more than one outbound burst of 4 sensor calls. Use the existing `IMemoryCache` semantics plus an `AsyncLock` keyed by `CacheKey`, or `MemoryCacheEntryOptions` with a factory pattern.
- Retry pipeline must be per-call (not per-process) so unrelated callers don't share state.

### NFR-4: Observability
- Every snapshot fetch emits exactly one structured log event at `Information` summarizing `Source`, `LiveSensorCount`, `Duration`, `RetryAttempts`.
- The single warning emitted on full failure includes correlation properties (`EntityId`, `Attempts`).

### NFR-5: Testability
- Unit tests cover: happy path, single transient failure recovered by retry, full failure with stale fallback, full failure without stale fallback, stale snapshot expiry, cold start with no cache, cancellation during retry, partial live snapshot does not overwrite last-known-good, telemetry suppression behavior.
- Coverage target ≥ 80% on the adapter project (existing target).
- Tests use `Moq` `HttpMessageHandler` patterns already established in `HomeAssistantConditionsReadingProviderTests.cs`.

## Data Model

### Updated enum `ConditionsReadingSource`
Add a new value while preserving existing ordinals:
```
public enum ConditionsReadingSource
{
    Live = 1,
    Partial = 2,
    Unavailable = 3,
    Stale = 4,   // new — served from last-known-good cache
}
```
Downstream consumers (`ManufactureConditionsTile`, `ManufactureProtocolData`, `ManufactureProtocolDocument`, `UpdateManufactureOrderStatusHandler`) MUST be audited to confirm they handle `Stale` sensibly (display the value, optionally annotate "data may be stale"). No DB migration required (enum is in-memory only).

### Internal "last-known-good" cache entry
New cache key `HomeAssistant_LastKnownGoodSnapshot` storing the most recent `Live` `ConditionsSnapshot`. Lifetime = `StaleSnapshotMaxAgeMinutes` (sliding expiration disabled — absolute expiry from `RecordedAt`).

### `HomeAssistantSettings` additions
```
public int RetryCount { get; init; } = 2;
public int RetryBaseDelayMilliseconds { get; init; } = 200;
public int StaleSnapshotMaxAgeMinutes { get; init; } = 60;
public int LiveSnapshotMaxAgeMinutes { get; init; } = 15;
```

## API / Interface Design

### Provider public surface — unchanged
`IConditionsReadingProvider.GetCurrentSnapshotAsync(CancellationToken)` keeps its signature. Behavior changes are described in FR-2 and FR-3.

### DI registration changes
`HomeAssistantAdapterServiceCollectionExtensions.AddHomeAssistantAdapter` is extended to:
1. Configure a Polly resilience pipeline on the typed `HttpClient` via `AddStandardResilienceHandler` or an inline `AddResilienceHandler` (preferred: explicit configuration for the retry strategy described in FR-1).
2. Register a `HomeAssistantConditionsHealthCheck : IHealthCheck` against the existing `IHealthChecksBuilder`. If the project does not yet wire health checks for outbound dependencies, add `services.AddHealthChecks().AddCheck<HomeAssistantConditionsHealthCheck>("homeassistant-conditions")`.
3. Register the dependency-telemetry filter (`ITelemetryProcessor` or `ITelemetryInitializer`) only when `services.AddApplicationInsightsTelemetry(...)` is in use — detect via `IConfiguration` check on `ApplicationInsights:ConnectionString` to avoid coupling the adapter to AI when AI isn't configured.

### Health endpoint
No new HTTP route; existing health endpoint (if present) gains the `homeassistant-conditions` check. If no health endpoint exists, this spec adds one under `GET /health` returning standard ASP.NET Core health-check JSON payload, gated behind the `Production` and `Staging` environments only (same env policy as today's other diagnostic endpoints).

### Configuration shape (`appsettings.json` / Key Vault)
```
"HomeAssistant": {
  "BaseUrl": "https://homeassistant.tail0cdb23.ts.net",
  "AccessToken": "<from KV>",
  "InnerTemperatureEntityId": "…",
  …
  "RequestTimeoutSeconds": 3,
  "ConditionsCacheDurationMinutes": 5,
  "RetryCount": 2,
  "RetryBaseDelayMilliseconds": 200,
  "StaleSnapshotMaxAgeMinutes": 60,
  "LiveSnapshotMaxAgeMinutes": 15
}
```
Existing Key Vault secrets (`HomeAssistant--BaseUrl`, `HomeAssistant--AccessToken`, etc.) are unchanged. New options have safe defaults and need no KV entry to function.

## Dependencies
- **Polly v8** (`Microsoft.Extensions.Http.Resilience` package) — already a transitive dependency via .NET 8 templates; confirm it is referenced directly in `Anela.Heblo.Adapters.HomeAssistant.csproj`, add if missing.
- **Microsoft.Extensions.Diagnostics.HealthChecks** — verify presence; pull in if not already referenced by the API project graph.
- **Microsoft.ApplicationInsights** dependency-tracking telemetry initializer hook — already in use by the host; the adapter will only register an `ITelemetryProcessor` if AI is configured in DI.
- Domain enum change is consumed by `ManufactureConditionsTile`, `ManufactureProtocolData`, `ManufactureProtocolDocument`, `UpdateManufactureOrderStatusHandler` — those four files require touch-up.

## Out of Scope
- Investigating or remediating the underlying Tailscale tunnel instability or Home Assistant uptime (operational, not application).
- Switching transport away from the Tailscale tunnel (e.g., direct VPN, public reverse proxy).
- Adding push/webhook-based sensor updates from Home Assistant instead of polling.
- Surfacing per-sensor history or trend charts in the frontend.
- Changing the manufacture-order conditions UX (tile/PDF) — only enum-handling correctness, not visual redesign.
- Alerting rules / Application Insights workbook updates (handled separately by ops).
- Front-end changes to display the new `Stale` source visually beyond confirming the existing "data may be unavailable" affordance still renders correctly.

## Open Questions
None.

## Status: COMPLETE
```