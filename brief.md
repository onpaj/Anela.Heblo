## Telemetry

`GET Invoices/GetRunningInvoiceImportJobs` appeared in slow-request telemetry: **2 hits in the last 24h** at an average duration of **3 734 ms** (threshold: 3 000 ms). This endpoint is not tracked before, making it a new finding.

## Root Cause

The handler delegates to `HangfireBackgroundWorker` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs`).

### 1. N+1 database connections in `GetRunningJobs()`

For every processing job a **new `IStorageConnection` is opened inside the loop**, and `GetJobDisplayName` opens *another* connection per job:

```csharp
foreach (var job in processingJobs)
{
    using var connection = JobStorage.Current.GetConnection(); // new connection per job
    var jobDetails = connection.GetJobData(job.Key);
    ...
    // GetJobDisplayName also opens a connection:
    using var connection2 = JobStorage.Current.GetConnection();
    var customDisplayName = connection2.GetJobParameter(jobId, "DisplayName");
}
```

With N processing jobs this creates 2N round-trips to the Hangfire storage (PostgreSQL), each with connection overhead.

### 2. Unbounded page size in `GetPendingJobs()`

```csharp
var enqueuedJobs = monitoring.EnqueuedJobs("default", 0, int.MaxValue);
var scheduledJobs = monitoring.ScheduledJobs(0, int.MaxValue);
```

Using `int.MaxValue` as the page size fetches **every queued and scheduled job** in a single query. If the Hangfire queue accumulates a backlog this will become progressively slower.

## Suggested Fix

1. **Open a single connection before the loop** in `GetRunningJobs()` and reuse it for all `GetJobData` and `GetJobParameter` calls.
2. **Apply a reasonable cap** (e.g. 200) for the `EnqueuedJobs`/`ScheduledJobs` page size — the UI only shows a handful of active jobs at a time.
3. Consider caching the result for a few seconds (e.g. via `IMemoryCache`) since this endpoint is likely polled repeatedly by the frontend while an import is running.
