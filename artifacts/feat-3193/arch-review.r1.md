# Architecture Review: Identify and Fix Surviving SocketException / Polly Exhaustion (feat-3193)

## Skip Design: true

## Architectural Fit Assessment

This is a pure infrastructure / observability hardening task. No new modules, domain types, or API surfaces are introduced. All changes are confined to three existing subsystems:

1. **`ProductPairingDqtJob` / `ProductPairingDqtComparer`** (Application layer, DataQuality module)
2. **`ShoptetStockClient` / `ShoptetApiAdapterServiceCollectionExtensions`** (ShoptetApi adapter)
3. **`CatalogResilienceService`** (Application layer, Catalog module)

The codebase exploration confirms the spec's hypothesis:

- `ProductPairingDqtJob` (`CronExpression = "0 6 * * *"`, timezone `Europe/Prague`) is registered at `06:00` Prague time, which is `05:00` UTC in winter and `04:00` UTC in summer. The Jun 16 06:39 UTC burst **does not match** Prague 06:00. However, if the host is running UTC as its system timezone (likely in Azure), Hangfire would schedule the job at 06:00 UTC — which is consistent with the observed burst at 06:39 UTC (Hangfire poll interval is 15 s, so actual fire time drifts slightly past the cron minute).
- `RecurringJobMetadata.TimeZoneId` defaults to `"Europe/Prague"`, but the `RecurringJobDiscoveryService` passes `metadata.TimeZoneId` into `HangfireJobRegistrationHelper.RegisterOrUpdate` which calls `TimeZoneInfo.FindSystemTimeZoneById`. On a Linux Azure container, `"Europe/Prague"` resolves correctly via IANA — so Prague 06:00 = UTC 04:00 (summer) / UTC 05:00 (winter). June is CEST (UTC+2), so Prague 06:00 = UTC 04:00. This is a 2.5-hour gap from the observed 06:39. **The most plausible resolution**: the Hangfire DB had a persisted `CronExpression` override in UTC (`"0 6 * * *"` interpreted as UTC by Hangfire when timezone is not rehydrated correctly after a deploy), causing 06:00 UTC execution in June. This must be confirmed via Hangfire dashboard.
- `InvoiceClassificationJob` (`CronExpression = "0 * * * *"`) is hourly and calls FlexiBee only — no `ShoptetStockCsv` HTTP client in that path. The Jun 15 00:05, 01:06, 02:06 cluster cannot be `InvoiceClassificationJob` producing a SocketException through the ShoptetStockCsv named client. The hourly pattern on Jun 15 is most likely Hangfire auto-retry of a previously failed `ProductPairingDqtJob` execution — **`ProductPairingDqtJob` does not carry `[AutomaticRetry(Attempts = 0)]`**, unlike `PlaudTokenRefreshJob` and `ProductExportDownloadJob` which explicitly suppress retries.
- `CatalogResilienceService` wraps **both** `_eshopStockClient.ListAsync` and `_erpStockClient.ListAsync` calls, applying its own 30-second outer Polly timeout and retry pipeline. The double-exception pattern (two exceptions at identical millisecond precision) is explained by this: both calls run sequentially, and the first timeout at `ProductPairingDqtComparer.EshopList` propagates through `DriftDqtJobRunner.RunAsync`, which catches and calls `run.Fail()`, then rethrows. Hangfire logs the rethrow. App Insights captures the log-level Error twice if the retry handler inside `CatalogResilienceService` also logs before the outer catch.
- The **dependency telemetry gap** is structural: `ShoptetStockClient.ListAsync` fetches via a named `HttpClient` (`"ShoptetStockCsv"`) created with `_httpClientFactory.CreateClient("ShoptetStockCsv")`. This bypasses the typed-client registration (`AddHttpClient<IEshopStockClient, ShoptetStockClient>`) and its associated `DependencyTrackingTelemetryModule` instrumentation. Named clients created via `CreateClient()` at call time do **not** automatically carry the outbound `Activity` correlation that produces `dependencies` rows in App Insights. Since Hangfire jobs run outside an ASP.NET request scope, `Activity.Current` is null, so the `DependencyTrackingTelemetryModule` has no parent operation to attach the dependency to — `operation_Name` is empty on the resulting exception telemetry.

## Proposed Architecture

### Component Overview

```
Hangfire Recurring Job Scheduler
        │
        ▼
ProductPairingDqtJob.ExecuteAsync()
  [AutomaticRetry(Attempts=0)]   ← MISSING — add this
        │
        ▼
DriftDqtJobRunner.RunAsync()
        │
        ▼
ProductPairingDqtComparer.CompareAsync()
        │
        ├─── CatalogResilienceService.ExecuteWithResilienceAsync("EshopList")
        │         │
        │         └─── ShoptetStockClient.ListAsync()
        │                    │
        │                    └─── _httpClientFactory.CreateClient("ShoptetStockCsv")
        │                                │  [Polly: retry×3 + 8s per-attempt timeout]
        │                                └─── HTTP GET static CSV URL
        │
        └─── CatalogResilienceService.ExecuteWithResilienceAsync("ErpList")
                  │
                  └─── FlexiStockClient.ListAsync()   ← not the failing path

App Insights Telemetry Pipeline
  ExceptionTelemetry  ← captured (operation_Name empty — no parent Activity)
  DependencyTelemetry ← MISSING for ShoptetStockCsv calls outside request scope
```

### Key Design Decisions

#### Decision 1: Add `[AutomaticRetry(Attempts = 0)]` to `ProductPairingDqtJob.ExecuteAsync`

**Options considered:**
- Leave default Hangfire retry behaviour (10 attempts with exponential backoff over ~1 hour)
- Add `[AutomaticRetry(Attempts = 0)]` to suppress all Hangfire-level retries

**Chosen approach:** Add `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` to `ProductPairingDqtJob.ExecuteAsync`.

**Rationale:** Polly resilience already exists inside `CatalogResilienceService` (retry×3 + circuit breaker) and on the named `HttpClient` (retry×3 + 8s timeout). Hangfire-level retries on top produce the observed hourly recurrence on Jun 15 — the job fails, Hangfire schedules a retry at t+10min, t+30min, t+1h etc., which by coincidence aligns with hourly-ish intervals. Adding `[AutomaticRetry(Attempts = 0)]` eliminates Hangfire-layer retries so transient Shoptet outages surface as a single job failure, not a cascade. This is consistent with the established pattern: `PlaudTokenRefreshJob`, `ProductExportDownloadJob`, and `PlaudPollingJob` all carry this attribute.

#### Decision 2: Add a Hangfire `IServerFilter` to set `Activity.Current` operation name on job execution

**Options considered:**
a. No Hangfire telemetry enricher — accept that `operation_Name` is empty for job telemetry
b. Add a `HangfireJobActivityFilter : Hangfire.Common.JobFilterAttribute, Hangfire.Server.IServerFilter` that starts a named `Activity` at job start and stops it at job completion, giving App Insights a parent operation to attach dependency and exception telemetry to
c. Instrument `DriftDqtJobRunner` directly with a manual `Activity`

**Chosen approach:** Option (b) — a lightweight `HangfireJobActivityFilter` registered as a global filter via `GlobalJobFilters.Filters.Add(new HangfireJobActivityFilter())`. This is the narrowest-scope change that fixes `operation_Name` for all current and future Hangfire jobs simultaneously.

**Rationale:** Option (c) only fixes the DQT path; other jobs remain dark. Option (a) leaves the telemetry gap open for future incidents. Option (b) follows the same pattern as `HomeAssistantRetryActivityTaggingHandler` — a delegating handler that decorates the execution context without touching business logic.

The filter should:
1. Start a `System.Diagnostics.Activity` named `"Hangfire.Job.{jobName}"` using the project's existing `ActivitySource` convention (or create a new one named `"Anela.Heblo.Hangfire"`).
2. Set `Activity.Current` so that App Insights auto-instrumentation picks it up and populates `operation_Name` on child telemetry.
3. Stop/dispose the activity in `OnPerformed`.

#### Decision 3: Add structured log entry in `ShoptetStockClient.ListAsync` at Warning severity before the exception propagates

**Options considered:**
- Rely entirely on the existing `LogError` in the catch blocks of `ShoptetStockClient`
- Add a structured warning before the retry sequence that names the job context

**Chosen approach:** Keep `ShoptetStockClient` as-is for error logging (it already logs fully structured). The key improvement is in `ProductPairingDqtComparer.CompareAsync` — add a `LogWarning` with `JobName`, `OperationName`, and elapsed time when the resilience call throws, before letting the exception propagate to `DriftDqtJobRunner`.

**Rationale:** `DriftDqtJobRunner` already catches and calls `run.Fail(ex.Message)`, so the exception is absorbed. The structured warning at the comparer level provides a breadcrumb linking the job name and the HTTP operation in the same log entry — critical for diagnosis when `operation_Name` is still empty on existing telemetry.

#### Decision 4: Fix the dependency telemetry gap without moving the `ShoptetStockCsv` named client

**Options considered:**
a. Convert `ShoptetStockClient` from named-client injection to typed-client injection (removing the `_httpClientFactory.CreateClient("ShoptetStockCsv")` call inside `ListAsync`)
b. Start a `DependencyTelemetry` manually inside `ShoptetStockClient.ListAsync` using `TelemetryClient`
c. Accept the gap — rely on structured logs instead of App Insights dependency rows

**Chosen approach:** Option (a) — refactor `ShoptetApiAdapterServiceCollectionExtensions` to register `ShoptetStockClient` as a **typed** client for its `IHttpClientFactory`-sourced call, or preferably restructure `ListAsync` to use the already-injected typed `_http` client instead of calling `_httpClientFactory.CreateClient("ShoptetStockCsv")`.

**Rationale:** The current design injects `HttpClient http` as the typed client (via `AddHttpClient<IEshopStockClient, ShoptetStockClient>`) but also injects `IHttpClientFactory` for a **second** named client used only in `ListAsync`. This is inconsistent. The named `"ShoptetStockCsv"` client has the Polly retry pipeline attached. The typed client registered via `AddHttpClient<IEshopStockClient, ShoptetStockClient>` has no resilience handler and no timeout override. The correct fix is to attach the resilience handler to the typed client registration (`AddHttpClient<IEshopStockClient, ShoptetStockClient>`) with the same options, and then use `_http` directly in `ListAsync`, eliminating the named-client call. This makes the client's dependency telemetry flow through the typed-client Activity that App Insights auto-instruments correctly even outside a request scope, as long as `HangfireJobActivityFilter` has created a parent `Activity`.

Option (b) introduces a direct `TelemetryClient` dependency into an adapter — that couples the adapter to App Insights and was intentionally avoided everywhere else (all telemetry goes through `ITelemetryService`).

## Implementation Guidance

### Directory / Module Structure

No new directories. Changes are in-place modifications to existing files:

```
backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/
  ProductPairingDqtJob.cs                  ← add [AutomaticRetry(Attempts=0)]

backend/src/Anela.Heblo.Application/Features/DataQuality/Services/
  ProductPairingDqtComparer.cs             ← add LogWarning on resilience failure

backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/
  ShoptetApiAdapterServiceCollectionExtensions.cs  ← merge named client into typed client
  Stock/ShoptetStockClient.cs              ← remove _httpClientFactory, use _http

backend/src/Anela.Heblo.API/Infrastructure/Hangfire/
  HangfireJobActivityFilter.cs             ← new file (IServerFilter, sets Activity.Current)

backend/src/Anela.Heblo.API/Extensions/
  ServiceCollectionExtensions.cs           ← register HangfireJobActivityFilter as global filter
```

### Interfaces and Contracts

**`HangfireJobActivityFilter`** — new class, no interface:

```csharp
// backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobActivityFilter.cs
using System.Diagnostics;
using Hangfire.Common;
using Hangfire.Server;

public sealed class HangfireJobActivityFilter : JobFilterAttribute, IServerFilter
{
    private static readonly ActivitySource Source = new("Anela.Heblo.Hangfire");

    public void OnPerforming(PerformingContext context)
    {
        var jobName = context.BackgroundJob.Job.Type.Name;
        var activity = Source.StartActivity($"Hangfire.Job.{jobName}", ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag("hangfire.job.id", context.BackgroundJob.Id);
            activity.SetTag("hangfire.job.type", context.BackgroundJob.Job.Type.FullName);
            context.Items["HangfireActivity"] = activity;
        }
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.TryGetValue("HangfireActivity", out var obj) && obj is Activity activity)
        {
            if (context.Exception is not null)
                activity.SetStatus(ActivityStatusCode.Error, context.Exception.Message);
            activity.Dispose();
        }
    }
}
```

Registration in `AddHangfireServices`:
```csharp
GlobalJobFilters.Filters.Add(new HangfireJobActivityFilter());
```

**`ProductPairingDqtJob.ExecuteAsync`** — add attribute:
```csharp
[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public async Task ExecuteAsync(CancellationToken cancellationToken = default)
```

**`ShoptetApiAdapterServiceCollectionExtensions`** — merged typed client:

Remove:
- `IHttpClientFactory` injection from `ShoptetStockClient` constructor
- `_httpClientFactory.CreateClient("ShoptetStockCsv")` call in `ListAsync`
- The separate `services.AddHttpClient("ShoptetStockCsv", ...)` registration

Add: resilience handler directly on the typed client registration:
```csharp
services.AddHttpClient<IEshopStockClient, ShoptetStockClient>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
    var opts = sp.GetRequiredService<IOptions<ShoptetStockClientOptions>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
    client.Timeout = Timeout.InfiniteTimeSpan; // Polly AddTimeout manages per-attempt timeout
})
.AddResilienceHandler("shoptet-stock-csv", (builder, context) =>
{
    var opts = context.ServiceProvider.GetRequiredService<IOptions<ShoptetStockClientOptions>>().Value;
    // ... same retry + per-attempt timeout as existing named client
});
```

`ShoptetStockClient.ListAsync` then uses `_http` directly:
```csharp
using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken);
```

Remove `_httpClientFactory` from the constructor and the field.

### Data Flow

**Happy path (job succeeds):**
```
Hangfire fires ProductPairingDqtJob at cron time
  → HangfireJobActivityFilter.OnPerforming starts Activity("Hangfire.Job.ProductPairingDqtJob")
  → ProductPairingDqtJob.ExecuteAsync (no Hangfire retry)
  → DriftDqtJobRunner.RunAsync
  → ProductPairingDqtComparer.CompareAsync
    → CatalogResilienceService wraps EshopList
      → ShoptetStockClient.ListAsync → _http.GetAsync (typed client, Polly: retry×3, 8s timeout)
      → App Insights DependencyTelemetry emitted (parent Activity populated)
    → CatalogResilienceService wraps ErpList
      → FlexiStockClient.ListAsync
  → DqtRun.Complete
  → HangfireJobActivityFilter.OnPerformed stops Activity
  → Job state: Succeeded
```

**Shoptet connectivity failure path:**
```
ShoptetStockClient._http.GetAsync → timeout (8s) → Polly retries ×3 (~30s total)
  → Each retry: LogWarning from retry handler (OperationName, AttemptNumber)
  → After MaxRetryAttempts: HttpRequestException thrown
  → CatalogResilienceService catches: LogError("Failed to execute {OperationName}"), rethrows
  → ProductPairingDqtComparer: LogWarning with job context (new addition), rethrows
  → DriftDqtJobRunner.RunAsync catch: LogError, run.Fail(ex.Message)
  → DqtRun persisted as Failed
  → Exception rethrown → Hangfire marks job Failed (no retry due to [AutomaticRetry(Attempts=0)])
  → App Insights ExceptionTelemetry: operation_Name = "Hangfire.Job.ProductPairingDqtJob"
  → App Insights DependencyTelemetry: HTTP dependency row present (Success=false)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Merging the `"ShoptetStockCsv"` named client into the typed client registration changes the `HttpClient` base address from `settings.BaseUrl` (API base) to the same `settings.BaseUrl`, but `ListAsync` uses `_stockClientOptions.Value.Url` which is a full absolute URL. This means the typed client's `BaseAddress` is irrelevant for `GetAsync(url, ct)` when `url` is absolute. | Low | Verify `_stockClientOptions.Value.Url` is always an absolute URL (it defaults to `"http://"` — confirm production value is absolute). No base address conflict for absolute URL gets. |
| `client.Timeout = Timeout.InfiniteTimeSpan` on the typed client: previously the typed client had a 100s default timeout. Setting to infinite is correct when Polly manages per-attempt timeout, but if `CatalogResilienceService`'s 30s outer timeout fires before Polly's per-attempt timeout inside the named client, the exception type may differ (`TimeoutRejectedException` vs `TaskCanceledException`). | Low | The outer 30s Polly timeout in `CatalogResilienceService` remains. Per-attempt 8s × 3 attempts + 2s retry delays ≈ 28s, which is just under the 30s outer budget. Validate that `ShouldHandle` predicates in both Polly pipelines cover `TimeoutRejectedException`. |
| `HangfireJobActivityFilter` starting an `ActivitySource` with a new source name requires that the App Insights SDK listens to it. App Insights auto-listens to `ActivitySource` names that begin with `"Microsoft."` or are registered. | Medium | Register the source via `services.AddOpenTelemetry()` or confirm that App Insights 2.x SDK picks up all `ActivitySource` names automatically (it does via the OpenTelemetry bridge in App Insights SDK 2.22+). Test on staging before production. If App Insights does not pick up the activity, `operation_Name` will remain empty but no regression occurs. |
| Removing `IHttpClientFactory` from `ShoptetStockClient` is a constructor signature change. Tests that mock `IHttpClientFactory` will need updating. | Low | `backend/test/Anela.Heblo.Tests/` — check `ShoptetStockClient` test files and update constructor usage. |
| `ProductPairingDqtJob` uses `Europe/Prague` as the cron timezone. If a job already has `CronExpression` = `"0 6 * * *"` persisted in the Hangfire DB from before `TimeZoneId` was wired into the DB config, the fire time depends on how Hangfire stored the timezone at registration. | Medium | After deploying, verify in the Hangfire dashboard that `daily-product-pairing-dqt` shows the expected next-fire time (Prague 06:00 → UTC 04:00 in summer). If incorrect, delete the Hangfire recurring job record and let `RecurringJobDiscoveryService` re-register it. |

## Specification Amendments

**Amendment 1 — Jun 15 hourly cluster cause:**
The spec lists `InvoiceClassificationJob` as the primary alternative for the Jun 15 cluster. Code inspection rules this out: `InvoiceClassificationJob` calls only FlexiBee (`IInvoiceClassificationsClient` / `IReceivedInvoicesClient`), not `ShoptetStockCsv`. The correct explanation is Hangfire default retry behaviour applied to `ProductPairingDqtJob` — after the first failure at some point on Jun 14 or 15, Hangfire scheduled automatic retries at 10-minute, 30-minute, then hourly intervals. The spec's FR-4 ("Investigate Jun 15 hourly cluster") can be closed by confirming Hangfire job history shows `daily-product-pairing-dqt` in Failed/Retrying state during that window.

**Amendment 2 — Double exception root cause:**
The spec proposes two parallel async calls as the cause of paired exceptions. Code inspection shows `CompareAsync` calls EshopList and ErpList **sequentially** (not in parallel). The double-logging is caused by `CatalogResilienceService.ExecuteWithResilienceAsync` emitting `LogError` on the catch, and then `DriftDqtJobRunner.RunAsync` also emitting `LogError` on its catch, both attached to the same exception instance at identical timestamps. Fix: remove the `LogError` from `CatalogResilienceService`'s generic catch (it adds no context over the caller's log); or demote it to `LogDebug`. The spec's FR-3 structured logging addition should land in `ProductPairingDqtComparer`, not in `CatalogResilienceService`.

**Amendment 3 — FR-1 confirmation path:**
The spec says to confirm the failing job via Hangfire history. The exact query to run against the Hangfire PostgreSQL schema (schema name is in `HangfireOptions.SchemaName`, default `"hangfire"`) is:

```sql
SELECT j.id, j.invocationdata, s.name AS state, s."createdAt", s.reason
FROM hangfire.job j
JOIN hangfire.state s ON s.id = j.stateid
WHERE s."createdAt" BETWEEN '2026-06-15 00:00:00' AND '2026-06-16 13:00:00'
  AND s.name IN ('Failed', 'Processing')
ORDER BY s."createdAt";
```

The `invocationdata` JSON column contains the job type. Filter for `ProductPairingDqtJob` to confirm.

## Prerequisites

1. **Hangfire dashboard access** — confirm which job ran at 2026-06-15 00:05, 01:06, 02:06 UTC before cutting code. This determines whether `StockWriteBackDqtJob` (07:00 daily) or `InvoiceDqtJob` (05:00 daily) is also implicated, or whether the source is exclusively `ProductPairingDqtJob` retries.

2. **No secrets or config changes** required — `ShoptetStockClientOptions` settings remain; only DI wiring changes.

3. **Test update** — `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogResilienceServiceTests.cs` and any test that constructs `ShoptetStockClient` directly (search for `new ShoptetStockClient(` in the test project) must have constructor arguments updated after removing `IHttpClientFactory`.

4. **App Insights SDK version check** — confirm `Microsoft.ApplicationInsights.AspNetCore` >= 2.22 in the API `.csproj` to ensure `ActivitySource` auto-listening is active. If below that version, the `HangfireJobActivityFilter` activity will not populate `operation_Name` and additional registration steps are needed.
