# Optimize GetRunningInvoiceImportJobs Endpoint Performance — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `GET /Invoices/GetRunningInvoiceImportJobs` comfortably under the 3,000 ms slow-request threshold (target < 1,000 ms typical) by reusing a single Hangfire storage connection per call, capping unbounded page reads, and adding a 2-second in-memory cache.

**Architecture:** Three surgical edits across the existing Clean Architecture: (1) extend `HangfireOptions` with two new tunables, (2) refactor `HangfireBackgroundWorker` to acquire one `IStorageConnection` per call and apply `MaxPendingJobsPageSize` to `ProcessingJobs`/`EnqueuedJobs`/`ScheduledJobs`, (3) wrap `GetRunningInvoiceImportJobsHandler` with a short-lived `IMemoryCache` entry keyed on `"invoices:running-import-jobs"`. The public `IBackgroundWorker` contract and HTTP endpoint contract are unchanged.

**Tech Stack:** .NET 8, MediatR handlers, Hangfire (PostgreSQL storage), `Microsoft.Extensions.Caching.Memory.IMemoryCache`, `Microsoft.Extensions.Options.IOptions<T>`, xUnit + Moq + FluentAssertions for tests.

---

## File Structure

**Modified files:**
- `backend/src/Anela.Heblo.API/Extensions/HangfireOptions.cs` — add `MaxPendingJobsPageSize` and `RunningJobsCacheSeconds` properties with safe defaults.
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs` — accept `IOptions<HangfireOptions>`, reuse a single `IStorageConnection` in `GetRunningJobs()` and `GetPendingJobs()`, cap all three monitoring paging calls, change `GetJobDisplayName` to accept a connection.
- `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs` — inject `IMemoryCache` + `IOptions<HangfireOptions>`, wrap pipeline in a read-through cache.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — defensively call `services.AddMemoryCache()` inside `AddHangfireServices()` (idempotent).

**New files:**
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetRunningInvoiceImportJobsHandlerTests.cs` — handler cache & filter behavior.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/HangfireBackgroundWorkerTests.cs` — placeholder; see Task 5 for why direct unit tests of the worker are out of scope.

**Unchanged contracts:**
- `backend/src/Anela.Heblo.Xcc/Services/IBackgroundWorker.cs`
- `backend/src/Anela.Heblo.Xcc/Services/BackgroundJobInfo.cs`
- `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsRequest.cs`
- The HTTP route, status codes, request shape, and JSON response shape.

---

## Task 1: Extend `HangfireOptions` with two new tunables

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/HangfireOptions.cs`

- [ ] **Step 1: Add the two new properties with safe defaults**

Replace the entire contents of `backend/src/Anela.Heblo.API/Extensions/HangfireOptions.cs` with:

```csharp
namespace Anela.Heblo.API.Extensions;

public class HangfireOptions
{
    public static string ConfigurationKey => "Hangfire";
    public string SchemaName { get; set; } = "hangfire_heblo";
    public bool SchedulerEnabled { get; set; } = false;
    public int WorkerCount { get; set; } = 1;
    public bool UseInMemoryStorage { get; set; } = false;
    public int ConnectionLimit { get; set; } = 5;

    // Page cap applied to Hangfire monitoring API calls (ProcessingJobs, EnqueuedJobs, ScheduledJobs)
    // in HangfireBackgroundWorker. Replaces previous use of int.MaxValue.
    public int MaxPendingJobsPageSize { get; set; } = 200;

    // TTL (seconds) for the in-memory cache of GetRunningInvoiceImportJobs responses.
    // Set to 0 (or any non-positive value) to disable caching entirely.
    public int RunningJobsCacheSeconds { get; set; } = 2;
}
```

- [ ] **Step 2: Verify the project still builds**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/HangfireOptions.cs
git commit -m "feat: add MaxPendingJobsPageSize and RunningJobsCacheSeconds to HangfireOptions"
```

---

## Task 2: Defensively register `IMemoryCache` in `AddHangfireServices`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:326-332`

- [ ] **Step 1: Add the registration call inside `AddHangfireServices`**

Locate the block in `ServiceCollectionExtensions.cs` (around line 326) that reads:

```csharp
        // Register IBackgroundWorker implementation
        services.AddTransient<IBackgroundWorker, HangfireBackgroundWorker>();

        // Note: IRecurringJobStatusChecker is now registered in Application layer (BackgroundJobsModule)

        // Register configuration options
        services.Configure<HangfireOptions>(configuration.GetSection(HangfireOptions.ConfigurationKey));
        services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));
```

Replace it with:

```csharp
        // Register IBackgroundWorker implementation
        services.AddTransient<IBackgroundWorker, HangfireBackgroundWorker>();

        // Note: IRecurringJobStatusChecker is now registered in Application layer (BackgroundJobsModule)

        // Defensive: ensure IMemoryCache is available for handlers that cache Hangfire responses.
        // AddMemoryCache is idempotent — safe to call even if another module already registered it.
        services.AddMemoryCache();

        // Register configuration options
        services.Configure<HangfireOptions>(configuration.GetSection(HangfireOptions.ConfigurationKey));
        services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));
```

- [ ] **Step 2: Verify the project still builds**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "chore: ensure IMemoryCache is registered alongside Hangfire services"
```

---

## Task 3: Refactor `HangfireBackgroundWorker` — connection reuse + page cap

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs` (full rewrite of the class body; method signatures on the `IBackgroundWorker` interface are unchanged).

**What changes:**
- Add constructor `HangfireBackgroundWorker(IOptions<HangfireOptions> options)`; store `_options`.
- `GetRunningJobs()` — single `using var connection = …` outside the loop; pass `connection` to `GetJobDisplayName`; replace `int.MaxValue` in `ProcessingJobs(0, …)` with `_options.MaxPendingJobsPageSize`.
- `GetPendingJobs()` — connection reuse already shaped this way; pass `connection` to `GetJobDisplayName`; replace `int.MaxValue` in `EnqueuedJobs("default", 0, …)` and `ScheduledJobs(0, …)` with `_options.MaxPendingJobsPageSize`.
- `GetJobDisplayName(string jobId, Job job)` → `GetJobDisplayName(IStorageConnection connection, string jobId, Job job)`. The helper no longer opens its own connection.
- `GetJobById`, `GetJobStartedAt`, and `GetJobState` are **out of scope** — leave them unchanged (do not edit, including the `int.MaxValue` inside `GetJobStartedAt`).

- [ ] **Step 1: Replace the file contents**

Overwrite `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs` with:

```csharp
using System.Linq.Expressions;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.Xcc.Services;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Options;
using System.ComponentModel;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

public class HangfireBackgroundWorker : IBackgroundWorker
{
    private readonly HangfireOptions _options;

    public HangfireBackgroundWorker(IOptions<HangfireOptions> options)
    {
        _options = options.Value;
    }

    public string Enqueue<T>(Expression<Func<T, Task>> methodCall)
    {
        return BackgroundJob.Enqueue(methodCall);
    }

    public string Enqueue<T>(Expression<Action<T>> methodCall)
    {
        return BackgroundJob.Enqueue(methodCall);
    }

    public string Schedule<T>(Expression<Func<T, Task>> methodCall, TimeSpan delay)
    {
        return BackgroundJob.Schedule(methodCall, delay);
    }

    public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
    {
        return BackgroundJob.Schedule(methodCall, delay);
    }

    public string Schedule<T>(Expression<Func<T, Task>> methodCall, DateTimeOffset enqueueAt)
    {
        return BackgroundJob.Schedule(methodCall, enqueueAt);
    }

    public string Schedule<T>(Expression<Action<T>> methodCall, DateTimeOffset enqueueAt)
    {
        return BackgroundJob.Schedule(methodCall, enqueueAt);
    }

    public IList<BackgroundJobInfo> GetPendingJobs()
    {
        var pageSize = _options.MaxPendingJobsPageSize;
        var monitoring = JobStorage.Current.GetMonitoringApi();

        var enqueuedJobs = monitoring.EnqueuedJobs("default", 0, pageSize);
        var scheduledJobs = monitoring.ScheduledJobs(0, pageSize);

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
            if (jobDetails?.Job != null)
            {
                result.Add(new BackgroundJobInfo
                {
                    Id = job.Key,
                    JobName = GetJobDisplayName(connection, job.Key, job.Value.Job),
                    State = "Scheduled",
                    CreatedAt = job.Value.EnqueueAt,
                    Queue = jobDetails.Job.Queue ?? "default"
                });
            }
        }

        return result;
    }

    public IList<BackgroundJobInfo> GetRunningJobs()
    {
        var pageSize = _options.MaxPendingJobsPageSize;
        var monitoring = JobStorage.Current.GetMonitoringApi();
        var processingJobs = monitoring.ProcessingJobs(0, pageSize);

        var result = new List<BackgroundJobInfo>();

        using var connection = JobStorage.Current.GetConnection();

        foreach (var job in processingJobs)
        {
            var jobDetails = connection.GetJobData(job.Key);

            if (jobDetails?.Job != null)
            {
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
        }

        return result;
    }

    public BackgroundJobInfo? GetJobById(string jobId)
    {
        try
        {
            using var connection = JobStorage.Current.GetConnection();
            var jobDetails = connection.GetJobData(jobId);

            if (jobDetails?.Job == null)
                return null;

            var state = GetJobState(connection, jobId);

            return new BackgroundJobInfo
            {
                Id = jobId,
                JobName = GetJobDisplayName(connection, jobId, jobDetails.Job),
                State = state,
                CreatedAt = jobDetails.CreatedAt,
                StartedAt = GetJobStartedAt(connection, jobId),
                Queue = jobDetails.Job.Queue ?? "default"
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string GetJobState(IStorageConnection connection, string jobId)
    {
        var stateData = connection.GetStateData(jobId);
        return stateData?.Name ?? "Unknown";
    }

    private static DateTime? GetJobStartedAt(IStorageConnection connection, string jobId)
    {
        try
        {
            var monitoring = JobStorage.Current.GetMonitoringApi();
            var processingJobs = monitoring.ProcessingJobs(0, int.MaxValue);

            var processingJob = processingJobs.FirstOrDefault(j => j.Key == jobId);
            if (processingJob.Value != null)
                return processingJob.Value.StartedAt;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetJobDisplayName(IStorageConnection connection, string jobId, Job job)
    {
        if (job?.Method?.Name == null)
            return "Unknown Job";

        try
        {
            var customDisplayName = connection.GetJobParameter(jobId, "DisplayName");
            if (!string.IsNullOrEmpty(customDisplayName))
                return customDisplayName;
        }
        catch
        {
            // Fall back if parameter retrieval fails
        }

        var displayNameAttribute = job.Method.GetCustomAttributes(typeof(System.ComponentModel.DisplayNameAttribute), false)
            .FirstOrDefault() as System.ComponentModel.DisplayNameAttribute;

        if (displayNameAttribute != null)
        {
            var displayName = displayNameAttribute.DisplayName;
            for (int i = 0; i < job.Args.Count; i++)
            {
                displayName = displayName.Replace($"{{{i}}}", job.Args[i]?.ToString() ?? "null");
            }
            return displayName;
        }

        var methodName = job.Method.Name;

        if (job.Args?.Count > 0)
        {
            var argsDisplay = string.Join(", ", job.Args.Select(arg =>
            {
                if (arg == null)
                    return "null";

                if (arg is string stringArg)
                    return $"\"{stringArg}\"";

                if (arg.GetType().IsPrimitive || arg is decimal || arg is DateTime || arg is DateTimeOffset)
                    return arg.ToString();

                return $"<{arg.GetType().Name}>";
            }));

            return $"{methodName}({argsDisplay})";
        }

        return $"{methodName}()";
    }
}
```

**Notes for the implementer:**
- The `using Microsoft.Extensions.Options;` import is required by the new constructor.
- The `using Anela.Heblo.API.Extensions;` import is required because `HangfireOptions` lives in that namespace.
- `GetJobById` now passes its existing `connection` to `GetJobDisplayName` instead of letting the helper open another one. This is a side-benefit of the signature change (one less connection per `GetJobById` call) and is required because the helper no longer has the old self-opening fallback.
- `GetJobStartedAt` deliberately keeps `int.MaxValue` for now — it is out of scope per arch-review §"Specification Amendments" item 4. Do not change it. Mention it in the PR description as a follow-up.

- [ ] **Step 2: Verify the project still builds**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Build succeeded, 0 errors. The DI container will resolve `IOptions<HangfireOptions>` automatically because of the `services.Configure<HangfireOptions>(…)` already in `AddHangfireServices`.

- [ ] **Step 3: Run any existing tests that touch Hangfire**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Invoices" --no-restore`
Expected: All pass. If a test fails because it instantiates `HangfireBackgroundWorker` directly with no constructor args, update that test to pass `Options.Create(new HangfireOptions())`.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs
git commit -m "perf(hangfire): reuse single storage connection and cap page reads in worker"
```

---

## Task 4: Cache the handler result with `IMemoryCache`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs`

**What changes:**
- Inject `IMemoryCache` and `IOptions<HangfireOptions>`.
- Read-through cache on a constant key `"invoices:running-import-jobs"`.
- `RunningJobsCacheSeconds <= 0` → bypass cache.
- Existing try/catch + empty-list fallback preserved.

**Cross-project reference note:** `HangfireOptions` lives in `Anela.Heblo.API.Extensions`, and `Anela.Heblo.Application` already references `Anela.Heblo.API` indirectly via the test project. Verify the dependency direction first — if `Anela.Heblo.Application` does **not** reference `Anela.Heblo.API`, the implementer must instead read only the two `int` values needed via a small options record co-located with the handler, or move `HangfireOptions` to a shared project. See Step 0 below.

- [ ] **Step 0: Verify dependency direction (one-time check)**

Run this from the repo root to see whether the Application project already has a path to `Anela.Heblo.API`:

```bash
grep -l "Anela.Heblo.API" backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj || echo "NO_DIRECT_REFERENCE"
```

- If output is `NO_DIRECT_REFERENCE`: Application does **not** reference API. **Stop and resolve before continuing.** The cleanest fix is to relocate `HangfireOptions` to a shared project (e.g. `Anela.Heblo.Xcc`) and update the existing `using` in `ServiceCollectionExtensions.cs` and `HangfireBackgroundWorker.cs` to the new namespace. Do this as a tiny prefix-commit:

  ```bash
  # 1. Move the file
  git mv backend/src/Anela.Heblo.API/Extensions/HangfireOptions.cs \
         backend/src/Anela.Heblo.Xcc/HangfireOptions.cs
  # 2. Update namespace inside the file from "Anela.Heblo.API.Extensions" → "Anela.Heblo.Xcc"
  # 3. Update the two callers that import it (ServiceCollectionExtensions.cs + HangfireBackgroundWorker.cs)
  #    to use `using Anela.Heblo.Xcc;` instead of `using Anela.Heblo.API.Extensions;`
  # 4. Build + commit:
  dotnet build backend/Anela.Heblo.sln
  git add -A
  git commit -m "refactor: move HangfireOptions to Anela.Heblo.Xcc so Application can read it"
  ```

- If output shows the csproj path: Application already references API. **Proceed to Step 1 with `using Anela.Heblo.API.Extensions;` in the handler.**

For the rest of this task, the code blocks assume `HangfireOptions` lives in `Anela.Heblo.API.Extensions`. **If you moved it in Step 0, change `using Anela.Heblo.API.Extensions;` to `using Anela.Heblo.Xcc;` in every code block below.**

- [ ] **Step 1: Replace the handler file contents**

Overwrite `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs` with:

```csharp
using Anela.Heblo.API.Extensions;
using Anela.Heblo.Xcc.Services;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetRunningInvoiceImportJobs;

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
        var cacheTtlSeconds = _options.RunningJobsCacheSeconds;

        if (cacheTtlSeconds > 0 &&
            _memoryCache.TryGetValue<IList<BackgroundJobInfo>>(CacheKey, out var cached) &&
            cached is not null)
        {
            return Task.FromResult(cached);
        }

        try
        {
            var runningJobs = _backgroundWorker.GetRunningJobs();
            var pendingJobs = _backgroundWorker.GetPendingJobs();

            // Filter for invoice import jobs based on job name containing "InvoiceImport"
            var invoiceImportJobs = runningJobs
                .Concat(pendingJobs)
                .Where(job => job.JobName != null &&
                              job.JobName.Contains("InvoiceImport", StringComparison.OrdinalIgnoreCase))
                .ToList();

            _logger.LogDebug("Found {Count} running/pending invoice import jobs", invoiceImportJobs.Count);

            IList<BackgroundJobInfo> result = invoiceImportJobs;

            if (cacheTtlSeconds > 0)
            {
                _memoryCache.Set(CacheKey, result, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(cacheTtlSeconds)
                });
            }

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get running invoice import jobs");
            return Task.FromResult<IList<BackgroundJobInfo>>(new List<BackgroundJobInfo>());
        }
    }
}
```

**Notes:**
- The catch branch deliberately does **not** populate the cache — a transient failure must not poison subsequent polls for the TTL window.
- `AbsoluteExpirationRelativeToNow` (not `SlidingExpiration`) is required so heavy polling cannot indefinitely extend stale data.
- `IList<BackgroundJobInfo>` is the cache value type; matches both the interface return type and the handler signature.

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded, 0 errors. If `Anela.Heblo.Application` cannot resolve `Anela.Heblo.API.Extensions`, you missed Step 0 — go back and relocate `HangfireOptions`.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs
git commit -m "perf(invoices): cache GetRunningInvoiceImportJobs result for short TTL"
```

---

## Task 5: Handler tests — cache hit, miss, disabled, filter, failure path

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetRunningInvoiceImportJobsHandlerTests.cs`

These tests exercise the cache semantics and filter behavior without needing a live Hangfire instance — `IBackgroundWorker` is mocked.

- [ ] **Step 1: Write the failing test file**

Create `backend/test/Anela.Heblo.Tests/Features/Invoices/GetRunningInvoiceImportJobsHandlerTests.cs`:

```csharp
using Anela.Heblo.API.Extensions;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetRunningInvoiceImportJobs;
using Anela.Heblo.Xcc.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class GetRunningInvoiceImportJobsHandlerTests
{
    private static GetRunningInvoiceImportJobsHandler CreateHandler(
        IBackgroundWorker worker,
        IMemoryCache cache,
        int cacheSeconds = 2)
    {
        var options = Options.Create(new HangfireOptions
        {
            RunningJobsCacheSeconds = cacheSeconds
        });
        return new GetRunningInvoiceImportJobsHandler(
            worker,
            cache,
            options,
            NullLogger<GetRunningInvoiceImportJobsHandler>.Instance);
    }

    private static IMemoryCache NewCache() =>
        new MemoryCache(new MemoryCacheOptions());

    private static BackgroundJobInfo Job(string name, string state = "Processing", string id = "j1") =>
        new() { Id = id, JobName = name, State = state, Queue = "default" };

    [Fact]
    public async Task Handle_FiltersToInvoiceImportJobsOnly()
    {
        // Arrange
        var worker = new Mock<IBackgroundWorker>();
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", id: "r1"),
            Job("SomeOtherJob.Run", id: "r2"),
        });
        worker.Setup(w => w.GetPendingJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", state: "Enqueued", id: "p1"),
            Job("UnrelatedJob.Run", state: "Enqueued", id: "p2"),
        });

        var handler = CreateHandler(worker.Object, NewCache());

        // Act
        var result = await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Select(j => j.Id).Should().BeEquivalentTo(new[] { "r1", "p1" });
    }

    [Fact]
    public async Task Handle_CacheHit_DoesNotCallWorkerSecondTime()
    {
        // Arrange
        var worker = new Mock<IBackgroundWorker>();
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", id: "r1"),
        });
        worker.Setup(w => w.GetPendingJobs()).Returns(new List<BackgroundJobInfo>());

        var cache = NewCache();
        var handler = CreateHandler(worker.Object, cache, cacheSeconds: 60);

        // Act
        var first = await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);
        var second = await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);

        // Assert
        first.Should().HaveCount(1);
        second.Should().BeSameAs(first); // returned from cache, exact same reference
        worker.Verify(w => w.GetRunningJobs(), Times.Once);
        worker.Verify(w => w.GetPendingJobs(), Times.Once);
    }

    [Fact]
    public async Task Handle_CacheDisabled_CallsWorkerOnEveryInvocation()
    {
        // Arrange
        var worker = new Mock<IBackgroundWorker>();
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", id: "r1"),
        });
        worker.Setup(w => w.GetPendingJobs()).Returns(new List<BackgroundJobInfo>());

        var handler = CreateHandler(worker.Object, NewCache(), cacheSeconds: 0);

        // Act
        await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);
        await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);
        await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);

        // Assert
        worker.Verify(w => w.GetRunningJobs(), Times.Exactly(3));
        worker.Verify(w => w.GetPendingJobs(), Times.Exactly(3));
    }

    [Fact]
    public async Task Handle_CacheDisabled_DoesNotWriteToCache()
    {
        // Arrange
        var worker = new Mock<IBackgroundWorker>();
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", id: "r1"),
        });
        worker.Setup(w => w.GetPendingJobs()).Returns(new List<BackgroundJobInfo>());

        var cache = NewCache();
        var handler = CreateHandler(worker.Object, cache, cacheSeconds: 0);

        // Act
        await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);

        // Assert
        cache.TryGetValue("invoices:running-import-jobs", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WorkerThrows_ReturnsEmptyListAndDoesNotCache()
    {
        // Arrange
        var worker = new Mock<IBackgroundWorker>();
        worker.Setup(w => w.GetRunningJobs()).Throws(new InvalidOperationException("hangfire down"));

        var cache = NewCache();
        var handler = CreateHandler(worker.Object, cache, cacheSeconds: 60);

        // Act
        var result = await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        cache.TryGetValue("invoices:running-import-jobs", out _).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the test file — it should compile and pass once the handler from Task 4 is in place**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetRunningInvoiceImportJobsHandlerTests" --no-restore`
Expected: 5 tests, all PASS. If any fail, fix the handler — not the tests — unless the test assertion is wrong.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetRunningInvoiceImportJobsHandlerTests.cs
git commit -m "test: cover GetRunningInvoiceImportJobsHandler cache + filter behavior"
```

---

## Task 6: Worker test — `HangfireOptions` is wired into paging calls

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/HangfireBackgroundWorkerTests.cs`

**Why this is narrowly scoped:** Hangfire's `JobStorage.Current` is a process-wide static, and `IStorageConnection` is acquired through it — there is no clean dependency-injection seam to substitute storage in a pure unit test. Rather than introduce a wrapper purely for testability (YAGNI), this task asserts the **wiring** with a small reflection-based check: the worker reads `MaxPendingJobsPageSize` from `HangfireOptions` it was given at construction. The real connection-reuse and page-cap correctness is validated by the existing integration test stack (which uses `UseInMemoryStorage = true`) plus manual verification per Task 8.

- [ ] **Step 1: Write the test file**

Create `backend/test/Anela.Heblo.Tests/Features/Invoices/HangfireBackgroundWorkerTests.cs`:

```csharp
using System.Reflection;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.API.Infrastructure.Hangfire;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class HangfireBackgroundWorkerTests
{
    [Fact]
    public void Constructor_StoresHangfireOptions()
    {
        // Arrange
        var options = Options.Create(new HangfireOptions { MaxPendingJobsPageSize = 200 });

        // Act
        var worker = new HangfireBackgroundWorker(options);

        // Assert — the worker must hold the options so its monitoring calls use the cap.
        var stored = typeof(HangfireBackgroundWorker)
            .GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(worker) as HangfireOptions;

        stored.Should().NotBeNull();
        stored!.MaxPendingJobsPageSize.Should().Be(200);
    }

    [Fact]
    public void Constructor_AcceptsCustomPageSize()
    {
        // Arrange
        var options = Options.Create(new HangfireOptions { MaxPendingJobsPageSize = 50 });

        // Act
        var worker = new HangfireBackgroundWorker(options);

        // Assert
        var stored = typeof(HangfireBackgroundWorker)
            .GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(worker) as HangfireOptions;

        stored!.MaxPendingJobsPageSize.Should().Be(50);
    }
}
```

- [ ] **Step 2: Run the new tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HangfireBackgroundWorkerTests" --no-restore`
Expected: 2 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/HangfireBackgroundWorkerTests.cs
git commit -m "test: assert HangfireBackgroundWorker stores HangfireOptions for paging"
```

---

## Task 7: Full backend validation — build, format, full test pass

- [ ] **Step 1: Format**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: exits 0; any formatter fixes are auto-applied.

- [ ] **Step 2: Build the whole solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded, 0 errors, 0 warnings (or pre-existing warning count only).

- [ ] **Step 3: Run the full test suite (Invoices feature surface and anything that constructs `HangfireBackgroundWorker`)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Invoices|FullyQualifiedName~Hangfire" --no-restore`
Expected: all tests pass. If a previously-green test fails because it manually constructs `HangfireBackgroundWorker()` with no args, update that test's constructor call to:

```csharp
new HangfireBackgroundWorker(Microsoft.Extensions.Options.Options.Create(new Anela.Heblo.API.Extensions.HangfireOptions()))
```

- [ ] **Step 4: Run the full test suite for one final sanity check**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore`
Expected: 100% green.

- [ ] **Step 5: Commit any formatter or test-construction fixups**

```bash
git status
# If there are changes:
git add -A
git commit -m "chore: dotnet format + fix-ups after HangfireBackgroundWorker constructor change"
```

If `git status` is clean, skip the commit.

---

## Task 8: Manual smoke + measurement

This task confirms the perf fix actually lands. Skip only if the environment cannot be exercised.

- [ ] **Step 1: Start the backend locally**

Per `docs/development/setup.md`, run the API. Confirm Hangfire dashboard is reachable (so PostgreSQL storage is wired up correctly with the new options binding).

- [ ] **Step 2: Hit the endpoint twice in quick succession**

Run (replace `<port>` with the local API port from `docs/architecture/environments.md`):

```bash
curl -s -w "first: %{time_total}s\n" -o /dev/null http://localhost:<port>/Invoices/GetRunningInvoiceImportJobs
curl -s -w "second: %{time_total}s\n" -o /dev/null http://localhost:<port>/Invoices/GetRunningInvoiceImportJobs
```

Expected:
- First call returns within target latency (well under 1 s under normal load; well under 3 s even with backlog at the page cap).
- Second call (within 2 s) returns from cache — should be < 50 ms.

- [ ] **Step 3: Verify cache TTL releases after 2 seconds**

Run:

```bash
curl -s -w "first: %{time_total}s\n" -o /dev/null http://localhost:<port>/Invoices/GetRunningInvoiceImportJobs
sleep 3
curl -s -w "after-ttl: %{time_total}s\n" -o /dev/null http://localhost:<port>/Invoices/GetRunningInvoiceImportJobs
```

Expected: the second call (`after-ttl`) is slower than a fresh cache hit — it has re-queried Hangfire after the TTL expired. Both should still be well under 3 s.

- [ ] **Step 4: Verify telemetry still reports the endpoint**

The slow-request telemetry path must still be observing this endpoint (per FR-4). Confirm the new average is below 3,000 ms and ideally below 1,000 ms.

- [ ] **Step 5: (No commit — this is a verification step)**

If everything checks out, proceed to Task 9.

---

## Task 9: Push and open PR

- [ ] **Step 1: Push the branch**

```bash
git push -u origin feat-get-invoices-getrunninginvoiceimportjobs
```

- [ ] **Step 2: Open the PR**

```bash
gh pr create --title "perf(invoices): speed up GetRunningInvoiceImportJobs" --body "$(cat <<'EOF'
## Summary

- Reuse one Hangfire `IStorageConnection` per call inside `HangfireBackgroundWorker.GetRunningJobs()` and `GetPendingJobs()`; `GetJobDisplayName` now takes the caller's connection instead of opening its own. Removes the ~2N connection-acquire pattern that pushed this endpoint past the 3,000 ms slow-request threshold.
- Cap `ProcessingJobs`, `EnqueuedJobs`, and `ScheduledJobs` paging at `HangfireOptions.MaxPendingJobsPageSize` (default 200) instead of `int.MaxValue`, so latency no longer scales linearly with backlog depth.
- Wrap `GetRunningInvoiceImportJobsHandler` with `IMemoryCache` on a fixed key (`"invoices:running-import-jobs"`), TTL = `HangfireOptions.RunningJobsCacheSeconds` (default 2 s). Set to `0` to disable.
- Two config keys (`Hangfire:MaxPendingJobsPageSize`, `Hangfire:RunningJobsCacheSeconds`) added with sensible defaults — no `appsettings.json` change required for deployment.
- `IBackgroundWorker` public contract and HTTP response shape are unchanged. No frontend changes, no DB migrations, no new NuGet packages.

## Follow-ups (out of scope for this PR)

- `HangfireBackgroundWorker.GetJobStartedAt` still calls `ProcessingJobs(0, int.MaxValue)`. Same risk class; different code path (used by `GetJobById`, not implicated in the current slow-request telemetry). Worth a follow-up issue.

## Test plan

- [ ] `dotnet build backend/Anela.Heblo.sln` — green
- [ ] `dotnet format backend/Anela.Heblo.sln` — clean
- [ ] `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — 100% green, including the new `GetRunningInvoiceImportJobsHandlerTests` and `HangfireBackgroundWorkerTests`
- [ ] Manual: two back-to-back curls of `GET /Invoices/GetRunningInvoiceImportJobs` against a local API show the second call returns from cache (< 50 ms)
- [ ] Manual: after a 3-second pause, the call re-queries Hangfire and still completes well under the 3 s threshold
- [ ] Telemetry for the endpoint continues to be reported (no logging silenced) and average drops below 3,000 ms
EOF
)"
```

Expected: PR URL printed.

---

## Self-Review (post-write)

**Spec coverage:**
- FR-1 (single connection in `GetRunningJobs`) → Task 3.
- FR-2 (cap page size in `GetPendingJobs`) → Task 3 (and additionally `ProcessingJobs` per arch-review §"Specification Amendments" item 1).
- FR-3 (short-lived `IMemoryCache`, default 2 s, disable-by-zero) → Task 4.
- FR-4 (telemetry preserved, fix measurable) → Task 8 manual measurement; no telemetry/logging is removed in any code change.
- NFR-1 perf targets → validated in Task 8.
- NFR-2 (contract unchanged, DTOs remain classes) → `BackgroundJobInfo` untouched; `HangfireOptions` remains a class; HTTP route untouched.
- NFR-3 (no auth change) → no controller or auth code touched.
- NFR-4 (bounded memory) → single cache entry, 2 s TTL, absolute expiration.
- NFR-5 (named constants, small focused methods) → `CacheKey` constant, options properties named, no magic numbers introduced.

**Placeholder scan:** No "TBD", "implement later", or unreferenced types. Every code block compiles as written assuming the preceding tasks are in place. The one conditional branch (Step 0 of Task 4 — relocate `HangfireOptions` if Application can't reach API) has concrete commands for both outcomes.

**Type/name consistency:**
- `HangfireOptions.MaxPendingJobsPageSize` and `HangfireOptions.RunningJobsCacheSeconds` — same names in Tasks 1, 3, 4, 5, 6.
- `GetJobDisplayName(IStorageConnection connection, string jobId, Job job)` — same 3-arg signature everywhere it's invoked (Task 3, lines for `GetPendingJobs`, `GetRunningJobs`, `GetJobById`).
- `CacheKey = "invoices:running-import-jobs"` — same literal in Task 4 handler, Task 5 tests.
- `IList<BackgroundJobInfo>` used consistently as the cache value type and handler return type.
- `BackgroundJobInfo` properties used in tests (`Id`, `JobName`, `State`, `Queue`) match the DTO in `Anela.Heblo.Xcc/Services/BackgroundJobInfo.cs`.

Plan is internally consistent and ready for execution.
