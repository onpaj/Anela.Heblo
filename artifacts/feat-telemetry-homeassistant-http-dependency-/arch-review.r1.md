```markdown
# Architecture Review: Resilient HomeAssistant Dependency

## Skip Design: true

Pure backend resilience/telemetry change. The only user-visible surface is the new `Stale` enum value rendered by an existing tile (`ManufactureConditionsTile`) and the manufacture protocol PDF (`ManufactureProtocolDocument.SourceSuffix`). The spec explicitly excludes visual redesign — the tile already shows `source` as a string and the PDF needs only a one-line label entry. No new screens, components, or design tokens.

## Architectural Fit Assessment

The change lands cleanly in an existing, well-shaped seam:

- The adapter (`Anela.Heblo.Adapters.HomeAssistant`) is the only consumer of `HttpClient` for HA, and the only producer of `ConditionsSnapshot`. All resilience belongs here — no domain or application changes required.
- The codebase already standardizes on **Polly v8** (`MetaAdsTransactionSource` uses `Polly` 8.4.1 + `Polly.Extensions`). `Microsoft.Extensions.Diagnostics.HealthChecks` is on the Application graph. Pattern precedent exists for `ITelemetryProcessor` via `CostOptimizedTelemetryProcessor` registered through `AddApplicationInsightsTelemetryProcessor<T>`. We should reuse these patterns, not invent new ones.
- The Domain enum `ConditionsReadingSource` lives in the Domain layer and is consumed by three downstream components: `ManufactureConditionsTile`, `ManufactureProtocolDocument.SourceSuffix`, and `UpdateManufactureOrderStatusHandler`. Adding `Stale` is a single non-breaking ordinal append (ordinal 4). Audit confirms only `ManufactureProtocolDocument.SourceSuffix` needs a new switch arm — the others use `.ToString()` or compare only to `Unavailable`.
- Health-check endpoints (`/health`, `/health/ready`, `/health/live`) and `AddHealthCheckServices` are already wired in the API layer. New check slots in without endpoint changes.

Main integration tension: the **resilience pipeline lives in the Adapter project but the telemetry processor coupling lives in the API project** (where AI is registered). Keep the processor type *defined* in the Adapter project (so it travels with the integration) and *registered* from the API layer (conditional on AI being configured), mirroring how `CostOptimizedTelemetryProcessor` is wired today.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.API                                                      │
│                                                                      │
│   AddOptimizedApplicationInsights ─┐                                 │
│                                    ├── AddApplicationInsights         │
│                                    │   TelemetryProcessor<           │
│                                    │   HomeAssistantDependency       │
│                                    │   TelemetryFilter>  (conditional)│
│   AddHealthCheckServices ──────────┼── .AddCheck<HomeAssistant       │
│                                    │       ConditionsHealthCheck>    │
│                                    │     (tag: "ready", "homeassistant")│
│   AddHomeAssistantAdapter ─────────┘                                 │
└────────────────┬─────────────────────────────────────────────────────┘
                 │
┌────────────────▼─────────────────────────────────────────────────────┐
│ Anela.Heblo.Adapters.HomeAssistant                                   │
│                                                                      │
│   AddHomeAssistantAdapter(IConfiguration)                            │
│   ├─ Options<HomeAssistantSettings>  (+ Retry/Stale knobs)           │
│   ├─ IMemoryCache                                                    │
│   ├─ AddHttpClient<HomeAssistantConditionsReadingProvider>           │
│   │   .AddResilienceHandler("ha-conditions", b => b                  │
│   │       .AddTimeout(perAttempt)                                    │
│   │       .AddRetry(retryOptions {                                   │
│   │           ShouldHandle = transientPredicate,                     │
│   │           OnRetry = TagActivityAsSuppressed                      │
│   │       }))                                                        │
│   ├─ HomeAssistantConditionsHealthCheck : IHealthCheck               │
│   ├─ HomeAssistantSnapshotMetrics  (Meter "Anela.Heblo.HomeAssistant")│
│   └─ HomeAssistantDependencyTelemetryFilter : ITelemetryProcessor    │
│                                                                      │
│   HomeAssistantConditionsReadingProvider                             │
│   ├─ live cache:    "HomeAssistant_ConditionsSnapshot"   (5 min)     │
│   ├─ LKG cache:     "HomeAssistant_LastKnownGoodSnapshot"(60 min)    │
│   ├─ single-flight: SemaphoreSlim per CacheKey                       │
│   └─ FetchSensorValueAsync — no try/catch (Polly handles it)         │
└──────────────────────────────────────────────────────────────────────┘
                 │
┌────────────────▼─────────────────────────────────────────────────────┐
│ Domain (consumers of ConditionsReadingSource — Stale added)          │
│   ManufactureConditionsTile (.ToString()) — auto-handles "Stale"     │
│   ManufactureProtocolDocument.SourceSuffix — new switch arm          │
│   UpdateManufactureOrderStatusHandler — unaffected (only checks      │
│                                          for Unavailable fallback)   │
└──────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Resilience via `AddResilienceHandler`, not in-provider `ResiliencePipeline.ExecuteAsync`

**Options considered:**
- (A) Inject `ResiliencePipelineProvider<string>` into the provider, wrap `_httpClient.GetAsync` inline.
- (B) Configure resilience on the typed `HttpClient` via `IHttpClientBuilder.AddResilienceHandler(...)`.
- (C) Use `AddStandardResilienceHandler` with defaults override.

**Chosen approach:** (B) — explicit `AddResilienceHandler` with a named pipeline.

**Rationale:** Keeps the adapter free of Polly concerns (provider code stays a thin JSON parser), centralizes per-attempt timeout + retry in one place adjacent to `HttpClient.Timeout` (which must be raised — see Decision 2). Option C bundles rate-limiter + circuit-breaker + hedging we don't want; option A complicates testing because Polly state crosses transport boundaries.

#### Decision 2: Disable `HttpClient.Timeout`; use per-attempt Polly timeout

**Options considered:**
- (A) Keep `HttpClient.Timeout = RequestTimeoutSeconds` (3 s) — Polly retries inside this budget.
- (B) Set `HttpClient.Timeout = InfiniteTimeSpan`; use Polly `TimeoutStrategy` per attempt = 3 s.
- (C) Raise `HttpClient.Timeout` to `RequestTimeoutSeconds × (RetryCount + 1)` — both timeouts active.

**Chosen approach:** (B).

**Rationale:** `HttpClient.Timeout` cancels the *entire* `SendAsync` chain including retries — a 3 s ceiling means we can never actually retry. Option C works but creates two redundant timeout knobs and confusing semantics if either changes. Polly's `TimeoutStrategy` produces a `TimeoutRejectedException`/`OperationCanceledException` that the retry strategy can distinguish from caller cancellation via the inbound `CancellationToken`. The provider continues to honor caller cancellation by passing the inbound token to Polly's `ExecuteAsync` (via the HttpClient send path), satisfying FR-1 (≤200 ms cancellation).

#### Decision 3: Two distinct cache entries with single-flight gating

**Options considered:**
- (A) One cache entry, demote source when serving from beyond live TTL.
- (B) Live cache (5 min, absolute) + separate Last-Known-Good cache (60 min, absolute from `RecordedAt`, written only on `Live` source).

**Chosen approach:** (B).

**Rationale:** Spec FR-2 requires that **Partial** results never overwrite the LKG entry. A single cache cannot express "live for 5 minutes, but the underlying value is also reusable for 60 minutes when fresh refresh fails." Single-flight (NFR-3) is implemented as a `SemaphoreSlim` keyed by `CacheKey` (the provider is registered transient but the singleton `IMemoryCache` and a singleton `HomeAssistantSnapshotCoordinator` hold the semaphore — see Implementation Guidance).

#### Decision 4: Activity-tag–based telemetry suppression

**Options considered:**
- (A) Replace AI's `DependencyTrackingTelemetryModule` HTTP collector with a custom one.
- (B) Add an `ITelemetryProcessor` that drops `DependencyTelemetry` for HA when an Activity tag marks the attempt as "retry-suppressed".
- (C) Use a `DelegatingHandler` that swallows the dependency record per attempt.

**Chosen approach:** (B).

**Rationale:** AI auto-instruments outbound HTTP via `System.Diagnostics.Activity`. `DependencyTrackingTelemetryModule` enriches each dependency from the current Activity's tags. In Polly's `RetryStrategyOptions.OnRetry`, set `Activity.Current?.SetTag("ha.retry-suppress", "true")` on the *just-failed* attempt's activity. The processor inspects `DependencyTelemetry.Properties["ha.retry-suppress"]` and drops the item. The final attempt (success or failure) carries no tag → exactly one dependency record per `GetCurrentSnapshotAsync` per sensor, matching FR-3 acceptance criteria. Option A is invasive; option C requires duplicating AI's HTTP-dependency logic.

#### Decision 5: Custom metric via `System.Diagnostics.Metrics.Meter`

**Options considered:**
- (A) `TelemetryClient.GetMetric("homeassistant.snapshot.source", "source").TrackValue(1)`.
- (B) `Meter("Anela.Heblo.HomeAssistant").CreateCounter<long>("homeassistant.snapshot.source")` with a `source` tag.

**Chosen approach:** (B).

**Rationale:** `Meter` is the modern .NET 8 API; AI 2.22+ auto-publishes `Meter` instruments as `customMetrics`. It decouples the adapter from `TelemetryClient` (so the adapter project doesn't need a hard reference to `Microsoft.ApplicationInsights`). A no-op when no listener is attached; no conditional registration needed.

#### Decision 6: Health check reads cache only, never calls HA

**Chosen approach:** `HomeAssistantConditionsHealthCheck` depends on `IMemoryCache` (and a tiny shared coordinator that exposes "last recorded source + RecordedAt") and computes status without any HTTP call.

**Rationale:** FR-4 explicitly forbids outbound traffic on health probes; Kubernetes-style liveness probes would otherwise hammer HA at probe cadence and amplify the very failures we're suppressing.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/
├── HomeAssistantAdapterServiceCollectionExtensions.cs   (modify)
├── HomeAssistantConditionsReadingProvider.cs            (modify)
├── HomeAssistantSettings.cs                             (add knobs)
├── Resilience/
│   ├── HomeAssistantResiliencePipelineBuilder.cs        (new — static helper)
│   └── TransientHttpErrorPredicate.cs                   (new)
├── Caching/
│   └── HomeAssistantSnapshotCoordinator.cs              (new — singleton; single-flight semaphore + LKG accessor)
├── HealthChecks/
│   └── HomeAssistantConditionsHealthCheck.cs            (new)
├── Telemetry/
│   ├── HomeAssistantSnapshotMetrics.cs                  (new — Meter wrapper)
│   └── HomeAssistantDependencyTelemetryFilter.cs        (new — ITelemetryProcessor)
└── Anela.Heblo.Adapters.HomeAssistant.csproj            (add PackageReferences)

backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/
└── ConditionsReadingSource.cs                           (add Stale = 4)

backend/src/Anela.Heblo.API/
├── Extensions/ServiceCollectionExtensions.cs            (register health check in AddHealthCheckServices)
├── Extensions/ApplicationInsightsExtensions.cs          (conditionally register HA telemetry processor)
└── PDFPrints/ManufactureProtocolDocument.cs             (add Stale switch arm)

backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/
├── HomeAssistantConditionsReadingProviderTests.cs       (extend)
├── HomeAssistantConditionsHealthCheckTests.cs           (new)
├── HomeAssistantDependencyTelemetryFilterTests.cs       (new)
└── HomeAssistantResiliencePipelineTests.cs              (new)
```

CSProj additions to `Anela.Heblo.Adapters.HomeAssistant.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="8.10.0" />
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="8.0.0" />
<PackageReference Include="Microsoft.ApplicationInsights" Version="2.22.0" />
```

Add `Microsoft.ApplicationInsights` as the *minimum* surface required for `ITelemetry` / `ITelemetryProcessor` / `DependencyTelemetry` types only. This is a leaf dependency — no transitive concerns.

### Interfaces and Contracts

```csharp
// Domain — non-breaking append.
public enum ConditionsReadingSource
{
    Live = 1,
    Partial = 2,
    Unavailable = 3,
    Stale = 4,
}

// Adapter — config knobs (init-only, defaulted, safe to deploy unset).
public class HomeAssistantSettings
{
    // ... existing properties ...
    public int RetryCount { get; init; } = 2;
    public int RetryBaseDelayMilliseconds { get; init; } = 200;
    public int StaleSnapshotMaxAgeMinutes { get; init; } = 60;
    public int LiveSnapshotMaxAgeMinutes { get; init; } = 15;
}

// Adapter — singleton coordinator shared by provider + health check.
internal sealed class HomeAssistantSnapshotCoordinator
{
    public SemaphoreSlim Gate { get; } = new(initialCount: 1, maxCount: 1);
    public ConditionsSnapshot? LastObservedSnapshot { get; private set; }   // any source, for health check
    public ConditionsSnapshot? LastKnownGoodLive { get; private set; }      // Live only, for fallback
    public void RecordObserved(ConditionsSnapshot s);
    public void RecordLive(ConditionsSnapshot s);
}

// Adapter — metric facade.
internal sealed class HomeAssistantSnapshotMetrics
{
    public const string MeterName = "Anela.Heblo.HomeAssistant";
    public void RecordSnapshot(ConditionsReadingSource source);   // counter +1 tagged source=…
}

// Adapter — telemetry filter; constructor takes ITelemetryProcessor next.
public sealed class HomeAssistantDependencyTelemetryFilter : ITelemetryProcessor
{
    public const string SuppressTagName = "ha.retry-suppress";
    // Drops DependencyTelemetry with Properties[SuppressTagName] == "true".
}

// Adapter — health check.
public sealed class HomeAssistantConditionsHealthCheck : IHealthCheck
{
    // Reads HomeAssistantSnapshotCoordinator only; never calls HA.
}
```

Public surface of `IConditionsReadingProvider` is unchanged (per spec).

### Data Flow

**Happy path (cache miss → all sensors fresh):**

1. `GetCurrentSnapshotAsync` checks live cache → miss.
2. Acquires `Coordinator.Gate` (single-flight). Re-checks cache after acquire (double-check pattern).
3. Fires 4 parallel `FetchSensorValueAsync` calls; each goes through `HttpClient` → Polly `AddResilienceHandler` → typed `HttpClient` → AI dependency collector.
4. All four succeed first attempt. `source = Live`, snapshot built.
5. `Coordinator.RecordLive(snapshot)`; live cache set (5 min); LKG cache set (60 min).
6. `HomeAssistantSnapshotMetrics.RecordSnapshot(Live)`.
7. Single `Information` log: `Source=Live, LiveSensorCount=4, Duration=…, RetryAttempts=0`.
8. Release `Gate`. Return snapshot.

**Transient failure recovered by retry:**

1. Sensor 1 first attempt throws `IOException`. Polly `OnRetry` callback sets `Activity.Current?.SetTag("ha.retry-suppress", "true")` and logs `Debug` (per FR-3).
2. AI dependency collector emits a `DependencyTelemetry` carrying that tag → `HomeAssistantDependencyTelemetryFilter` drops it before send.
3. Polly delays 200 ms ± jitter, retries → success.
4. Snapshot is `Live`. Aggregated `Information` log includes `RetryAttempts=1`.

**All retries exhausted on a sensor + LKG available:**

1. Polly exhausts retries; final attempt's `DependencyTelemetry` has **no** suppress tag → AI keeps one Faulted dependency record.
2. `FetchSensorValueAsync` returns `null` for that sensor.
3. Live snapshot computes as `Partial` or `Unavailable`.
4. Provider checks `Coordinator.LastKnownGoodLive`:
   - If `Unavailable` and LKG exists and `(now - LKG.RecordedAt) ≤ StaleSnapshotMaxAgeMinutes` → return new snapshot with `Source = Stale, RecordedAt = LKG.RecordedAt`, *values from LKG*. Do **not** update live cache or LKG.
   - If `Partial` → return live `Partial` (fresher beats older — per FR-2). Update live cache; do not touch LKG.
5. Single `Warning` log (no exception object): `EntityId, Attempts, LastException.GetType().Name, LastException.Message`.
6. `HomeAssistantSnapshotMetrics.RecordSnapshot(Stale | Partial | Unavailable)`.

**Health probe:**

1. `HomeAssistantConditionsHealthCheck.CheckHealthAsync` reads `Coordinator.LastObservedSnapshot` (in-memory).
2. Status:
   - `Healthy` if last is `Live` and `(now - RecordedAt) ≤ LiveSnapshotMaxAgeMinutes`.
   - `Degraded` if last is `Partial` or `Stale`.
   - `Unhealthy` if last is `Unavailable` or null.
3. Returns immediately; no HTTP. Tagged `"homeassistant"` and `"ready"` (joins the existing `/health/ready` predicate).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `HttpClient.Timeout` of 3 s prevents any retry from running (spec FR-1 silently disabled). | High | Decision 2: set `client.Timeout = Timeout.InfiniteTimeSpan` and enforce per-attempt timeout via Polly `AddTimeout`. Unit test asserts a 7-second retry sequence completes. |
| Polly `OnRetry` does not have the failed attempt's `Activity` in scope (Activity may have already ended). | Medium | Use a `DelegatingHandler` registered **after** `AddResilienceHandler` in the inner pipeline that wraps `SendAsync` in a try/catch and tags the *current* Activity inside `catch` before rethrowing — this guarantees the Activity is the per-attempt one. Validate with a test that asserts exactly one Faulted dependency per snapshot under full-failure. |
| `ManufactureProtocolDocument.SourceSuffix` falls to `default` for `Stale` → silently empty suffix in PDFs. | Medium | Add explicit switch arm: `ConditionsReadingSource.Stale => " (Starší údaje)"`. Covered in spec amendment below. |
| Single-flight `SemaphoreSlim` held across HTTP I/O can stall every caller behind one slow request. | Medium | Bound wait with `await Gate.WaitAsync(timeout, ct)` where `timeout = perAttemptTimeout × (RetryCount + 1) + 1s`. On timeout, fall through to LKG/Unavailable rather than queueing indefinitely. |
| `HomeAssistantDependencyTelemetryFilter` runs even when AI isn't configured (dev/local) → null `_next`. | Low | Register the processor through `AddApplicationInsightsTelemetryProcessor<>` only inside `AddOptimizedApplicationInsights` (which already gates on connection string). The adapter exposes a static `RegisterTelemetryProcessor(IServiceCollection)` helper to keep the type internal-feeling. |
| `LastKnownGoodLive` survives a service restart only until next `Live` snapshot — first failure after restart still yields `Unavailable`. | Low | Acceptable per spec FR-2 cold-start clause. Document inline; revisit if it becomes painful. |
| Stale snapshot's `RecordedAt` may confuse downstream consumers that compare to `now` (e.g., "too old → discard"). | Low | Stamp `RecordedAt = LKG.RecordedAt` (truthful) but add `Source = Stale` discriminator. The tile already shows `recordedAt` to the user — that's the correct UX (data may be a few minutes old). |
| Polly + `HttpClient` inner-handler tests are brittle if Polly internals change. | Low | Test the *behavior* (call counts, exception propagation, cancellation) via `Mock<HttpMessageHandler>` returning sequences (`SetupSequence`) — already the pattern used in the existing test suite. |

## Specification Amendments

1. **FR-2 clarification (additive):** When returning a stale snapshot, copy the LKG's *values* and *RecordedAt* into the returned `ConditionsSnapshot` but set `Source = Stale`. The current snapshot's `RecordedAt` field semantically means "when the values were measured", not "when this call ran" — so it must remain the LKG's timestamp. Confirm this is the intended UX (the tile displays `lastUpdated = snapshot.RecordedAt`).

2. **Decision 2 amendment (FR-1 / NFR-1):** Set `HttpClient.Timeout = Timeout.InfiniteTimeSpan` in `AddHomeAssistantAdapter`; per-attempt timeout is enforced by Polly's `AddTimeout(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds))`. Spec's "worst-case ≤ 9 s" calculation already assumed retries fit inside `RequestTimeoutSeconds × 3`, which only works if the outer transport timeout is removed.

3. **FR-3 amendment (out-of-scope downstream):** `ManufactureProtocolDocument.SourceSuffix` (`backend/src/Anela.Heblo.API/PDFPrints/ManufactureProtocolDocument.cs:276`) must gain a `Stale => " (Starší údaje)"` arm. Spec listed the file in scope but did not enumerate the change.

4. **FR-3 implementation note (activity tagging):** Add an internal `HomeAssistantDependencyTaggingHandler : DelegatingHandler` registered between the Polly resilience handler and the primary handler, so the per-attempt Activity is reliably in scope when tagging on retry. Document the tag name (`ha.retry-suppress`) as the contract between adapter and filter.

5. **NFR-3 single-flight (additive):** Bound the gate wait with a timeout = `perAttemptTimeout × (RetryCount + 1) + 1s`; on timeout, the waiter falls through to the LKG/Unavailable path. Prevents a stuck fetch from cascading.

6. **FR-4 health-check tags:** Register the check with tags `{ "homeassistant", "ready" }` so it participates in `/health/ready` (which currently filters by `"ready"` or `DB_TAG`) without changing the endpoint predicate. The check will also surface under aggregate `/health`.

7. **FR-5 (additive):** Add `RetryMaxDelaySeconds` (default `2`) bounding the worst-case jittered backoff per attempt, to keep cancellation budgets predictable.

## Prerequisites

- **Package references** added to `Anela.Heblo.Adapters.HomeAssistant.csproj`:
  - `Microsoft.Extensions.Http.Resilience` 8.10.0
  - `Microsoft.Extensions.Diagnostics.HealthChecks` 8.0.0
  - `Microsoft.ApplicationInsights` 2.22.0
- **No** new Key Vault secrets, environment variables, or migrations required. New `HomeAssistantSettings` knobs ship with safe defaults; appsettings overrides are optional.
- **No** infrastructure or CI changes. AI is already configured in Staging/Production via `ApplicationInsights:ConnectionString` (gated in `ApplicationInsightsExtensions.cs:14`); the filter activates automatically there.
- **No** new health endpoint — reuses existing `/health` and `/health/ready` mappings in `ApplicationBuilderExtensions.cs:173`.
- **Downstream audit** before merge: confirm the frontend tile (`source` string) gracefully renders the value `"Stale"` (likely just shows it as a label — verify in browser against the existing tile component, no design change needed per spec).
```