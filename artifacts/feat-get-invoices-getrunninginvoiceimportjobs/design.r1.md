# Design: Optimize GetRunningInvoiceImportJobs Endpoint Performance

## Component Design

### 1. `HangfireOptions` — extended configuration

**File:** `backend/src/Anela.Heblo.API/Extensions/HangfireOptions.cs`

Add two properties to the existing class. Both are optional with safe defaults so no `appsettings.json` changes are required for deployment.

```csharp
public class HangfireOptions
{
    public static string ConfigurationKey => "Hangfire";
    public string SchemaName { get; set; } = "hangfire_heblo";
    public bool SchedulerEnabled { get; set; } = false;
    public int WorkerCount { get; set; } = 1;
    public bool UseInMemoryStorage { get; set; } = false;
    public int ConnectionLimit { get; set; } = 5;

    // NEW
    public int MaxPendingJobsPageSize { get; set; } = 200;
    public int RunningJobsCacheSeconds { get; set; } = 2;
}
```

`MaxPendingJobsPageSize` caps all Hangfire monitoring API page requests.
`RunningJobsCacheSeconds <= 0` disables caching entirely (useful in tests).

---

### 2. `HangfireBackgroundWorker` — connection reuse + page cap

**File:** `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs`

**Changes:**

- Add constructor accepting `IOptions<HangfireOptions>`; store `_options`.
- `GetRunningJobs()`: open **one** `IStorageConnection` before the loop; pass it to `GetJobDisplayName`; apply page cap to `ProcessingJobs`.
- `GetPendingJobs()`: apply page cap to `EnqueuedJobs` and `ScheduledJobs`; pass the existing `connection` to `GetJobDisplayName`.
- `GetJobDisplayName(string jobId, Job job)` → `GetJobDisplayName(IStorageConnection connection, string jobId, Job job)`: accepts the caller's connection; never opens its own.

**Public surface (`IBackgroundWorker`) is unchanged.**

`GetJobById`, `GetJobStartedAt`, and `GetJobState` are out of scope — do not modify them.

Responsibility boundaries after the change:

| Method | Connections opened | Page cap applied |
|---|---|---|
| `GetRunningJobs()` | 1 (shared across all jobs) | `ProcessingJobs(0, MaxPendingJobsPageSize)` |
| `GetPendingJobs()` | 1 (shared across all jobs) | `EnqueuedJobs` + `ScheduledJobs` both capped |
| `GetJobDisplayName(conn, …)` | 0 (uses caller's connection) | — |
| `GetJobById` | 1 (unchanged — out of scope) | — |

Refactored method signatures:

```csharp
public class HangfireBackgroundWorker : IBackgroundWorker
{
    private readonly HangfireOptions _options;

    public HangfireBackgroundWorker(IOptions<HangfireOptions> options)
        => _options = options.Value;

    public IList<BackgroundJobInfo> GetRunningJobs()
    {
        var monitoring = JobStorage.Current.GetMonitoringApi();
        var processingJobs = monitoring.ProcessingJobs(0, _options.MaxPendingJobsPageSize);
        var result = new List<BackgroundJobInfo>();

        using var connection = JobStorage.Current.GetConnection();
        foreach (var job in processingJobs)
        {
            var jobDetails = connection.GetJobData(job.Key);
            if (jobDetails?.Job == null) continue;

            result.Add(new BackgroundJobInfo
            {
                Id = job.Key,
                JobName = GetJobDisplayName(connection, job.Key, job.Value.Job),
                State = "Processing",
                CreatedAt = jobDetails.CreatedAt,
                StartedAt = job.Value.StartedAt,
                Queue = jobDetails.Job.Queue ?? "default"
            });
        }
        return result;
    }

    public IList<BackgroundJobInfo> GetPendingJobs()
    {
        var monitoring = JobStorage.Current.GetMonitoringApi();
        var enqueuedJobs = monitoring.EnqueuedJobs("default", 0, _options.MaxPendingJobsPageSize);
        var scheduledJobs = monitoring.ScheduledJobs(0, _options.MaxPendingJobsPageSize);
        var result = new List<BackgroundJobInfo>();

        using var connection = JobStorage.Current.GetConnection();
        foreach (var job in enqueuedJobs)
        {
            result.Add(new BackgroundJobInfo
            {
                Id = job.Key,
                JobName = GetJobDisplayName(connection, job.Key, job.Value.Job),
                State = "Enqueued",
                CreatedAt = job.Value.EnqueuedAt,
                Queue = "default"
            });
        }
        foreach (var job in scheduledJobs)
        {
            var jobDetails = connection.GetJobData(job.Key);
            if (jobDetails?.Job == null) continue;

            result.Add(new BackgroundJobInfo
            {
                Id = job.Key,
                JobName = GetJobDisplayName(connection, job.Key, job.Value.Job),
                State = "Scheduled",
                CreatedAt = job.Value.EnqueueAt,
                Queue = jobDetails.Job.Queue ?? "default"
            });
        }
        return result;
    }

    private static string GetJobDisplayName(IStorageConnection connection, string jobId, Job job)
    {
        // no longer opens its own connection
        if (job?.Method?.Name == null) return "Unknown Job";

        try
        {
            var customDisplayName = connection.GetJobParameter(jobId, "DisplayName");
            if (!string.IsNullOrEmpty(customDisplayName)) return customDisplayName;
        }
        catch { /* fall through */ }

        // ... existing DisplayNameAttribute + method-name fallback logic unchanged
    }
}
```

---

### 3. `GetRunningInvoiceImportJobsHandler` — short-lived cache

**File:** `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs`

**New dependencies:** `IMemoryCache`, `IOptions<HangfireOptions>`.

**Responsibility:** wrap the existing `GetRunningJobs() + GetPendingJobs() + filter` pipeline with a read-through cache keyed on `"invoices:running-import-jobs"`.

Cache key is a `private const string` inside the handler class.

Cache logic (pseudo-code):

```
if RunningJobsCacheSeconds <= 0  → bypass cache, call worker directly
if memoryCache.TryGetValue(CacheKey) → return cached result
else:
    result = query worker + filter
    memoryCache.Set(CacheKey, result, AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(RunningJobsCacheSeconds))
    return result
```

Absolute expiration (not sliding) prevents indefinite extension under heavy polling.

Refactored class shape:

```csharp
public class GetRunningInvoiceImportJobsHandler
    : IRequestHandler<GetRunningInvoiceImportJobsRequest, IList<BackgroundJobInfo>>
{
    private const string CacheKey = "invoices:running-import-jobs";

    private readonly IBackgroundWorker _backgroundWorker;
    private readonly IMemoryCache _memoryCache;
    private readonly HangfireOptions _options;
    private readonly ILogger<GetRunningInvoiceImportJobsHandler> _logger;

    public GetRunningInvoiceImportJobsHandler(
        IBackgroundWorker backgroundWorker,
        IMemoryCache memoryCache,
        IOptions<HangfireOptions> options,
        ILogger<GetRunningInvoiceImportJobsHandler> logger)
    {
        _backgroundWorker = backgroundWorker;
        _memoryCache = memoryCache;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IList<BackgroundJobInfo>> Handle(
        GetRunningInvoiceImportJobsRequest request,
        CancellationToken cancellationToken)
    {
        if (_options.RunningJobsCacheSeconds > 0 &&
            _memoryCache.TryGetValue<IList<BackgroundJobInfo>>(CacheKey, out var cached) &&
            cached is not null)
        {
            return Task.FromResult(cached);
        }

        try
        {
            var result = _backgroundWorker.GetRunningJobs()
                .Concat(_backgroundWorker.GetPendingJobs())
                .Where(job => job.JobName != null &&
                              job.JobName.Contains("InvoiceImport", StringComparison.OrdinalIgnoreCase))
                .ToList<BackgroundJobInfo>();

            _logger.LogDebug("Found {Count} running/pending invoice import jobs", result.Count);

            if (_options.RunningJobsCacheSeconds > 0)
            {
                _memoryCache.Set(CacheKey, (IList<BackgroundJobInfo>)result,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow =
                            TimeSpan.FromSeconds(_options.RunningJobsCacheSeconds)
                    });
            }

            return Task.FromResult<IList<BackgroundJobInfo>>(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get running invoice import jobs");
            return Task.FromResult<IList<BackgroundJobInfo>>(new List<BackgroundJobInfo>());
        }
    }
}
```

---

### 4. DI registration — defensive `AddMemoryCache`

**File:** `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` → `AddHangfireServices()`

Add `services.AddMemoryCache();` inside `AddHangfireServices()`. The call is idempotent — harmless if `IMemoryCache` is already registered by another module.

---

### Call flow summary

**Cache hit (steady-state during active import polling):**
```
InvoicesController
  → MediatR.Send(GetRunningInvoiceImportJobsRequest)
    → GetRunningInvoiceImportJobsHandler.Handle
      → IMemoryCache.TryGetValue("invoices:running-import-jobs") → HIT
        → return cached IList<BackgroundJobInfo>
```
Zero Hangfire calls. Zero PostgreSQL round-trips. Target < 5 ms.

**Cache miss (first poll, or every ~2 s after TTL expiry):**
```
InvoicesController
  → MediatR.Send(GetRunningInvoiceImportJobsRequest)
    → GetRunningInvoiceImportJobsHandler.Handle
      → IMemoryCache.TryGetValue → MISS
        → HangfireBackgroundWorker.GetRunningJobs()
            1× IStorageConnection
            1× ProcessingJobs(0, 200)
            for each job: connection.GetJobData + connection.GetJobParameter
        → HangfireBackgroundWorker.GetPendingJobs()
            1× IStorageConnection
            1× EnqueuedJobs("default", 0, 200)
            1× ScheduledJobs(0, 200)
            for each job: connection.GetJobData + connection.GetJobParameter
        → filter by "InvoiceImport"
        → IMemoryCache.Set("invoices:running-import-jobs", result, TTL=2s)
        → return IList<BackgroundJobInfo>
```
2 storage connections total (one per worker method), O(N) `GetJobData`/`GetJobParameter` calls over those connections. Down from the previous `~2N` connection acquisitions.

---

### Test surface

Two new test files under `backend/test/Anela.Heblo.Tests/Features/Invoices/`:

**`GetRunningInvoiceImportJobsHandlerTests`**
- Cache hit: second call returns cached result; worker is not called again.
- Cache miss: worker is called; result is stored in cache.
- Cache disabled (`RunningJobsCacheSeconds = 0`): worker is called on every invocation.
- Filter: jobs without "InvoiceImport" in the name are excluded.
- On worker exception: handler returns empty list and logs a warning (existing behavior preserved).

**`HangfireBackgroundWorkerTests`** _(unit-level; mock `IStorageConnection` and `IMonitoringApi`)_
- `GetRunningJobs()` passes the same `IStorageConnection` instance to every `GetJobDisplayName` call.
- `GetPendingJobs()` calls `EnqueuedJobs` with `MaxPendingJobsPageSize`, not `int.MaxValue`.
- `GetPendingJobs()` calls `ScheduledJobs` with `MaxPendingJobsPageSize`, not `int.MaxValue`.
- `GetRunningJobs()` calls `ProcessingJobs` with `MaxPendingJobsPageSize`, not `int.MaxValue`.

---

## Data Schemas

### `BackgroundJobInfo` DTO (unchanged)

`backend/src/Anela.Heblo.Xcc/Services/BackgroundJobInfo.cs`

```csharp
public class BackgroundJobInfo
{
    public string Id { get; set; } = string.Empty;
    public string JobName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public string Queue { get; set; } = string.Empty;
}
```

Field semantics, types, and nullability are unchanged.

---

### HTTP endpoint (unchanged contract)

```
GET /api/invoices/import/running-jobs
Authorization: existing scheme (unchanged)

200 OK
Content-Type: application/json

[
  {
    "id": "string",
    "jobName": "string",
    "state": "Processing" | "Enqueued" | "Scheduled",
    "createdAt": "2025-01-01T00:00:00Z" | null,
    "startedAt": "2025-01-01T00:00:00Z" | null,
    "queue": "string"
  }
]
```

The response is always an array. Returns an empty array on error or when no matching jobs exist.

---

### Configuration schema — new keys

Both keys are optional. Defaults apply when absent from `appsettings.json`.

```json
{
  "Hangfire": {
    "MaxPendingJobsPageSize": 200,
    "RunningJobsCacheSeconds": 2
  }
}
```

| Key | Type | Default | Effect |
|---|---|---|---|
| `Hangfire:MaxPendingJobsPageSize` | `int` | `200` | Page cap for `ProcessingJobs`, `EnqueuedJobs`, `ScheduledJobs` |
| `Hangfire:RunningJobsCacheSeconds` | `int` | `2` | Cache TTL in seconds; `<= 0` disables caching |

Environment variable overrides follow ASP.NET Core double-underscore convention:
- `Hangfire__MaxPendingJobsPageSize`
- `Hangfire__RunningJobsCacheSeconds`
