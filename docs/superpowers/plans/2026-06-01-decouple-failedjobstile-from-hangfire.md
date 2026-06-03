# Decouple FailedJobsTile from Hangfire Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `FailedJobsTile`'s Hangfire dependency behind an Application-owned `IFailedJobCounter` abstraction so the Application layer no longer compiles against `Hangfire.JobStorage` for the tile.

**Architecture:** Mirrors the existing `IHangfireJobEnqueuer` / `IHangfireRecurringJobScheduler` pattern. Application owns the contract (`Anela.Heblo.Application/Features/BackgroundJobs/Services/IFailedJobCounter.cs`); a sealed adapter (`Anela.Heblo.API/Infrastructure/Hangfire/HangfireFailedJobCounter.cs`) implements it; the binding is registered in `AddHangfireServices` alongside the sibling adapters. The tile's JSON envelope, drill-down URL, metadata, and tile-shaped error handling are preserved byte-identical.

**Tech Stack:** .NET 8, C# 12, xUnit, Moq, FluentAssertions, Hangfire 1.8.x, Microsoft.Extensions.DependencyInjection.

---

## File Structure

**Create:**
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IFailedJobCounter.cs` — Application-layer contract returning the failed-job count.
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireFailedJobCounter.cs` — sealed Hangfire adapter implementing the contract.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireFailedJobCounterTests.cs` — adapter unit tests (placed next to `HangfireJobEnqueuerTests.cs` per arch-review Amendment #2).

**Modify:**
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs` — drop `using Hangfire;`, swap `JobStorage` for `IFailedJobCounter`, convert `LoadDataAsync` to `async`.
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — add one DI registration line (~line 346) in the existing adapter block inside `AddHangfireServices`.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs` — replace `Mock<JobStorage>` + `Mock<IMonitoringApi>` with `Mock<IFailedJobCounter>`; rename the propagation test per arch-review Amendment #3.

**Out of scope (do not touch):**
- `HangfireJobRegistrationHelper.cs` (tracked separately).
- The `Hangfire.Core` `<PackageReference>` in `Anela.Heblo.Application.csproj` (six other Application files still use it; arch-review Amendment #1 tightens FR-3 to "does not gain a new reference").
- Tile metadata, drill-down URL `/hangfire/jobs/failed`, `metadata.source = "Hangfire"` string.

---

## Task 1: Add the `IFailedJobCounter` Application contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IFailedJobCounter.cs`

- [ ] **Step 1: Create the interface file**

Write the following file:

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Abstraction over the background-job system that returns the current number of failed jobs.
/// Implemented by an infrastructure adapter (e.g. Hangfire) registered in the API project.
/// </summary>
public interface IFailedJobCounter
{
    /// <summary>
    /// Returns the current count of failed background jobs.
    /// </summary>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>Number of failed jobs.</returns>
    Task<long> GetFailedCountAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Verify the file contains no Hangfire reference**

Run:

```bash
grep -nE "Hangfire" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IFailedJobCounter.cs
```

Expected: no matches (exit code 1 / empty output).

- [ ] **Step 3: Build the Application project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IFailedJobCounter.cs
git commit -m "feat(background-jobs): add IFailedJobCounter abstraction"
```

---

## Task 2: Rewrite `FailedJobsTileTests` to mock the new abstraction (RED)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs`

The test file currently mocks `JobStorage` + `IMonitoringApi`. After this task it will mock `IFailedJobCounter` and reference no Hangfire types. The four existing scenarios are preserved; the propagation test is renamed `LoadDataAsync_CounterThrows_ReturnsErrorAndDoesNotPropagate` per arch-review Amendment #3.

- [ ] **Step 1: Replace the entire test file**

Overwrite `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs` with:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs.DashboardTiles;

public sealed class FailedJobsTileTests
{
    private readonly Mock<IFailedJobCounter> _counterMock = new();
    private readonly FailedJobsTile _tile;

    public FailedJobsTileTests()
    {
        _tile = new FailedJobsTile(_counterMock.Object, NullLogger<FailedJobsTile>.Instance);
    }

    [Fact]
    public async Task LoadDataAsync_ZeroFailures_ReturnsSuccessWithCountZero()
    {
        _counterMock
            .Setup(c => c.GetFailedCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt64().Should().Be(0L);
        doc.RootElement.GetProperty("drillDown").GetProperty("url").GetString()
            .Should().Be("/hangfire/jobs/failed");
        doc.RootElement.GetProperty("drillDown").GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task LoadDataAsync_PositiveFailureCount_ReturnsSuccessWithCount()
    {
        _counterMock
            .Setup(c => c.GetFailedCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(7L);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt64().Should().Be(7L);
    }

    [Fact]
    public async Task LoadDataAsync_CounterThrows_ReturnsErrorAndDoesNotPropagate()
    {
        _counterMock
            .Setup(c => c.GetFailedCountAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("error").GetString().Should().Be("Failed to retrieve job count. See server logs.");
        doc.RootElement.GetProperty("drillDown").GetProperty("url").GetString()
            .Should().Be("/hangfire/jobs/failed");
        doc.RootElement.GetProperty("drillDown").GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void TileMetadata_MatchesSpec()
    {
        _tile.Title.Should().Be("Failed background jobs");
        _tile.Description.Should().Be("Hangfire jobs in the failed queue");
        _tile.Size.Should().Be(TileSize.Small);
        _tile.Category.Should().Be(TileCategory.System);
        _tile.DefaultEnabled.Should().BeTrue();
        _tile.AutoShow.Should().BeFalse();
        _tile.RequiredPermissions.Should().BeEmpty();
    }

    private static JsonDocument ToJsonDoc(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload));
}
```

- [ ] **Step 2: Verify no `using Hangfire*` directives remain in the file**

Run:

```bash
grep -nE "^using Hangfire" backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs
```

Expected: no matches.

- [ ] **Step 3: Build the test project and observe the failing compile**

Run:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: BUILD FAILS with a CS1503/CS1729 error about `FailedJobsTile`'s constructor not accepting `IFailedJobCounter` (the tile still takes `JobStorage`). This is the RED step — the test cannot compile until Task 3 lands.

- [ ] **Step 4: Do not commit yet**

The repo is in an intentionally broken state between Task 2 and Task 3. Task 3 will fix it and a single commit will cover both files.

---

## Task 3: Refactor `FailedJobsTile` to depend on `IFailedJobCounter` (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs`

- [ ] **Step 1: Overwrite the tile file**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs` with:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;

public sealed class FailedJobsTile : ITile
{
    private const string FailedJobsUrl = "/hangfire/jobs/failed";

    private readonly IFailedJobCounter _failedJobCounter;
    private readonly ILogger<FailedJobsTile> _logger;

    public string Title => "Failed background jobs";
    public string Description => "Hangfire jobs in the failed queue";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.System;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public FailedJobsTile(IFailedJobCounter failedJobCounter, ILogger<FailedJobsTile> logger)
    {
        _failedJobCounter = failedJobCounter;
        _logger = logger;
    }

    public async Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var failedCount = await _failedJobCounter.GetFailedCountAsync(cancellationToken);

            return new
            {
                status = "success",
                data = new { count = failedCount },
                metadata = new { lastUpdated = DateTime.UtcNow, source = "Hangfire" },
                drillDown = new
                {
                    url = FailedJobsUrl,
                    enabled = true,
                    tooltip = "Open Hangfire failed jobs"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Hangfire failed job count");

            return new
            {
                status = "error",
                data = (object?)null,
                error = "Failed to retrieve job count. See server logs.",
                drillDown = new
                {
                    url = FailedJobsUrl,
                    enabled = true,
                    tooltip = "Open Hangfire failed jobs"
                }
            };
        }
    }
}
```

Note: `LoadDataAsync` is now `async Task<object>` — it `await`s the counter and returns the anonymous object directly (no more `Task.FromResult`). The literal strings, drill-down URL, metadata source, and tile metadata properties are byte-identical to the previous version per FR-4 and NFR-4.

- [ ] **Step 2: Confirm no Hangfire reference remains in the dashboard-tile folder**

Run:

```bash
grep -rnE "Hangfire" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/
```

Expected: no matches.

- [ ] **Step 3: Build the Application project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds with no errors.

- [ ] **Step 4: Build the test project and run the four tile tests (GREEN)**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~FailedJobsTileTests"
```

Expected: 4 tests pass — `LoadDataAsync_ZeroFailures_ReturnsSuccessWithCountZero`, `LoadDataAsync_PositiveFailureCount_ReturnsSuccessWithCount`, `LoadDataAsync_CounterThrows_ReturnsErrorAndDoesNotPropagate`, `TileMetadata_MatchesSpec`.

- [ ] **Step 5: Run `dotnet format` on the touched files**

Run:

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj \
  --include backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs \
            backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IFailedJobCounter.cs
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --include backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs
```

Expected: no diagnostics reported / formatting clean.

- [ ] **Step 6: Commit (covers Tasks 2 and 3 together)**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs
git commit -m "refactor(background-jobs): make FailedJobsTile depend on IFailedJobCounter"
```

---

## Task 4: Write `HangfireFailedJobCounterTests` (RED)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireFailedJobCounterTests.cs`

Two scenarios, mirroring FR-7 and arch-review Decision #4: (1) happy-path returns the count from `JobStorage.GetMonitoringApi().FailedCount()`; (2) exceptions thrown by `FailedCount()` propagate unchanged out of `GetFailedCountAsync` (no swallowing in the adapter — error envelope is the tile's job).

- [ ] **Step 1: Create the test file**

Write `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireFailedJobCounterTests.cs`:

```csharp
using Anela.Heblo.API.Infrastructure.Hangfire;
using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

/// <summary>
/// Unit tests for HangfireFailedJobCounter — the only place in the codebase where mocking
/// Hangfire types remains, since this adapter IS the Hangfire seam.
/// </summary>
public sealed class HangfireFailedJobCounterTests
{
    private readonly Mock<JobStorage> _storageMock = new();
    private readonly Mock<IMonitoringApi> _monitoringApiMock = new();
    private readonly HangfireFailedJobCounter _counter;

    public HangfireFailedJobCounterTests()
    {
        _storageMock.Setup(s => s.GetMonitoringApi()).Returns(_monitoringApiMock.Object);
        _counter = new HangfireFailedJobCounter(_storageMock.Object);
    }

    [Fact]
    public async Task GetFailedCountAsync_ReturnsValueFromMonitoringApi()
    {
        _monitoringApiMock.Setup(a => a.FailedCount()).Returns(42L);

        var count = await _counter.GetFailedCountAsync();

        count.Should().Be(42L);
    }

    [Fact]
    public async Task GetFailedCountAsync_WhenMonitoringApiThrows_PropagatesException()
    {
        _monitoringApiMock
            .Setup(a => a.FailedCount())
            .Throws(new InvalidOperationException("storage unavailable"));

        var act = async () => await _counter.GetFailedCountAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("storage unavailable");
    }

    [Fact]
    public void Constructor_WithNullJobStorage_ThrowsArgumentNullException()
    {
        Action act = () => _ = new HangfireFailedJobCounter(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("jobStorage");
    }
}
```

- [ ] **Step 2: Build the test project and observe the failing compile**

Run:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: BUILD FAILS with a CS0246 error — `HangfireFailedJobCounter` does not exist yet. This is the RED step.

- [ ] **Step 3: Do not commit yet**

Same as Task 2: Task 5 lands the production code that turns this green; commit covers both files.

---

## Task 5: Implement `HangfireFailedJobCounter` adapter (GREEN)

**Files:**
- Create: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireFailedJobCounter.cs`

- [ ] **Step 1: Create the adapter file**

Write `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireFailedJobCounter.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Hangfire;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

/// <summary>
/// Hangfire-backed implementation of <see cref="IFailedJobCounter"/>.
/// Queries the Hangfire monitoring API for the current failed-job count.
/// </summary>
public sealed class HangfireFailedJobCounter : IFailedJobCounter
{
    private readonly JobStorage _jobStorage;

    public HangfireFailedJobCounter(JobStorage jobStorage)
    {
        _jobStorage = jobStorage ?? throw new ArgumentNullException(nameof(jobStorage));
    }

    public Task<long> GetFailedCountAsync(CancellationToken cancellationToken = default)
    {
        // Hangfire's FailedCount() is synchronous; token accepted for interface conformance.
        var count = _jobStorage.GetMonitoringApi().FailedCount();
        return Task.FromResult(count);
    }
}
```

The class is `sealed` per FR-2 and arch-review Decision #5. The one-line comment about the cancellation token is per arch-review Amendment #4 (a future reader will otherwise look for token propagation that does not exist). Exceptions from `FailedCount()` propagate per arch-review Decision #4 — the tile owns the presentation/envelope concern.

- [ ] **Step 2: Build the API project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: build succeeds with no errors.

- [ ] **Step 3: Run the adapter tests (GREEN)**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~HangfireFailedJobCounterTests"
```

Expected: 3 tests pass — `GetFailedCountAsync_ReturnsValueFromMonitoringApi`, `GetFailedCountAsync_WhenMonitoringApiThrows_PropagatesException`, `Constructor_WithNullJobStorage_ThrowsArgumentNullException`.

- [ ] **Step 4: Run `dotnet format` on the touched files**

Run:

```bash
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
  --include backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireFailedJobCounter.cs
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --include backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireFailedJobCounterTests.cs
```

Expected: no diagnostics reported.

- [ ] **Step 5: Commit (covers Tasks 4 and 5 together)**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireFailedJobCounter.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireFailedJobCounterTests.cs
git commit -m "feat(background-jobs): add HangfireFailedJobCounter adapter"
```

---

## Task 6: Register the new binding in `AddHangfireServices`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (around line 345-346, inside the existing adapter-registration block)

Lifetime is `Scoped` per FR-5 / arch-review Decision #3 — consistency with `IHangfireJobEnqueuer`. `JobStorage` is registered as a singleton by Hangfire's own bootstrap (~ServiceCollectionExtensions.cs:311–328), so any lifetime ≥ Scoped is safe.

- [ ] **Step 1: Add the DI line**

Locate the existing block in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`:

```csharp
        // Register Hangfire adapter implementations (interfaces live in Application,
        // concrete types live in API/Infrastructure/Hangfire — relocated to keep the
        // Application project free of Hangfire imports for these specific adapters).
        services.AddScoped<IHangfireJobEnqueuer, HangfireJobEnqueuer>();
        services.AddSingleton<IHangfireRecurringJobScheduler, HangfireRecurringJobScheduler>();
```

Insert a new line immediately after the `IHangfireJobEnqueuer` registration so the result reads:

```csharp
        // Register Hangfire adapter implementations (interfaces live in Application,
        // concrete types live in API/Infrastructure/Hangfire — relocated to keep the
        // Application project free of Hangfire imports for these specific adapters).
        services.AddScoped<IHangfireJobEnqueuer, HangfireJobEnqueuer>();
        services.AddScoped<IFailedJobCounter, HangfireFailedJobCounter>();
        services.AddSingleton<IHangfireRecurringJobScheduler, HangfireRecurringJobScheduler>();
```

No new `using` directive is needed — `Anela.Heblo.API.Infrastructure.Hangfire` is already imported at line 15 and `Anela.Heblo.Application.Features.BackgroundJobs.Services` at line 24 of this file.

- [ ] **Step 2: Build the solution**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds with no errors.

- [ ] **Step 3: Run the full BackgroundJobs test slice**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs"
```

Expected: all BackgroundJobs tests pass — `FailedJobsTileTests` (4), `HangfireFailedJobCounterTests` (3), plus the existing `HangfireJobEnqueuerTests`, `HangfireRecurringJobSchedulerTests`, `HangfireJobRegistrationHelperTests` continue to pass unchanged.

- [ ] **Step 4: Run `dotnet format`**

Run:

```bash
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
  --include backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
```

Expected: no diagnostics reported.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(background-jobs): register HangfireFailedJobCounter in AddHangfireServices"
```

---

## Task 7: Final whole-solution validation

This task is the gate before declaring the feature done. It checks (a) the solution builds, (b) the full test suite is green (no regression in tiles, dashboard, or Hangfire-touching code), (c) the Application csproj did not gain a Hangfire reference, (d) the JSON envelope is byte-identical to today's shape.

- [ ] **Step 1: Full solution build**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds with no errors and no new warnings.

- [ ] **Step 2: Full backend test suite**

Run:

```bash
dotnet test backend/Anela.Heblo.sln
```

Expected: all tests pass. (If any unrelated flake appears, re-run that single test; do not paper over real failures.)

- [ ] **Step 3: Confirm `Anela.Heblo.Application.csproj` did not gain a Hangfire reference (NFR-2 / Amendment #1)**

Run:

```bash
git diff main -- backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: empty diff. The existing `Hangfire.Core 1.8.21` reference stays (six other Application files still need it), but no new `<PackageReference Include="Hangfire.*">` was added by this change.

- [ ] **Step 4: Confirm the dashboard-tile folder is Hangfire-free (FR-3)**

Run:

```bash
grep -rnE "Hangfire" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/
```

Expected: no matches.

- [ ] **Step 5: Confirm the Services folder remains Hangfire-free (NFR-2)**

Run:

```bash
grep -rnE "^using Hangfire" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/
```

Expected: no matches. (The Services folder hosts only abstractions: `IHangfireJobEnqueuer`, `IHangfireRecurringJobScheduler`, and the new `IFailedJobCounter`. The "Hangfire" word appears in two interface *names* — that is intentional and excluded by the `^using` anchor.)

- [ ] **Step 6: Confirm the tile JSON envelope is byte-identical (NFR-4)**

Inspect the diff of `FailedJobsTile.cs`:

```bash
git diff main -- backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs
```

Expected: the only changes are (a) `using` directives swapped, (b) the field/constructor type swapped from `JobStorage` to `IFailedJobCounter`, (c) `LoadDataAsync` signature changed to `async`, (d) the call site swapped from `_jobStorage.GetMonitoringApi().FailedCount()` to `await _failedJobCounter.GetFailedCountAsync(cancellationToken)`, (e) `Task.FromResult<object>(new { … })` simplified to `return new { … }`. **All literal strings — `"success"`, `"error"`, `"Failed to retrieve job count. See server logs."`, `"Failed to load Hangfire failed job count"`, `"Hangfire"`, `"/hangfire/jobs/failed"`, `"Open Hangfire failed jobs"` — are unchanged.** The anonymous-object shape (property names, ordering, value types) is unchanged.

- [ ] **Step 7: Push and conclude**

Run:

```bash
git log --oneline main..HEAD
```

Expected: three commits on the branch from this plan —
1. `feat(background-jobs): add IFailedJobCounter abstraction`
2. `refactor(background-jobs): make FailedJobsTile depend on IFailedJobCounter`
3. `feat(background-jobs): add HangfireFailedJobCounter adapter`
4. `feat(background-jobs): register HangfireFailedJobCounter in AddHangfireServices`

(Four commits, one per task pair / single task. Acceptable to push as-is; squashing is at the PR author's discretion.)

---

## Spec Coverage Matrix

| Spec requirement | Task(s) |
|---|---|
| FR-1 `IFailedJobCounter` in `Application/Features/BackgroundJobs/Services/` with `Task<long> GetFailedCountAsync(CancellationToken)` | Task 1 |
| FR-2 `HangfireFailedJobCounter` sealed adapter in `API/Infrastructure/Hangfire/` wrapping `JobStorage.GetMonitoringApi().FailedCount()`; does not swallow exceptions; CT accepted at API surface | Task 5 |
| FR-3 `FailedJobsTile` refactored — `using Hangfire;` removed, field/ctor swapped, `LoadDataAsync` becomes async, CT propagated | Task 3 |
| FR-4 Error envelope preserved (log message, error string, drill-down block); no exception escapes `LoadDataAsync` | Task 3 (production) + Task 2 (test) |
| FR-5 DI registration `services.AddScoped<IFailedJobCounter, HangfireFailedJobCounter>()` in the existing adapter block | Task 6 |
| FR-6 Tile tests rewritten to mock `IFailedJobCounter`; four scenarios preserved; propagation test renamed per Amendment #3 | Task 2 |
| FR-7 New `HangfireFailedJobCounterTests` placed under `test/Features/BackgroundJobs/` per Amendment #2; covers happy path and exception propagation | Task 4 |
| NFR-1 Performance — single virtual call added; `Task.FromResult` allocation acceptable | Tasks 3, 5 |
| NFR-2 Application/`DashboardTiles/` and Application/`Services/` Hangfire-free | Task 7 Steps 4–5 |
| NFR-3 Tile test no longer references Hangfire | Task 2 |
| NFR-4 JSON envelope byte-identical | Task 7 Step 6 |
| NFR-5 No auth / permission changes | (covered by `TileMetadata_MatchesSpec` in Task 2) |
| Arch-review Amendment #1 — Application.csproj does not gain Hangfire reference | Task 7 Step 3 |
| Arch-review Amendment #2 — adapter test placed next to `HangfireJobEnqueuerTests.cs` | Task 4 |
| Arch-review Amendment #3 — test renamed `LoadDataAsync_CounterThrows_…` | Task 2 |
| Arch-review Amendment #4 — single-line comment on the unused CT in the adapter | Task 5 |
