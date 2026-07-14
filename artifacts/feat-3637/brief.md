## Module
BackgroundJobs

## Finding
`HangfireBackgroundWorker.GetJobStartedAt` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs`, lines 160–177) retrieves every processing job from the Hangfire storage — using `int.MaxValue` as the page size — just to find the start time of a single job by its ID:

```csharp
private static DateTime? GetJobStartedAt(IStorageConnection connection, string jobId)
{
    try
    {
        var monitoring = JobStorage.Current.GetMonitoringApi();
        var processingJobs = monitoring.ProcessingJobs(0, int.MaxValue);  // ← full table scan

        var processingJob = processingJobs.FirstOrDefault(j => j.Key == jobId);
        if (processingJob.Value != null)
            return processingJob.Value.StartedAt;

        return null;
    }
    catch { return null; }
}
```

`GetJobStartedAt` is called from `GetJobById` (line 141), which is a per-job-ID lookup. Under normal production load with many concurrent or queued jobs this issues a full-table-equivalent scan through all currently-processing jobs for every single-job status call.

## Why it matters
O(N) memory allocation and DB load for an O(1) lookup. The Hangfire `IStorageConnection` already exposes per-job data via `GetJobData(jobId)` and `GetStateData(jobId)` — both of which are used elsewhere in the same class. The `StartedAt` value can be obtained from the state data or by a targeted per-job query rather than scanning the entire processing queue.

## Suggested fix
Remove the `ProcessingJobs` scan. Read `StartedAt` from the state data that `GetJobState` already fetches, or from `connection.GetJobData`:

```csharp
private static DateTime? GetJobStartedAt(IStorageConnection connection, string jobId)
{
    // GetStateData is already called by GetJobState for the same jobId;
    // the "Processing" state stores StartedAt in its Data dictionary.
    var stateData = connection.GetStateData(jobId);
    if (stateData?.Name == "Processing" &&
        stateData.Data.TryGetValue("StartedAt", out var raw) &&
        JobHelper.DeserializeNullableDateTime(raw) is { } startedAt)
    {
        return startedAt;
    }
    return null;
}
```

This eliminates the `int.MaxValue` scan entirely.

---
_Filed by daily arch-review routine on 2026-07-14._
