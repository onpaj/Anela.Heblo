I have enough context. Writing the architecture review now.

# Architecture Review: Optimize `GetRunningInvoiceImportJobs` Endpoint Performance

## Architectural Fit Assessment

The feature is a **surgical performance fix** confined to two well-bounded layers of the existing Clean Architecture:

- `Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs` — Hangfire-specific implementation of the `IBackgroundWorker` cross-cutting abstraction.
- `Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs` — the MediatR handler invoked by the controller.

All three fixes (connection reuse, page-cap, short-lived cache) fit existing patterns:

- **Connection reuse + page cap** belong inside the Hangfire adapter — the abstraction (`IBackgroundWorker` in `Anela.Heblo.Xcc.Services`) already hides the underlying storage. No interface change needed.
- **In-memory caching** has heavy precedent: `IMemoryCache` is already injected by `SalesCostCache`, `FlexiSupplierRepository`, `CatalogRepository`, `FinancialAnalysisService`, etc. Registration via `services.AddMemoryCache()` is idempotent and already occurs in multiple modules.
- **Configuration** follows the Options pattern already used by `HangfireOptions` (`backend/src/Anela.Heblo.API/Extensions/HangfireOptions.cs`, section `"Hangfire"`).

Critical alignment: the handler concatenates running + pending jobs and filters by `JobName.Contains("InvoiceImport")`, so caching at the **handler level** (after the filter) is the only place that captures all three downstream costs in a single cache hit.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────┐
│ InvoicesController.GetRunningInvoiceImportJobs             │
│   (backend/src/Anela.Heblo.API/Controllers)                │
└──────────────────────────┬─────────────────────────────────┘
                           │ MediatR.Send
                           ▼
┌────────────────────────────────────────────────────────────┐
│ GetRunningInvoiceImportJobsHandler  ← cache layer here     │
│   ─ IMemoryCache (singleton)                               │
│   ─ IOptions<HangfireOptions> → RunningJobsCacheSeconds    │
│   ─ IBackgroundWorker (transient)                          │
└──────────────────────────┬─────────────────────────────────┘
                           │ (cache miss only)
                           ▼
┌────────────────────────────────────────────────────────────┐
│ HangfireBackgroundWorker : IBackgroundWorker               │
│   GetRunningJobs() ─── 1 IStorageConnection reused         │
│   GetPendingJobs() ─── MaxPendingJobsPageSize cap          │
│   GetJobDisplayName(conn, jobId, job) ── takes connection  │
└──────────────────────────┬─────────────────────────────────┘
                           ▼
                   Hangfire PostgreSQL storage
```

### Key Design Decisions

#### Decision 1: Place caching in the handler, not in `HangfireBackgroundWorker`

**Options considered:**
- A. Cache at the controller action layer (e.g. `OutputCache` or `ResponseCache`).
- B. Cache inside `HangfireBackgroundWorker.GetRunningJobs()` / `GetPendingJobs()`.
- C. Cache inside `GetRunningInvoiceImportJobsHandler` after filtering.

**Chosen approach:** **C** — cache the filtered `IList<BackgroundJobInfo>` result inside the handler with a fixed cache key (`"invoices:running-import-jobs"`).

**Rationale:**
- `IBackgroundWorker` is a generic cross-cutting abstraction used by other handlers (`GetInvoiceImportJobStatusHandler`, `EnqueueImportInvoicesHandler`, photobank retag, integration tests). Caching there would silently affect unrelated callers and break test determinism.
- The handler already performs the post-filter (`Contains("InvoiceImport")`), so caching the filtered list eliminates *all* downstream work on a cache hit — including the per-job display-name resolution.
- Controller-level output caching is heavier (introduces middleware), and the spec explicitly scopes to `IMemoryCache`.

#### Decision 2: Extend the existing `HangfireOptions` rather than introduce a new options class

**Options considered:**
- A. New `RunningJobsOptions` class.
- B. Add the two new settings to `HangfireOptions` under the existing `"Hangfire"` config section.

**Chosen approach:** **B**. Add `MaxPendingJobsPageSize` (default `200`) and `RunningJobsCacheSeconds` (default `2`) to `HangfireOptions`.

**Rationale:**
- Spec defines the config keys as `Hangfire:MaxPendingJobsPageSize` and `Hangfire:RunningJobsCacheSeconds` — they belong to the existing section.
- `HangfireOptions` is already bound and DI-registered (`services.Configure<HangfireOptions>(...)` at `ServiceCollectionExtensions.cs:332`). No new wiring needed.
- Backwards-compatible: missing keys yield C# property defaults.

#### Decision 3: Inject `IOptions<HangfireOptions>` into both the handler and the worker

**Options considered:**
- A. Hardcoded constants inside `HangfireBackgroundWorker`.
- B. Constructor-injected `IOptions<HangfireOptions>` (worker is `Transient`, options are singleton — no lifetime conflict).

**Chosen approach:** **B**. The worker reads `MaxPendingJobsPageSize`; the handler reads `RunningJobsCacheSeconds`.

**Rationale:** Spec requires both to be tunable via configuration. The Options pattern is the project's standard (`HomeAssistantOptions`, `ProductExportOptions`, etc.). Using `IOptions<HangfireOptions>` (not `IOptionsSnapshot`) is correct here — the worker is transient but values are read per-call; `IOptionsMonitor` would only matter if values needed to change at runtime, which they do not.

#### Decision 4: Cache key, TTL semantics, and disable-by-zero

**Chosen approach:**
- Key: const `private const string CacheKey = "invoices:running-import-jobs";` inside the handler.
- TTL: `AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.RunningJobsCacheSeconds)`.
- When `RunningJobsCacheSeconds <= 0`, bypass the cache entirely (no Set/Get).

**Rationale:** Matches spec FR-3. Absolute (not sliding) expiration prevents indefinite extension under heavy polling. Disable-by-zero gives an explicit kill switch without code change.

#### Decision 5: No interface change to `IBackgroundWorker`

`GetJobDisplayName` becomes an instance method (currently `static`) taking `IStorageConnection`. This is an internal refactor only — the public `IBackgroundWorker` contract is unchanged, so no consumer is impacted.

## Implementation Guidance

### Directory / Module Structure

No new directories. Edit existing files only:

```
backend/src/Anela.Heblo.API/
├── Infrastructure/Hangfire/
│   └── HangfireBackgroundWorker.cs           ← refactor GetRunningJobs / GetPendingJobs / GetJobDisplayName
└── Extensions/
    └── HangfireOptions.cs                    ← add 2 properties

backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/
└── GetRunningInvoiceImportJobsHandler.cs     ← inject IMemoryCache + IOptions<HangfireOptions>, wrap with cache

backend/test/Anela.Heblo.Tests/Features/Invoices/
└── (new) GetRunningInvoiceImportJobsHandlerTests.cs   ← cache hit/miss + filter behavior
└── (new) HangfireBackgroundWorkerTests.cs              ← connection reuse + page-cap (if practical without live Hangfire)
```

### Interfaces and Contracts

**`HangfireOptions` (extended — note class-style as required by project rules; DTOs are classes, but this is a config options class, also a class):**

```csharp
public class HangfireOptions
{
    public static string ConfigurationKey => "Hangfire";
    public string SchemaName { get; set; } = "hangfire_heblo";
    public bool SchedulerEnabled { get; set; } = false;
    public int WorkerCount { get; set; } = 1;
    public bool UseInMemoryStorage { get; set; } = false;
    public int ConnectionLimit { get; set; } = 5;
    public int MaxPendingJobsPageSize { get; set; } = 200;
    public int RunningJobsCacheSeconds { get; set; } = 2;
}
```

**`HangfireBackgroundWorker` (refactored signatures, public surface unchanged):**

```csharp
public class HangfireBackgroundWorker : IBackgroundWorker
{
    private readonly HangfireOptions _options;

    public HangfireBackgroundWorker(IOptions<HangfireOptions> options) =>
        _options = options.Value;

    public IList<BackgroundJobInfo> GetRunningJobs()
    {
        using var connection = JobStorage.Current.GetConnection();
        var monitoring = JobStorage.Current.GetMonitoringApi();
        var processingJobs = monitoring.ProcessingJobs(0, _options.MaxPendingJobsPageSize);
        // ... iterate, calling GetJobDisplayName(connection, key, job.Value.Job)
    }

    public IList<BackgroundJobInfo> GetPendingJobs()
    {
        using var connection = JobStorage.Current.GetConnection();
        var monitoring = JobStorage.Current.GetMonitoringApi();
        var enqueued = monitoring.EnqueuedJobs("default", 0, _options.MaxPendingJobsPageSize);
        var scheduled = monitoring.ScheduledJobs(0, _options.MaxPendingJobsPageSize);
        // ... iterate, calling GetJobDisplayName(connection, key, job.Value.Job)
    }

    private static string GetJobDisplayName(IStorageConnection connection, string jobId, Job job)
    { ... }
}
```

Apply the same page-cap to `ProcessingJobs(0, MaxPendingJobsPageSize)` in `GetRunningJobs()` and `GetJobStartedAt()` — `int.MaxValue` there has identical risk and is out of scope per the strict reading of FR-2, **but** see "Specification Amendments" below.

**`GetRunningInvoiceImportJobsHandler` (cache-wrapped):**

```csharp
public class GetRunningInvoiceImportJobsHandler : IRequestHandler<GetRunningInvoiceImportJobsRequest, IList<BackgroundJobInfo>>
{
    private const string CacheKey = "invoices:running-import-jobs";

    private readonly IBackgroundWorker _backgroundWorker;
    private readonly IMemoryCache _memoryCache;
    private readonly HangfireOptions _options;
    private readonly ILogger<GetRunningInvoiceImportJobsHandler> _logger;

    public Task<IList<BackgroundJobInfo>> Handle(GetRunningInvoiceImportJobsRequest request, CancellationToken cancellationToken)
    {
        if (_options.RunningJobsCacheSeconds > 0 &&
            _memoryCache.TryGetValue<IList<BackgroundJobInfo>>(CacheKey, out var cached) && cached is not null)
        {
            return Task.FromResult(cached);
        }

        var result = QueryAndFilter();

        if (_options.RunningJobsCacheSeconds > 0)
        {
            _memoryCache.Set(CacheKey, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.RunningJobsCacheSeconds)
            });
        }

        return Task.FromResult(result);
    }
}
```

### Data Flow

**Cache hit (the common case while a frontend import is running):**

1. Controller → MediatR → `GetRunningInvoiceImportJobsHandler.Handle`.
2. `_memoryCache.TryGetValue("invoices:running-import-jobs", …)` → returns cached `IList<BackgroundJobInfo>`.
3. Zero Hangfire calls, zero PostgreSQL round-trips. Target latency < 5 ms.

**Cache miss (first poll, or every ~2 s):**

1. Handler invokes `GetRunningJobs()`:
   - 1× `JobStorage.Current.GetConnection()` opened.
   - 1× `monitoring.ProcessingJobs(0, 200)`.
   - For each processing job: `connection.GetJobData(...)` and (within `GetJobDisplayName`) `connection.GetJobParameter(...)`, both over the same connection.
2. Handler invokes `GetPendingJobs()`:
   - 1× connection, 1× `EnqueuedJobs(0, 200)`, 1× `ScheduledJobs(0, 200)`, per-job `GetJobData` over the same connection.
3. Concat → filter by `JobName.Contains("InvoiceImport", OrdinalIgnoreCase)` → `Set` in cache for `RunningJobsCacheSeconds`.
4. Return list.

**Total DB round-trips at the storage-connection level:** O(processing-jobs + scheduled-jobs) per cache miss, over **2 connections** (one per worker call). Down from the previous ~`1 + 2*N` connection acquisitions.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Stale data shown to UI for up to `RunningJobsCacheSeconds` | LOW | TTL is short (2 s default) and tunable per-environment; set to 0 to disable in tests if needed. |
| Test factories (`InvoiceImportTestFactory`) override `IBackgroundWorker` with a mock — cache may produce false positives if mock returns differ across calls | LOW | Default `RunningJobsCacheSeconds = 2` in test runs; integration tests can override the config section to `0` for the few tests that change `IBackgroundWorker` behavior mid-test. |
| Page-cap silently truncates a >200-deep backlog; UI shows fewer jobs than reality | LOW | Spec accepts this trade-off (FR-2 acceptance). Constant is config-tunable. Document the truncation in the handler debug log. |
| `IMemoryCache` not registered transitively in some startup paths (e.g. lean test hosts) | LOW | `AddMemoryCache()` is already called by `CatalogModule` and other always-loaded modules; if not, add `services.AddMemoryCache()` explicitly to `AddHangfireServices()` (idempotent). |
| Concurrent cache-miss requests run the Hangfire query in parallel ("cache stampede") | LOW | Volume is low (single user / few pollers); 2 s window prevents pile-up. If needed later, wrap with a `SemaphoreSlim` keyed on `CacheKey` — out of scope. |
| `GetJobStartedAt` still calls `monitoring.ProcessingJobs(0, int.MaxValue)` per request to `GetJobById` (unrelated path) | LOW | Out of scope per spec, but mention in PR description so a follow-up can fix it. |

## Specification Amendments

1. **Apply the page cap to `ProcessingJobs` in `GetRunningJobs()` as well.** The spec only names `EnqueuedJobs` and `ScheduledJobs`, but `GetRunningJobs()` already calls `monitoring.ProcessingJobs(0, int.MaxValue)` at line 87. Same N+1 risk class. Reuse the same `MaxPendingJobsPageSize` constant — no new config key.

2. **`GetJobDisplayName` should also accept the `Job` object (which the caller already has) instead of resolving it again.** Current callers pass `job.Value.Job`; the refactored signature is `GetJobDisplayName(IStorageConnection connection, string jobId, Job job)`. The current code already does this — keep that pattern, just thread the connection through.

3. **Note for `GetPendingJobs()`:** the function currently opens a `using var connection` *and* a monitoring API, but the enqueued-jobs branch never uses `connection`. After the refactor, the connection IS needed inside `GetJobDisplayName`, so the existing `using` becomes legitimate. No additional change.

4. **`GetJobStartedAt` and `GetJobById` are out of scope** (different code path, not implicated in the slow-request telemetry). Document as known follow-up; do not modify.

## Prerequisites

None — all required infrastructure exists:

- **`IMemoryCache` registration:** Already provided via `CatalogModule.AddMemoryCache()` and others (idempotent). No action required, but the implementer should verify the integration test host (`InvoiceImportTestFactory`) resolves `IMemoryCache` — add `services.AddMemoryCache()` to `AddHangfireServices()` defensively if uncertain.
- **`HangfireOptions` DI:** Already bound at `ServiceCollectionExtensions.cs:332`. New properties pick up via default values.
- **No appsettings.json change required** for production deployment. Defaults (`MaxPendingJobsPageSize = 200`, `RunningJobsCacheSeconds = 2`) take effect on first deploy. Operators can override later via `appsettings.{Env}.json` or environment variables (`Hangfire__MaxPendingJobsPageSize`, `Hangfire__RunningJobsCacheSeconds`).
- **No database migration, no new NuGet package, no frontend change.**