# Specification: Identify and Fix Surviving SocketException / Polly Exhaustion (feat-3193)

## Summary

A cluster of 19 `SocketException` / `Polly.Outcome.GetResultOrRethrow` exceptions fired between 2026-06-15 and 2026-06-16, all after the PR #3028 (Npgsql resilience) and PR #3045 (HomeAssistant resilience) fixes were deployed. The exceptions originate outside HTTP request scope (Hangfire background jobs), exhibit an 8-second per-attempt timeout, and a 30-second outer Polly timeout — a signature that matches `ShoptetStockClient` / `CatalogResilienceService`, not HomeAssistant or Plaud. This work identifies the exact failing job, confirms the dependency-telemetry gap, and adds resilience where it is absent.

## Background

### Signal summary

- **Exception:** `System.Net.Sockets.SocketException` surfacing through `Polly.Outcome\`1.GetResultOrRethrow`.
- **Window:** Jun 15–16 2026 (post-fix cluster, 19 occurrences). The pre-fix cluster (Jun 11–14) is a separate failure path already addressed.
- **Timestamps:** Hourly cadence Jun 15 (00:05, 01:06, 02:06 UTC); burst Jun 16 (06:39 × 3 retry attempts, then 11:16 and 12:17).
- **Fingerprint:** `operation_Name` is empty → Hangfire context; no matching `dependencies` row → HTTP client is not registered through `IHttpClientFactory` with App Insights auto-instrumentation, or dependency telemetry is being filtered.
- **Paired exceptions:** Two exceptions per event at identical millisecond precision → two parallel async calls, or a double-logged exception in the retry handler.

### Timeout ladder decoded

The observed burst on Jun 16 at 06:39:

| Elapsed | Event |
|---|---|
| 0 s | Attempt 1 → 8 s inner timeout fires |
| +10 s | Attempt 2 → 8 s inner timeout fires |
| +20 s | Attempt 3 → 8 s inner timeout fires |
| ≈+1 s | Polly outer 30 s budget exhausted → SocketException re-thrown |

This matches exactly the `ShoptetStockClientOptions` defaults: `TimeoutSeconds = 8`, `MaxRetryAttempts = 3`. The `ShoptetStockCsv` named `HttpClient` has an outer `client.Timeout = TimeoutSeconds × (MaxRetryAttempts + 1) + 5 = 37 s`, but the `CatalogResilienceService` wrapping the Shoptet call imposes a separate 30-second Polly outer timeout (`AddTimeout(TimeSpan.FromSeconds(30))`), which fires first.

### Most likely failing job

**`ProductPairingDqtJob`** (`CronExpression = "0 6 * * *"`, job name `daily-product-pairing-dqt`). It is the only Hangfire recurring job that:
1. Calls `IEshopStockClient.ListAsync()` → `ShoptetStockClient.ListAsync()` (8 s per-attempt timeout) wrapped by `CatalogResilienceService` (30 s outer Polly timeout).
2. Is scheduled such that a UTC+0 cron at 06:00 is consistent with the Jun 16 06:39 burst (job may have been queued slightly late or retried).
3. Produces two parallel exceptions because `ProductPairingDqtComparer.CompareAsync` calls `_eshopStockClient.ListAsync` and `_erpStockClient.ListAsync` sequentially, but App Insights may log the exception twice via the retry handler and the job runner rethrow.

The Jun 15 hourly cluster (00:05, 01:06, 02:06) does not match any hourly cron. A plausible explanation is that the `ProductPairingDqtJob` (06:00 daily) experienced persistent Shoptet connectivity degradation, and Hangfire retried or re-enqueued the job (despite `AutomaticRetry(Attempts = 0)` being set on `PlaudTokenRefreshJob` and `PlaudPollingJob`, no such attribute is visible on `ProductPairingDqtJob`). Alternatively, the Jun 15 cluster corresponds to a different job — **`InvoiceClassificationJob`** (`CronExpression = "0 * * * *"`) — which fires hourly but calls FlexiBee (not Shoptet). This must be ruled out by checking Hangfire job history.

### Dependency telemetry gap

`ShoptetStockClient.ListAsync()` obtains a named `HttpClient` via `_httpClientFactory.CreateClient("ShoptetStockCsv")`. Named clients created through `IHttpClientFactory` with `AddResilienceHandler` should emit App Insights dependency telemetry automatically via the `DependencyTrackingTelemetryModule`. The absence of a `dependencies` row suggests either:
- The dependency telemetry for this operation is being suppressed by `HomeAssistantDependencyTelemetryFilter` or `CostOptimizedTelemetryProcessor` (adaptive sampling excludes dependencies by default — the `excludedTypes` setting excludes only `"Exception;Event"`, so dependency sampling is subject to the 5-item/s adaptive cap).
- The Shoptet stock CSV URL is a static file host that does not get correlated via operation ID because the Hangfire context has no parent request.

## Functional Requirements

### FR-1: Confirm the failing job via Hangfire history

Check the Hangfire dashboard or database for job executions at these exact UTC timestamps:
- 2026-06-15 00:05, 01:06, 02:06
- 2026-06-16 06:39, 11:16, 12:17

**Acceptance criteria:**
- A Hangfire job class is identified for each timestamp cluster.
- The job class is documented in a comment on this issue before any code changes are made.
- If more than one job matches, all candidates are listed.

### FR-2: Ensure the failing HTTP client emits App Insights dependency telemetry

The named `HttpClient` `"ShoptetStockCsv"` (used by `ShoptetStockClient`) is registered via `AddHttpClient(...)` + `AddResilienceHandler(...)` in `ShoptetApiAdapterServiceCollectionExtensions.cs`. Verify that outbound calls from this client appear in the App Insights `dependencies` table during test runs or controlled retries.

If dependency telemetry is absent, add `AddHttpMessageHandler<>` with an explicit Activity-tagging handler analogous to `HomeAssistantRetryActivityTaggingHandler`, or confirm that `DependencyTrackingTelemetryModule` is picking it up automatically and the gap is purely a sampling artifact.

**Acceptance criteria:**
- After the fix, a test execution of `ProductPairingDqtJob` (or the confirmed failing job) produces at least one `dependencies` row in App Insights for the Shoptet outbound call.
- OR: a code comment explicitly explains why the dependency row is not expected (e.g., sampling exclusion by design) and a log-based alternative is confirmed present.

### FR-3: Add resilience to the identified Hangfire code path if missing

If `ProductPairingDqtJob` is confirmed as the failing job:

The `ProductPairingDqtComparer` already wraps `IEshopStockClient.ListAsync()` via `CatalogResilienceService`. The `ShoptetStockClient` itself is also wrapped with a `shoptet-stock-csv` Polly resilience handler (3 retries, 8 s per-attempt timeout, exponential backoff). The double-layer is intentional. The issue is not an absence of resilience but TCP socket exhaustion at the target (Shoptet CSV feed) surviving all retry attempts.

Required actions:
1. **Verify `AutomaticRetry(Attempts = 0)` on `ProductPairingDqtJob.ExecuteAsync`.** The Hangfire-level retry should be disabled for this job to prevent zombie re-queuing. Add `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` if it is not already present.
2. **Verify that `CatalogResilienceService` is not masking the exception.** The service catches `BrokenCircuitException` and re-throws as `InvalidOperationException`. All other exceptions are re-thrown. Confirm the original `SocketException` stack trace is preserved (not swallowed).
3. **Add structured logging at the point of Shoptet HTTP failure.** The existing catch block in `ShoptetStockClient.ListAsync()` logs `LogError` with the full exception. Confirm this log is emitted and captured by App Insights (it is: `EnableRequestTrackingTelemetryModule = true` + `TrackExceptions = true` is set).

**Acceptance criteria:**
- `ProductPairingDqtJob.ExecuteAsync` carries `[AutomaticRetry(Attempts = 0)]`.
- On a Shoptet connectivity failure, the exception propagates to App Insights `exceptions` table with a non-empty `operation_Name` set to the Hangfire job name (achievable by ensuring Hangfire sets an Activity or by adding a telemetry enricher).
- The `dependencies` table gap is resolved (see FR-2).

### FR-4: Investigate and fix the Jun 15 hourly cluster

If the Jun 15 hourly cluster (00:05, 01:06, 02:06) does not match `ProductPairingDqtJob`, identify the actual job:

Primary alternative candidate: **`InvoiceClassificationJob`** (`CronExpression = "0 * * * *"`). This job calls `IReceivedInvoicesClient → FlexiReceivedInvoicesClient`, which uses the FlexiBee SDK HTTP client (`IHttpClientFactory`-based). FlexiBee is a LAN-accessible ERP (Czech national standard), but the SDK timeout behavior must be verified.

If `InvoiceClassificationJob` is confirmed:
- Determine whether FlexiBee HTTP calls go through a Polly pipeline and what the per-attempt and outer timeout values are.
- If no resilience pipeline exists, apply the same pattern used in `HomeAssistantAdapterServiceCollectionExtensions` (retry + per-attempt timeout via `AddResilienceHandler`).

**Acceptance criteria:**
- The job responsible for the Jun 15 hourly cluster is identified.
- If it is `InvoiceClassificationJob`, a Polly per-attempt timeout matching the observed 8 s is confirmed or refuted in code.
- If an additional unprotected HTTP path is found, a resilience handler is added before close.

### FR-5: Rule out PlaudTokenRefreshClient as the failing path

`PlaudTokenRefreshJob` (`CronExpression = "0 4 * * 0"`, weekly on Sunday) and its `AddHttpClient<PlaudTokenRefreshClient>()` registration have **no Polly resilience handler** and **no explicit `client.Timeout` override**, meaning they inherit the default .NET 100-second `HttpClient.Timeout`. This does not match the observed 8-second inner timeout. Additionally, the job is weekly, not hourly.

However, `PlaudTokenRefreshClient.RefreshAsync` hits `platform.plaud.ai` (internet target), and auth expiry (#3118) may cause 401/4xx responses, not socket timeouts. These are distinct failure modes.

**Acceptance criteria:**
- Hangfire job history at the failing timestamps does not show `plaud-token-refresh` executions.
- If `PlaudTokenRefreshJob` is found at any of the timestamps, a separate issue is opened to add a Polly timeout to the `PlaudTokenRefreshClient` HttpClient registration.

### FR-6: Rule out HomeAssistant as the failing path (post-PR-#3045)

`HomeAssistantConditionsReadingProvider` uses `client.Timeout = Timeout.InfiniteTimeSpan` (outer) and a per-attempt timeout of `RequestTimeoutSeconds = 3` (default, configured to 3 seconds in `appsettings.json`). This does **not** match the 8-second inner timeout. Additionally, `HomeAssistantConditionsReadingProvider` is invoked request-scoped (via `IConditionsReadingProvider`), not by a Hangfire recurring job.

The 4 remaining `Faulted` HomeAssistant dependencies post-PR-#3045 are a known residual issue tracked separately.

**Acceptance criteria:**
- Code review confirms `HomeAssistantSettings.RequestTimeoutSeconds` is 3 seconds, not 8.
- The HomeAssistant adapter has no Hangfire recurring job registered.
- Both confirmed — no further HomeAssistant changes required for this feature.

## Non-Functional Requirements

### NFR-1: Performance

No performance regressions. The existing Shoptet retry ladder (`TimeoutSeconds=8`, `MaxRetryAttempts=3`) already bounds the worst case to ~37 seconds. No changes to these values unless the investigation reveals they are contributing to cascading failures.

### NFR-2: Observability

After the fix:
- Every Hangfire job execution that makes an outbound HTTP call should produce a `dependencies` entry in App Insights, or a log line at `LogError`/`LogWarning` level explicitly explaining the failure.
- The `operation_Name` on exception telemetry from Hangfire jobs should be non-empty to enable correlation.

### NFR-3: No new secrets or config

No new secrets are required. All timeout values are already configurable via `ShoptetStockClientOptions`, `HomeAssistantSettings`, and `FlexiAnalyticsSyncOptions` in Azure Key Vault / `appsettings.json`.

## Data Model

No schema changes. This feature is purely infrastructure/resilience and observability.

Relevant existing types:
- `ShoptetStockClientOptions` — `TimeoutSeconds` (8 s default), `MaxRetryAttempts` (3), `RetryBaseDelaySeconds` (1)
- `CatalogResilienceService` — `AddTimeout(TimeSpan.FromSeconds(30))` outer budget
- `HomeAssistantSettings` — `RequestTimeoutSeconds` (3 s default)
- `PlaudTokenRefreshJob.Metadata.CronExpression = "0 4 * * 0"` (weekly)
- `InvoiceClassificationJob.Metadata.CronExpression = "0 * * * *"` (hourly)
- `ProductPairingDqtJob.Metadata.CronExpression = "0 6 * * *"` (daily)

## API / Interface Design

No new public API surface. Changes are confined to:
1. `ProductPairingDqtJob.ExecuteAsync` — add `[AutomaticRetry(Attempts = 0)]` attribute if missing.
2. `ShoptetStockClient` / `ShoptetApiAdapterServiceCollectionExtensions` — add Activity-tagging or confirm App Insights auto-instrumentation is working.
3. Optionally: a Hangfire activity enricher that sets `operation_Name` from the executing job class name, to make exception telemetry correlatable.

## Dependencies

| Dependency | Notes |
|---|---|
| Hangfire dashboard / database | Required to confirm job class per failing timestamp (FR-1) |
| App Insights Live Metrics or Analytics | Required to verify dependency telemetry after fix (FR-2) |
| `ShoptetApiAdapterServiceCollectionExtensions.cs` | Shoptet resilience registration |
| `CatalogResilienceService.cs` | 30 s outer Polly timeout |
| `ProductPairingDqtComparer.cs` | Calls both `IEshopStockClient` and `IErpStockClient` via resilience service |
| `ProductPairingDqtJob.cs` | Daily job, confirmed cron match for Jun 16 06:39 cluster |
| `InvoiceClassificationJob.cs` | Hourly job, candidate for Jun 15 cluster |
| PR #3028 (Npgsql resilience) | Already merged — covers DB socket failures |
| PR #3045 (HomeAssistant resilience) | Already merged — covers HA socket failures |
| Issue #3118 (PlaudAuthExpiredException) | Related but distinct — covers Plaud auth, not socket timeout |

## Out of Scope

- Changes to HomeAssistant resilience (PR #3045 is already complete; the remaining 4 `Faulted` dependency telemetry entries are tracked separately).
- Changes to Plaud authentication flow (issue #3118 tracks this).
- Changes to Npgsql connection resilience (PR #3028 is already complete).
- Modifying Shoptet retry count or timeout values — the existing configuration is sound; root cause is transient Shoptet endpoint instability.
- Adding circuit breaker logic to `ProductPairingDqtJob` — the job runs once daily and already fails fast (30 s cap); a circuit breaker is not warranted.
- E2E or integration tests for Shoptet connectivity (no sandbox available; live calls only).

## Open Questions

1. **Which job ran at 2026-06-15 00:05, 01:06, 02:06 UTC?** The hourly cadence points to `InvoiceClassificationJob` (`0 * * * *`), but this job calls FlexiBee (not Shoptet). If FlexiBee is confirmed, what is the per-attempt timeout for the FlexiBee SDK HTTP client? Check `Rem.FlexiBeeSDK.Client` timeout configuration — if it is 8 seconds, this is the source. If it is not 8 seconds, the Jun 15 cluster has a different root cause that needs separate investigation.

2. **Does `ProductPairingDqtJob.ExecuteAsync` carry `[AutomaticRetry(Attempts = 0)]`?** The file at `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs` does not show this attribute in the code reviewed. If Hangfire default retry (10 attempts) is active, the job may have been re-enqueued multiple times after the initial 06:39 failure, explaining the 11:16 and 12:17 recurrences.

3. **Why is `operation_Name` empty?** Hangfire executes jobs outside of an ASP.NET Core request pipeline, so no `HttpContext`-based Activity is present. Does the project have a Hangfire `IJobFilter` or Activity source that sets the operation name? If not, every Hangfire exception will have an empty `operation_Name`, making correlation impossible. A global `IElectStateFilter` or Hangfire server middleware that starts a named `Activity` per job would fix this for all jobs, not just this one.

## Status: HAS_QUESTIONS
