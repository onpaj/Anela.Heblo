# Decouple Dashboard Tile Drill-Downs From Frontend Routing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace hardcoded frontend URL paths inside three dashboard tile backend handlers with a typed `DashboardTileDrillDown` contract that carries a semantic route key, and add a single frontend route registry + resolver that maps those keys back to React-router paths or backend-mounted admin URLs.

**Architecture:** Backend keeps the existing `Task<object>` tile payload shape, but the `drillDown` field is now a typed `DashboardTileDrillDown` class (not a record) with `RouteKey`, `Enabled`, and optional `Parameters`. Frontend hand-mirrors the type under `frontend/src/components/dashboard/drillDownRoutes.ts` (the tile `data` envelope stays `any`, so OpenAPI codegen does not pick the type up) and exposes a `resolveDrillDown(drillDown)` helper that returns either a `react-router` path or a backend-origin URL (`${apiUrl}${path}`) opened in a new tab. Filter-based drill-down tiles (`PurchaseOrdersInTransitTile`, `LowStockAlertTile`, etc.) keep their existing shape — only the three URL-emitting tiles change.

**Tech Stack:** .NET 8 + xUnit + FluentAssertions + Moq (backend); React 18 + React Router + Jest + @testing-library/react + jest mocks (frontend); no new packages.

---

## File Structure

**New files:**
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs` — typed DTO carrying `RouteKey`, `Enabled`, optional `Parameters`.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs` — new test file; the tile currently has no backend tests.
- `frontend/src/components/dashboard/drillDownRoutes.ts` — exports `DashboardDrillDownRouteKey`, `DrillDownTarget`, `DashboardTileDrillDown`, `DrillDownResolution`, `DASHBOARD_DRILLDOWN_ROUTES`, `resolveDrillDown(...)`.
- `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.ts` — unit tests for the resolver (known react-router, known external, unknown key, undefined input, disabled drill-down).
- `memory/patterns/dashboard-tile-drilldown.md` — short note documenting the two coexisting drill-down shapes and which to use for new tiles.

**Modified files:**
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` — replace both anonymous `drillDown = new { href = "/data-quality", enabled = true }` payloads with `new DashboardTileDrillDown { RouteKey = "dataQuality", Enabled = true }`.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` — remove `DrillDownHref` constant; replace four payloads.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs` — remove `FailedJobsUrl` constant; replace two payloads (keep `tooltip`).
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs` — assertions move from `drillDown.href == "/automation/data-quality"` to `drillDown.routeKey == "dataQuality"`; also assert the *absence* of `href`/`url` keys.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs` — same: `drillDown.routeKey == "hangfireFailedJobs"`, no `href`/`url` keys, `tooltip` preserved.
- `frontend/src/components/dashboard/tiles/DataQualityTile.tsx` — accept `drillDown` on `data`, replace hardcoded `navigate('/automation/data-quality')` with resolver-driven navigation, render non-interactive if resolver returns null.
- `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx` — same; preserve `data-testid="dqt-yesterday-tile"` and the existing branch (error / no_data / running / completed).
- `frontend/src/components/dashboard/tiles/FailedJobsTile.tsx` — remove `HANGFIRE_PATH` constant, drive click via resolver (which already returns the backend-origin URL for `external` strategy).
- `frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx` — pass `drillDown: { routeKey: 'dataQuality', enabled: true }` in the click test; an extra case with unknown key should leave navigation suppressed.
- `frontend/src/components/dashboard/tiles/__tests__/FailedJobsTile.test.tsx` — pass `drillDown: { routeKey: 'hangfireFailedJobs', enabled: true }` in the click test; the asserted URL `http://localhost:5001/hangfire/jobs/failed` stays identical.

**Out of scope for this plan (do not touch):**
- `frontend/src/utils/urlUtils.ts` (`DrillDownInfo`, `createFilteredUrl`, `isTileClickable`, `getTileTooltip`) — filter-based tile pattern stays as-is.
- `PurchaseOrdersInTransitTile`, `LowStockAlertTile`, `BackgroundTasksTile`, `CountTile`, `InventorySummaryTile` (BE + FE) — they already comply with the "frontend owns routing" rule.
- `DashboardTileDto.Data` envelope — stays `object?`.

---

## Task 1: Add the `DashboardTileDrillDown` contract DTO

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs`

This task adds the typed DTO that subsequent backend tasks will reference. It has no behavior of its own — verification is "the solution still builds" — so there is no unit test for the DTO itself. Tile-level tests assert correct usage.

- [ ] **Step 1: Create the DTO class**

Create `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Dashboard.Contracts;

// Plain class (not a record) — DTO serialization rule from CLAUDE.md: OpenAPI client
// generators mishandle record parameter order. Tile payloads use this DTO embedded in
// an anonymous projection because LoadDataAsync returns Task<object>.
public class DashboardTileDrillDown
{
    public string RouteKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public IReadOnlyDictionary<string, string>? Parameters { get; set; }
}
```

- [ ] **Step 2: Verify the solution still compiles**

Run: `dotnet build backend/Anela.Heblo.sln --nologo --verbosity minimal`
Expected: Build succeeds (no test runs yet).

- [ ] **Step 3: Run `dotnet format`**

Run: `dotnet format backend/Anela.Heblo.sln --verbosity minimal`
Expected: No errors. Any whitespace/using fixes applied automatically.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs
git commit -m "feat: add DashboardTileDrillDown contract DTO"
```

---

## Task 2: Refactor `DqtYesterdayStatusTile` (RED → GREEN)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs:32-47` and `:104-136`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs`

This tile is refactored first because it already has thorough backend tests and is the natural template for the others. The pattern is: change the test assertions to the new shape (RED), then change the tile (GREEN).

- [ ] **Step 1: Update the `NoRunCoveringYesterday` test assertions to the new shape**

In `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs`, replace lines 44-46:

```csharp
        doc.RootElement.GetProperty("drillDown").GetProperty("href").GetString()
            .Should().Be("/automation/data-quality");
        doc.RootElement.GetProperty("drillDown").GetProperty("enabled").GetBoolean().Should().BeTrue();
```

with:

```csharp
        var drillDown = doc.RootElement.GetProperty("drillDown");
        drillDown.GetProperty("routeKey").GetString().Should().Be("dataQuality");
        drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
        drillDown.TryGetProperty("href", out _).Should().BeFalse();
        drillDown.TryGetProperty("url", out _).Should().BeFalse();
```

- [ ] **Step 2: Update the `RepositoryThrows` test assertions to the new shape**

In the same file, replace lines 133-135:

```csharp
        doc.RootElement.GetProperty("drillDown").GetProperty("href").GetString()
            .Should().Be("/automation/data-quality");
```

with:

```csharp
        var drillDown = doc.RootElement.GetProperty("drillDown");
        drillDown.GetProperty("routeKey").GetString().Should().Be("dataQuality");
        drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
        drillDown.TryGetProperty("href", out _).Should().BeFalse();
        drillDown.TryGetProperty("url", out _).Should().BeFalse();
```

- [ ] **Step 3: Run the test to verify it fails (RED)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~DqtYesterdayStatusTileTests"`
Expected: Both `NoRunCoveringYesterday_ReturnsNoData` and `RepositoryThrows_ReturnsErrorAndDoesNotPropagate` fail with "JsonElement does not contain a 'routeKey' property".

- [ ] **Step 4: Refactor `DqtYesterdayStatusTile.cs` to emit the new shape**

Replace the entire file `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` with:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.DashboardTiles;

[TileId("dqtyesterdaystatus")]
public class DqtYesterdayStatusTile : ITile
{
    private const string DrillDownRouteKey = "dataQuality";

    private readonly IDqtRunRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DqtYesterdayStatusTile> _logger;

    public string Title => "DQT včera";
    public string Description => "Stav včerejšího DQT testu faktur";
    public TileSize Size => TileSize.Medium;
    public TileCategory Category => TileCategory.DataQuality;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public DqtYesterdayStatusTile(
        IDqtRunRepository repository,
        TimeProvider timeProvider,
        ILogger<DqtYesterdayStatusTile> logger)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var yesterday = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime).AddDays(-1);

        try
        {
            var run = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison,
                yesterday,
                cancellationToken);

            if (run is null)
            {
                return new
                {
                    status = "no_data",
                    data = (object?)null,
                    drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
                };
            }

            var statusStr = run.Status switch
            {
                DqtRunStatus.Failed => "error",
                DqtRunStatus.Running => "warning",
                DqtRunStatus.Completed when run.TotalMismatches > 0 => "warning",
                DqtRunStatus.Completed => "success",
                _ => "error"
            };

            return new
            {
                status = statusStr,
                data = new
                {
                    runId = run.Id,
                    runStatus = run.Status.ToString(),
                    dateFrom = run.DateFrom,
                    dateTo = run.DateTo,
                    totalChecked = run.TotalChecked,
                    totalMismatches = run.TotalMismatches
                },
                drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load yesterday DQT status for {TestType} on {TargetDate}",
                DqtTestType.IssuedInvoiceComparison,
                yesterday);

            return new
            {
                status = "error",
                data = (object?)null,
                drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
            };
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes (GREEN)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~DqtYesterdayStatusTileTests"`
Expected: All seven tests in `DqtYesterdayStatusTileTests` pass.

- [ ] **Step 6: Verify no `/`-leading literal remains in the file**

Run: `grep -nE "\"/" backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs`
Expected: No output. (The `using` directives and namespace must not contain quoted paths.)

- [ ] **Step 7: Format and commit**

```bash
dotnet format backend/Anela.Heblo.sln --verbosity minimal
git add backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs
git commit -m "refactor: emit DashboardTileDrillDown route key from DqtYesterdayStatusTile"
```

---

## Task 3: Refactor `FailedJobsTile` (RED → GREEN)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs:31-37` and `:60-69`
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs`

The Hangfire URL gets a different route key (`hangfireFailedJobs`) because the navigation strategy on the frontend is different (new tab to backend-origin admin UI, not React Router).

- [ ] **Step 1: Update the `ZeroFailures` test assertions**

In `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs`, replace lines 34-36:

```csharp
        doc.RootElement.GetProperty("drillDown").GetProperty("url").GetString()
            .Should().Be("/hangfire/jobs/failed");
        doc.RootElement.GetProperty("drillDown").GetProperty("enabled").GetBoolean().Should().BeTrue();
```

with:

```csharp
        var drillDown = doc.RootElement.GetProperty("drillDown");
        drillDown.GetProperty("routeKey").GetString().Should().Be("hangfireFailedJobs");
        drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
        drillDown.GetProperty("tooltip").GetString().Should().Be("Open Hangfire failed jobs");
        drillDown.TryGetProperty("href", out _).Should().BeFalse();
        drillDown.TryGetProperty("url", out _).Should().BeFalse();
```

- [ ] **Step 2: Update the `CounterThrows` test assertions**

In the same file, replace lines 65-68:

```csharp
        doc.RootElement.GetProperty("drillDown").GetProperty("url").GetString()
            .Should().Be("/hangfire/jobs/failed");
        doc.RootElement.GetProperty("drillDown").GetProperty("enabled").GetBoolean().Should().BeTrue();
```

with:

```csharp
        var drillDown = doc.RootElement.GetProperty("drillDown");
        drillDown.GetProperty("routeKey").GetString().Should().Be("hangfireFailedJobs");
        drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
        drillDown.GetProperty("tooltip").GetString().Should().Be("Open Hangfire failed jobs");
        drillDown.TryGetProperty("href", out _).Should().BeFalse();
        drillDown.TryGetProperty("url", out _).Should().BeFalse();
```

- [ ] **Step 3: Run the test to verify it fails (RED)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~FailedJobsTileTests"`
Expected: `ZeroFailures_ReturnsSuccessWithCountZero` and `CounterThrows_ReturnsErrorAndDoesNotPropagate` fail with "JsonElement does not contain a 'routeKey' property".

- [ ] **Step 4: Refactor `FailedJobsTile.cs` to emit the new shape (preserve `tooltip`)**

Replace the entire file `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs` with:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;

[TileId("failedjobs")]
public sealed class FailedJobsTile : ITile
{
    private const string DrillDownRouteKey = "hangfireFailedJobs";
    private const string DrillDownTooltip = "Open Hangfire failed jobs";

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
                    routeKey = DrillDownRouteKey,
                    enabled = true,
                    tooltip = DrillDownTooltip
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
                    routeKey = DrillDownRouteKey,
                    enabled = true,
                    tooltip = DrillDownTooltip
                }
            };
        }
    }
}
```

> Note: `FailedJobsTile` keeps an anonymous-projection `drillDown` (rather than the strongly-typed `DashboardTileDrillDown` class) because the `tooltip` field is **not** part of `DashboardTileDrillDown`. The serialized JSON has the same property names — that is what the tests assert. The DTO type still appears in scope via the `using` directive, even though it is not instantiated here; remove the unused `using` if the analyzer complains.

Adjust the `using` directives to match what is actually used — the file above does not directly reference `DashboardTileDrillDown`, so the corrected top block is:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;
```

Use the corrected `using` block (without the `Contracts` import).

- [ ] **Step 5: Run the test to verify it passes (GREEN)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~FailedJobsTileTests"`
Expected: All four tests in `FailedJobsTileTests` pass.

- [ ] **Step 6: Verify no `/`-leading literal remains in the file**

Run: `grep -nE "\"/" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs`
Expected: No output.

- [ ] **Step 7: Format and commit**

```bash
dotnet format backend/Anela.Heblo.sln --verbosity minimal
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs
git commit -m "refactor: emit DashboardTileDrillDown route key from FailedJobsTile"
```

---

## Task 4: Add tests for `DataQualityStatusTile` and refactor it (RED → GREEN)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs`

`DataQualityStatusTile` has no backend tests today. Per arch-review §7 we add a small test file to keep coverage symmetric with `DqtYesterdayStatusTile`, then refactor the tile.

- [ ] **Step 1: Write the test file**

Create `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.DataQuality.DashboardTiles;

public class DataQualityStatusTileTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly DataQualityStatusTile _tile;

    public DataQualityStatusTileTests()
    {
        _tile = new DataQualityStatusTile(_repositoryMock.Object);
    }

    [Fact]
    public async Task LoadDataAsync_NoRun_ReturnsNoDataWithRouteKey()
    {
        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAsync(
                DqtTestType.IssuedInvoiceComparison, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun?)null);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("no_data");
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);

        var drillDown = doc.RootElement.GetProperty("drillDown");
        drillDown.GetProperty("routeKey").GetString().Should().Be("dataQuality");
        drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
        drillDown.TryGetProperty("href", out _).Should().BeFalse();
        drillDown.TryGetProperty("url", out _).Should().BeFalse();
    }

    [Fact]
    public async Task LoadDataAsync_RunWithoutMismatches_ReturnsSuccessWithRouteKey()
    {
        var run = CreateCompletedRun(totalChecked: 50, totalMismatches: 0);

        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAsync(
                DqtTestType.IssuedInvoiceComparison, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("drillDown").GetProperty("routeKey").GetString().Should().Be("dataQuality");
    }

    [Fact]
    public async Task LoadDataAsync_RepositoryThrows_ReturnsErrorWithRouteKey()
    {
        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAsync(
                DqtTestType.IssuedInvoiceComparison, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        var drillDown = doc.RootElement.GetProperty("drillDown");
        drillDown.GetProperty("routeKey").GetString().Should().Be("dataQuality");
        drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    private static DqtRun CreateCompletedRun(int totalChecked, int totalMismatches)
    {
        var date = new DateOnly(2026, 5, 5);
        var run = DqtRun.Start(
            DqtTestType.IssuedInvoiceComparison,
            date,
            date,
            DqtTriggerType.Scheduled);
        run.Complete(totalChecked, totalMismatches);
        return run;
    }

    private static JsonDocument ToJsonDoc(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload));
}
```

- [ ] **Step 2: Run the test to verify it fails (RED)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~DataQualityStatusTileTests"`
Expected: All three tests fail with "JsonElement does not contain a 'routeKey' property" (current tile emits `href`).

- [ ] **Step 3: Refactor `DataQualityStatusTile.cs` to emit the new shape**

Replace the entire file `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` with:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.DataQuality.DashboardTiles;

[TileId("dataqualitystatus")]
public class DataQualityStatusTile : ITile
{
    private const string DrillDownRouteKey = "dataQuality";

    private readonly IDqtRunRepository _repository;

    public string Title => "Kvalita dat";
    public string Description => "Stav posledního DQT testu faktur";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.DataQuality;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public DataQualityStatusTile(IDqtRunRepository repository)
    {
        _repository = repository;
    }

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var run = await _repository.GetLatestByTestTypeAsync(DqtTestType.IssuedInvoiceComparison, cancellationToken);

            if (run is null)
            {
                return new
                {
                    status = "no_data",
                    data = (object?)null,
                    drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
                };
            }

            var statusStr = run.Status == DqtRunStatus.Failed
                ? "error"
                : run.TotalMismatches > 0
                    ? "warning"
                    : "success";

            return new
            {
                status = statusStr,
                data = new
                {
                    runId = run.Id,
                    runStatus = run.Status.ToString(),
                    dateFrom = run.DateFrom,
                    dateTo = run.DateTo,
                    totalChecked = run.TotalChecked,
                    totalMismatches = run.TotalMismatches
                },
                drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
            };
        }
        catch
        {
            return new
            {
                status = "error",
                data = (object?)null,
                drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
            };
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes (GREEN)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~DataQualityStatusTileTests"`
Expected: All three tests pass.

- [ ] **Step 5: Verify no `/`-leading literal remains in the file**

Run: `grep -nE "\"/" backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs`
Expected: No output.

- [ ] **Step 6: Run the full backend test suite to confirm no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore --nologo`
Expected: All tests pass.

- [ ] **Step 7: Format and commit**

```bash
dotnet format backend/Anela.Heblo.sln --verbosity minimal
git add backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs
git commit -m "refactor: emit DashboardTileDrillDown route key from DataQualityStatusTile"
```

---

## Task 5: Audit remaining backend tiles for forbidden URL literals

**Files:**
- Read-only: `backend/src/Anela.Heblo.Application/Features/**/DashboardTiles/*.cs`

This is a quick audit so the codebase enters the post-refactor state with **zero** backend tiles emitting URL strings. The spec ("Out of Scope") says we do not introduce route keys for tiles that have no drill-down today — only verify none of them slipped a URL in.

- [ ] **Step 1: Search every tile file for URL-looking literals**

Run:

```bash
grep -rn -E "\"/(data-quality|hangfire|automation|logistics|manufacturing|purchase|finance|terminal|kpi|expedition)" backend/src/Anela.Heblo.Application
```

Expected: No matches under any `*/DashboardTiles/*.cs`. Filter-based tiles (`PurchaseOrdersInTransitTile`, `LowStockAlertTile`, `BackgroundTasksTile`, `CountTile`, `InventorySummaryTile`) and other production code may still have unrelated string literals — only `DashboardTiles/` directories matter for this audit.

- [ ] **Step 2: If a forbidden literal is found, stop and escalate**

If `grep` returns anything under `*/DashboardTiles/*.cs`, the implementer should not silently add new route keys — that would expand scope. Add a note in the PR description and stop the task for human review.

- [ ] **Step 3: No commit needed**

Audit task has no edits when it passes; nothing to commit.

---

## Task 6: Add frontend route registry and `resolveDrillDown` helper (RED → GREEN)

**Files:**
- Create: `frontend/src/components/dashboard/drillDownRoutes.ts`
- Create: `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.ts`

TDD: the resolver test specifies the contract (known react-router key, known external key, unknown key warns + returns null, disabled drill-down returns null, undefined drill-down returns null). Then we write the resolver. The external-strategy URL must equal `${apiUrl}${path}` so the existing `FailedJobsTile` test assertion (`http://localhost:5001/hangfire/jobs/failed`) survives.

- [ ] **Step 1: Write the failing resolver test**

Create `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.ts`:

```typescript
import {
  DASHBOARD_DRILLDOWN_ROUTES,
  resolveDrillDown,
} from '../drillDownRoutes';

jest.mock('../../../config/runtimeConfig', () => ({
  getConfig: () => ({ apiUrl: 'http://localhost:5001' }),
}));

describe('drillDownRoutes', () => {
  const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

  beforeEach(() => {
    warnSpy.mockClear();
  });

  afterAll(() => {
    warnSpy.mockRestore();
  });

  it('registry contains the known route keys', () => {
    expect(DASHBOARD_DRILLDOWN_ROUTES.dataQuality).toEqual({
      type: 'react-router',
      path: '/automation/data-quality',
    });
    expect(DASHBOARD_DRILLDOWN_ROUTES.hangfireFailedJobs).toEqual({
      type: 'external',
      path: '/hangfire/jobs/failed',
    });
  });

  it('resolves a react-router key to the registered path with strategy "react-router"', () => {
    const result = resolveDrillDown({ routeKey: 'dataQuality', enabled: true });

    expect(result).toEqual({
      url: '/automation/data-quality',
      strategy: 'react-router',
    });
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('resolves an external key by prepending apiUrl and strategy "external"', () => {
    const result = resolveDrillDown({ routeKey: 'hangfireFailedJobs', enabled: true });

    expect(result).toEqual({
      url: 'http://localhost:5001/hangfire/jobs/failed',
      strategy: 'external',
    });
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('returns null and warns for an unknown route key', () => {
    const result = resolveDrillDown({ routeKey: 'someUnknownKey', enabled: true });

    expect(result).toBeNull();
    expect(warnSpy).toHaveBeenCalledWith(
      expect.stringContaining('someUnknownKey'),
    );
  });

  it('returns null when the drill-down is disabled', () => {
    const result = resolveDrillDown({ routeKey: 'dataQuality', enabled: false });

    expect(result).toBeNull();
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('returns null when the drill-down is undefined', () => {
    const result = resolveDrillDown(undefined);

    expect(result).toBeNull();
    expect(warnSpy).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails (RED)**

Run: `cd frontend && npx jest src/components/dashboard/__tests__/drillDownRoutes.test.ts`
Expected: All tests fail with "Cannot find module '../drillDownRoutes'".

- [ ] **Step 3: Implement the resolver**

Create `frontend/src/components/dashboard/drillDownRoutes.ts`:

```typescript
import { getConfig } from '../../config/runtimeConfig';

export type DashboardDrillDownRouteKey = 'dataQuality' | 'hangfireFailedJobs';

export type DrillDownTarget =
  | { type: 'react-router'; path: string }
  | { type: 'external'; path: string };

export interface DashboardTileDrillDown {
  routeKey: string;
  enabled: boolean;
  parameters?: Record<string, string>;
}

export interface DrillDownResolution {
  url: string;
  strategy: DrillDownTarget['type'];
}

// Closed set: adding a backend route key without a frontend entry is a build error
// at the consuming tile component (the union type narrows away unknowns).
export const DASHBOARD_DRILLDOWN_ROUTES: Record<DashboardDrillDownRouteKey, DrillDownTarget> = {
  dataQuality: { type: 'react-router', path: '/automation/data-quality' },
  hangfireFailedJobs: { type: 'external', path: '/hangfire/jobs/failed' },
};

const isKnownRouteKey = (key: string): key is DashboardDrillDownRouteKey =>
  Object.prototype.hasOwnProperty.call(DASHBOARD_DRILLDOWN_ROUTES, key);

export function resolveDrillDown(
  drillDown: DashboardTileDrillDown | undefined,
): DrillDownResolution | null {
  if (!drillDown || !drillDown.enabled) {
    return null;
  }

  if (!isKnownRouteKey(drillDown.routeKey)) {
    // Backend deployed ahead of frontend: leave the tile visible but non-interactive.
    console.warn(`[dashboard] Unknown drill-down route key: ${drillDown.routeKey}`);
    return null;
  }

  const target = DASHBOARD_DRILLDOWN_ROUTES[drillDown.routeKey];

  if (target.type === 'external') {
    const { apiUrl } = getConfig();
    return { url: `${apiUrl}${target.path}`, strategy: 'external' };
  }

  return { url: target.path, strategy: 'react-router' };
}
```

- [ ] **Step 4: Run the test to verify it passes (GREEN)**

Run: `cd frontend && npx jest src/components/dashboard/__tests__/drillDownRoutes.test.ts`
Expected: All six tests pass.

- [ ] **Step 5: Run TypeScript build to confirm types are clean**

Run: `cd frontend && npm run build`
Expected: Build succeeds. (Build includes `tsc --noEmit` via CRA's `react-scripts build`; if your local script differs use `npx tsc --noEmit` instead.)

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/dashboard/drillDownRoutes.ts \
        frontend/src/components/dashboard/__tests__/drillDownRoutes.test.ts
git commit -m "feat: add dashboard drill-down route registry and resolver"
```

---

## Task 7: Update `FailedJobsTile.tsx` to use the resolver (RED → GREEN)

**Files:**
- Modify: `frontend/src/components/dashboard/tiles/FailedJobsTile.tsx`
- Modify: `frontend/src/components/dashboard/tiles/__tests__/FailedJobsTile.test.tsx`

The existing component opens `${apiUrl}/hangfire/jobs/failed` in a new tab regardless of payload contents. After this task, navigation is driven by the resolver and the test passes a `drillDown` payload. The asserted URL stays identical.

- [ ] **Step 1: Update the test to pass the new `drillDown` payload and add a "no drill-down" case**

In `frontend/src/components/dashboard/tiles/__tests__/FailedJobsTile.test.tsx`, replace the entire file with:

```typescript
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FailedJobsTile } from '../FailedJobsTile';

jest.mock('../../../../config/runtimeConfig', () => ({
  getConfig: () => ({ apiUrl: 'http://localhost:5001' }),
}));

const mockWindowOpen = jest.fn();
beforeAll(() => {
  window.open = mockWindowOpen;
});
beforeEach(() => {
  mockWindowOpen.mockReset();
});

const drillDown = { routeKey: 'hangfireFailedJobs', enabled: true };

describe('FailedJobsTile', () => {
  it('renders error state without a clickable wrapper', () => {
    render(<FailedJobsTile data={{ status: 'error', error: 'storage unavailable', drillDown }} />);

    expect(screen.getByText('Unavailable')).toBeInTheDocument();
    expect(screen.queryByTestId('failed-jobs-tile')).toBeNull();
  });

  it('renders count 0 with red styling', () => {
    render(<FailedJobsTile data={{ status: 'success', data: { count: 0 }, drillDown }} />);

    expect(screen.getByTestId('failed-jobs-tile')).toBeInTheDocument();
    expect(screen.getByText('0')).toBeInTheDocument();
    expect(screen.getByText('failed jobs')).toBeInTheDocument();
  });

  it('renders non-zero count', () => {
    render(<FailedJobsTile data={{ status: 'success', data: { count: 12 }, drillDown }} />);

    expect(screen.getByText('12')).toBeInTheDocument();
    expect(screen.getByText('failed jobs')).toBeInTheDocument();
  });

  it('opens Hangfire failed jobs page in a new tab on click', () => {
    render(<FailedJobsTile data={{ status: 'success', data: { count: 3 }, drillDown }} />);

    fireEvent.click(screen.getByTestId('failed-jobs-tile'));

    expect(mockWindowOpen).toHaveBeenCalledWith(
      'http://localhost:5001/hangfire/jobs/failed',
      '_blank',
    );
  });

  it('does not open a window when the drill-down is missing', () => {
    render(<FailedJobsTile data={{ status: 'success', data: { count: 3 } }} />);

    fireEvent.click(screen.getByTestId('failed-jobs-tile'));

    expect(mockWindowOpen).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails (RED)**

Run: `cd frontend && npx jest src/components/dashboard/tiles/__tests__/FailedJobsTile.test.tsx`
Expected: The "no drill-down" case fails (current component always opens the window).

- [ ] **Step 3: Update `FailedJobsTile.tsx` to use the resolver**

Replace `frontend/src/components/dashboard/tiles/FailedJobsTile.tsx` with:

```typescript
import React from 'react';
import { AlertTriangle, XCircle } from 'lucide-react';
import {
  DashboardTileDrillDown,
  resolveDrillDown,
} from '../drillDownRoutes';

interface FailedJobsTileProps {
  data: {
    status?: string;
    data?: {
      count?: number;
    };
    error?: string;
    drillDown?: DashboardTileDrillDown;
  };
}

export const FailedJobsTile: React.FC<FailedJobsTileProps> = ({ data }) => {
  const resolution = resolveDrillDown(data.drillDown);

  const handleClick = () => {
    if (!resolution) {
      return;
    }
    if (resolution.strategy === 'external') {
      window.open(resolution.url, '_blank');
    }
    // react-router strategy is not used by this tile; if added later, route through useNavigate.
  };

  if (data.status === 'error') {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <div>
          <XCircle className="h-10 w-10 text-red-500 mx-auto mb-2" />
          <p className="text-red-600 text-sm">Unavailable</p>
        </div>
      </div>
    );
  }

  const count = data.data?.count ?? 0;

  return (
    <div
      data-testid="failed-jobs-tile"
      className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
      onClick={handleClick}
      style={{ touchAction: 'manipulation' }}
    >
      <div className="mb-2 text-red-600">
        <AlertTriangle className="h-10 w-10" />
      </div>
      <div className="text-3xl font-bold mb-1 text-red-700">
        {count}
      </div>
      <div className="text-sm text-gray-500">
        failed jobs
      </div>
    </div>
  );
};
```

- [ ] **Step 4: Run the test to verify it passes (GREEN)**

Run: `cd frontend && npx jest src/components/dashboard/tiles/__tests__/FailedJobsTile.test.tsx`
Expected: All five tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/dashboard/tiles/FailedJobsTile.tsx \
        frontend/src/components/dashboard/tiles/__tests__/FailedJobsTile.test.tsx
git commit -m "refactor: drive FailedJobsTile click via dashboard drill-down resolver"
```

---

## Task 8: Update `DqtYesterdayStatusTile.tsx` to use the resolver (RED → GREEN)

**Files:**
- Modify: `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx`
- Modify: `frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx`

This is the larger of the two react-router tiles — same pattern, but the resolver is called from inside the click handler and we keep the existing `data-testid="dqt-yesterday-tile"` plus all four visual states (error / no_data / running / success).

- [ ] **Step 1: Update the test to pass `drillDown` and add the "unknown key" case**

In `frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx`, replace the entire file with:

```typescript
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { DqtYesterdayStatusTile } from '../DqtYesterdayStatusTile';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => {
  const actual = jest.requireActual('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

jest.mock('../../../../config/runtimeConfig', () => ({
  getConfig: () => ({ apiUrl: 'http://localhost:5001' }),
}));

const drillDown = { routeKey: 'dataQuality', enabled: true };

const renderTile = (data: any) =>
  render(
    <BrowserRouter>
      <DqtYesterdayStatusTile data={data} />
    </BrowserRouter>,
  );

beforeEach(() => {
  mockNavigate.mockReset();
});

describe('DqtYesterdayStatusTile', () => {
  it('renders no_data state', () => {
    renderTile({ status: 'no_data', data: null, drillDown });

    expect(screen.getByText('Žádná data')).toBeInTheDocument();
    expect(screen.getByText('Včerejší test neproběhl')).toBeInTheDocument();
  });

  it('renders error state and does not navigate on click', () => {
    renderTile({ status: 'error', data: null, drillDown });

    expect(screen.getByText('Chyba při načítání dat')).toBeInTheDocument();
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('renders warning state with Running runStatus', () => {
    renderTile({
      status: 'warning',
      data: {
        runId: 'r1',
        runStatus: 'Running',
        dateFrom: '2026-05-05',
        dateTo: '2026-05-05',
        totalChecked: 0,
        totalMismatches: 0,
      },
      drillDown,
    });

    expect(screen.getByText('probíhá')).toBeInTheDocument();
    expect(screen.getByText('05.05.2026')).toBeInTheDocument();
  });

  it('renders warning state with completed run and mismatches', () => {
    renderTile({
      status: 'warning',
      data: {
        runId: 'r2',
        runStatus: 'Completed',
        dateFrom: '2026-05-05',
        dateTo: '2026-05-05',
        totalChecked: 123,
        totalMismatches: 4,
      },
      drillDown,
    });

    expect(screen.getByText('4')).toBeInTheDocument();
    expect(screen.getByText('neshod')).toBeInTheDocument();
    expect(screen.getByText('z 123 faktur')).toBeInTheDocument();
    expect(screen.getByText('05.05.2026')).toBeInTheDocument();
  });

  it('renders success state', () => {
    renderTile({
      status: 'success',
      data: {
        runId: 'r3',
        runStatus: 'Completed',
        dateFrom: '2026-05-05',
        dateTo: '2026-05-05',
        totalChecked: 200,
        totalMismatches: 0,
      },
      drillDown,
    });

    expect(screen.getByText('0')).toBeInTheDocument();
    expect(screen.getByText('vše OK')).toBeInTheDocument();
    expect(screen.getByText('z 200 faktur')).toBeInTheDocument();
    expect(screen.getByText('05.05.2026')).toBeInTheDocument();
  });

  it('falls back to "včera" label when dateTo is missing', () => {
    renderTile({
      status: 'warning',
      data: {
        runId: 'r4',
        runStatus: 'Running',
        totalChecked: 0,
        totalMismatches: 0,
      },
      drillDown,
    });

    expect(screen.getByText('včera')).toBeInTheDocument();
  });

  it('navigates to /automation/data-quality when clicked (success state)', () => {
    renderTile({
      status: 'success',
      data: {
        runId: 'r5',
        runStatus: 'Completed',
        dateFrom: '2026-05-05',
        dateTo: '2026-05-05',
        totalChecked: 50,
        totalMismatches: 0,
      },
      drillDown,
    });

    const tile = screen.getByTestId('dqt-yesterday-tile');
    fireEvent.click(tile);
    expect(mockNavigate).toHaveBeenCalledWith('/automation/data-quality');
  });

  it('does not navigate when the route key is unknown', () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
    renderTile({
      status: 'success',
      data: {
        runId: 'r6',
        runStatus: 'Completed',
        dateFrom: '2026-05-05',
        dateTo: '2026-05-05',
        totalChecked: 50,
        totalMismatches: 0,
      },
      drillDown: { routeKey: 'somethingNew', enabled: true },
    });

    fireEvent.click(screen.getByTestId('dqt-yesterday-tile'));
    expect(mockNavigate).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('somethingNew'));
    warnSpy.mockRestore();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails (RED)**

Run: `cd frontend && npx jest src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx`
Expected: The "unknown key" case fails (the current component navigates regardless of `drillDown`).

- [ ] **Step 3: Update `DqtYesterdayStatusTile.tsx` to use the resolver**

Replace `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx` with:

```typescript
import React from 'react';
import { useNavigate } from 'react-router-dom';
import { ShieldCheck, AlertTriangle, XCircle, Clock } from 'lucide-react';
import {
  DashboardTileDrillDown,
  resolveDrillDown,
} from '../drillDownRoutes';

interface DqtYesterdayStatusTileData {
  status?: 'success' | 'warning' | 'error' | 'no_data';
  data?: {
    runId?: string;
    runStatus?: 'Completed' | 'Failed' | 'Running';
    dateFrom?: string;
    dateTo?: string;
    totalChecked?: number;
    totalMismatches?: number;
  } | null;
  drillDown?: DashboardTileDrillDown;
}

interface DqtYesterdayStatusTileProps {
  data: DqtYesterdayStatusTileData;
}

const formatYesterdayLabel = (iso?: string): string => {
  if (!iso) return 'včera';
  const dateOnly = iso.slice(0, 10);
  const parts = dateOnly.split('-');
  if (parts.length !== 3) return 'včera';
  return `${parts[2]}.${parts[1]}.${parts[0]}`;
};

export const DqtYesterdayStatusTile: React.FC<DqtYesterdayStatusTileProps> = ({ data }) => {
  const navigate = useNavigate();
  const resolution = resolveDrillDown(data.drillDown);

  const handleClick = () => {
    if (!resolution) {
      return;
    }
    if (resolution.strategy === 'react-router') {
      navigate(resolution.url);
    } else {
      window.open(resolution.url, '_blank');
    }
  };

  if (data.status === 'error') {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <div>
          <XCircle className="h-10 w-10 text-red-500 mx-auto mb-2" />
          <p className="text-red-600 text-sm">Chyba při načítání dat</p>
        </div>
      </div>
    );
  }

  if (data.status === 'no_data') {
    return (
      <div
        className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
        onClick={handleClick}
        style={{ touchAction: 'manipulation' }}
        data-testid="dqt-yesterday-tile"
      >
        <Clock className="h-10 w-10 text-gray-400 mb-2" />
        <p className="text-sm text-gray-500">Žádná data</p>
        <p className="text-xs text-gray-400 mt-1">Včerejší test neproběhl</p>
      </div>
    );
  }

  const runStatus = data.data?.runStatus;
  const dateLabel = formatYesterdayLabel(data.data?.dateTo);

  if (data.status === 'warning' && runStatus === 'Running') {
    return (
      <div
        className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
        onClick={handleClick}
        style={{ touchAction: 'manipulation' }}
        data-testid="dqt-yesterday-tile"
      >
        <Clock className="h-10 w-10 text-amber-500 mb-2" />
        <p className="text-sm text-amber-600">probíhá</p>
        <p className="text-xs text-gray-400 mt-1">{dateLabel}</p>
      </div>
    );
  }

  const totalMismatches = data.data?.totalMismatches ?? 0;
  const totalChecked = data.data?.totalChecked ?? 0;
  const hasMismatches = totalMismatches > 0;
  const iconColor = hasMismatches ? 'text-red-500' : 'text-green-500';
  const countColor = hasMismatches ? 'text-red-700' : 'text-green-700';

  return (
    <div
      className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
      onClick={handleClick}
      style={{ touchAction: 'manipulation' }}
      data-testid="dqt-yesterday-tile"
    >
      <div className={`mb-2 ${iconColor}`}>
        {hasMismatches ? (
          <AlertTriangle className="h-10 w-10" />
        ) : (
          <ShieldCheck className="h-10 w-10" />
        )}
      </div>
      <div className={`text-3xl font-bold mb-1 ${countColor}`}>{totalMismatches}</div>
      <div className="text-sm text-gray-500">{hasMismatches ? 'neshod' : 'vše OK'}</div>
      {totalChecked > 0 && (
        <div className="text-xs text-gray-400 mt-1">z {totalChecked} faktur</div>
      )}
      <div className="text-xs text-gray-400 mt-1">{dateLabel}</div>
    </div>
  );
};
```

- [ ] **Step 4: Run the test to verify it passes (GREEN)**

Run: `cd frontend && npx jest src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx`
Expected: All eight tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx \
        frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx
git commit -m "refactor: drive DqtYesterdayStatusTile click via dashboard drill-down resolver"
```

---

## Task 9: Update `DataQualityTile.tsx` to use the resolver

**Files:**
- Modify: `frontend/src/components/dashboard/tiles/DataQualityTile.tsx`
- Create: `frontend/src/components/dashboard/tiles/__tests__/DataQualityTile.test.tsx`

This tile has no dedicated test file today. We create a small one alongside the refactor — only enough to lock the click → resolver path.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/dashboard/tiles/__tests__/DataQualityTile.test.tsx`:

```typescript
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { DataQualityTile } from '../DataQualityTile';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => {
  const actual = jest.requireActual('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

jest.mock('../../../../config/runtimeConfig', () => ({
  getConfig: () => ({ apiUrl: 'http://localhost:5001' }),
}));

const drillDown = { routeKey: 'dataQuality', enabled: true };

const renderTile = (data: any) =>
  render(
    <BrowserRouter>
      <DataQualityTile data={data} />
    </BrowserRouter>,
  );

beforeEach(() => {
  mockNavigate.mockReset();
});

describe('DataQualityTile', () => {
  it('navigates to /automation/data-quality on click in the no_data state', () => {
    renderTile({ status: 'no_data', drillDown });

    fireEvent.click(screen.getByText('Žádná data'));
    expect(mockNavigate).toHaveBeenCalledWith('/automation/data-quality');
  });

  it('navigates on click in the success state', () => {
    renderTile({
      status: 'success',
      data: { mismatchCount: 0, totalChecked: 100, dateFrom: '2026-05-05', dateTo: '2026-05-05' },
      drillDown,
    });

    fireEvent.click(screen.getByText('vše OK'));
    expect(mockNavigate).toHaveBeenCalledWith('/automation/data-quality');
  });

  it('does not navigate when the route key is unknown', () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
    renderTile({
      status: 'success',
      data: { mismatchCount: 0, totalChecked: 100, dateFrom: '2026-05-05', dateTo: '2026-05-05' },
      drillDown: { routeKey: 'somethingNew', enabled: true },
    });

    fireEvent.click(screen.getByText('vše OK'));
    expect(mockNavigate).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('somethingNew'));
    warnSpy.mockRestore();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails (RED)**

Run: `cd frontend && npx jest src/components/dashboard/tiles/__tests__/DataQualityTile.test.tsx`
Expected: The "unknown key" case fails because the current component always navigates.

- [ ] **Step 3: Update `DataQualityTile.tsx` to use the resolver**

Replace `frontend/src/components/dashboard/tiles/DataQualityTile.tsx` with:

```typescript
import React from 'react';
import { useNavigate } from 'react-router-dom';
import { ShieldCheck, AlertTriangle, XCircle, Clock } from 'lucide-react';
import {
  DashboardTileDrillDown,
  resolveDrillDown,
} from '../drillDownRoutes';

interface DataQualityTileProps {
  data: {
    status?: string;
    data?: {
      mismatchCount?: number;
      totalChecked?: number;
      dateFrom?: string;
      dateTo?: string;
    };
    error?: string;
    drillDown?: DashboardTileDrillDown;
  };
}

export const DataQualityTile: React.FC<DataQualityTileProps> = ({ data }) => {
  const navigate = useNavigate();
  const resolution = resolveDrillDown(data.drillDown);

  const handleClick = () => {
    if (!resolution) {
      return;
    }
    if (resolution.strategy === 'react-router') {
      navigate(resolution.url);
    } else {
      window.open(resolution.url, '_blank');
    }
  };

  if (data.status === 'error') {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <div>
          <XCircle className="h-10 w-10 text-red-500 mx-auto mb-2" />
          <p className="text-red-600 text-sm">{data.error || 'Chyba při načítání dat'}</p>
        </div>
      </div>
    );
  }

  if (data.status === 'no_data') {
    return (
      <div
        className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
        onClick={handleClick}
        style={{ touchAction: 'manipulation' }}
      >
        <Clock className="h-10 w-10 text-gray-400 mb-2" />
        <p className="text-sm text-gray-500">Žádná data</p>
        <p className="text-xs text-gray-400 mt-1">Spusťte první kontrolu</p>
      </div>
    );
  }

  const mismatchCount = data.data?.mismatchCount ?? 0;
  const totalChecked = data.data?.totalChecked ?? 0;
  const dateFrom = data.data?.dateFrom;
  const dateTo = data.data?.dateTo;

  const hasMismatches = mismatchCount > 0;
  const iconColor = hasMismatches ? 'text-red-500' : 'text-green-500';
  const countColor = hasMismatches ? 'text-red-700' : 'text-green-700';

  const dateRange =
    dateFrom && dateTo
      ? `${dateFrom} – ${dateTo}`
      : null;

  return (
    <div
      className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
      onClick={handleClick}
      style={{ touchAction: 'manipulation' }}
    >
      <div className={`mb-2 ${iconColor}`}>
        {hasMismatches ? (
          <AlertTriangle className="h-10 w-10" />
        ) : (
          <ShieldCheck className="h-10 w-10" />
        )}
      </div>
      <div className={`text-3xl font-bold mb-1 ${countColor}`}>
        {mismatchCount}
      </div>
      <div className="text-sm text-gray-500">
        {hasMismatches ? 'neshod' : 'vše OK'}
      </div>
      {totalChecked > 0 && (
        <div className="text-xs text-gray-400 mt-1">
          z {totalChecked} faktur
        </div>
      )}
      {dateRange && (
        <div className="text-xs text-gray-400 mt-1">
          {dateRange}
        </div>
      )}
    </div>
  );
};
```

- [ ] **Step 4: Run the test to verify it passes (GREEN)**

Run: `cd frontend && npx jest src/components/dashboard/tiles/__tests__/DataQualityTile.test.tsx`
Expected: All three tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/dashboard/tiles/DataQualityTile.tsx \
        frontend/src/components/dashboard/tiles/__tests__/DataQualityTile.test.tsx
git commit -m "refactor: drive DataQualityTile click via dashboard drill-down resolver"
```

---

## Task 10: Document the two-shape drill-down divergence

**Files:**
- Create: `memory/patterns/dashboard-tile-drilldown.md`

The codebase now has two drill-down payload shapes living side by side (filter-based legacy in `urlUtils.ts`, new route-key shape in `drillDownRoutes.ts`). Per arch-review Risk #4 and Amendment #5 we record a one-pager so future tile authors don't pick the wrong one.

- [ ] **Step 1: Write the pattern note**

Create `memory/patterns/dashboard-tile-drilldown.md`:

````markdown
# Dashboard tile drill-down: two shapes

As of 2026-06-02, the dashboard tile system supports **two** drill-down payload shapes. Use the route-key shape for any new tile.

## Preferred — route-key shape

Use `DashboardTileDrillDown` (backend: `Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs`; frontend mirror: `components/dashboard/drillDownRoutes.ts`).

- Backend emits `{ routeKey, enabled, parameters? }`. No URL strings.
- Add the route key to `DASHBOARD_DRILLDOWN_ROUTES` in `drillDownRoutes.ts` with either `type: 'react-router'` or `type: 'external'` (backend-mounted admin UI).
- Tile component calls `resolveDrillDown(data.drillDown)` and dispatches by `strategy`. Unknown / disabled / undefined → null → tile renders non-interactive.

Tiles using this shape: `DataQualityStatusTile`, `DqtYesterdayStatusTile`, `FailedJobsTile`.

## Legacy — filter shape

Used by `PurchaseOrdersInTransitTile`, `LowStockAlertTile`, `BackgroundTasksTile`, `CountTile`, `InventorySummaryTile`.

- Backend emits `{ filters, enabled, tooltip }`. No URL strings either.
- Frontend tile component hardcodes the base path and uses `createFilteredUrl(basePath, drillDown.filters)` (`frontend/src/utils/urlUtils.ts`).

Do **not** add new filter-shape tiles. If a new tile needs filter parameters in the URL, use the route-key shape with `parameters` and have the tile component build the filter URL from the resolver result.

## Future unification

A later pass should migrate filter-shape tiles onto the route-key shape (route key + parameters dict + frontend builds the URL from a registry entry). Out of scope for this iteration.
````

- [ ] **Step 2: Commit**

```bash
git add memory/patterns/dashboard-tile-drilldown.md
git commit -m "docs: note dashboard tile drill-down two-shape divergence"
```

---

## Task 11: Final cross-cutting validation

**Files:** (no edits)

End-to-end sanity check before declaring the feature done. This mirrors the project's "Validation before completion" checklist in `CLAUDE.md`.

- [ ] **Step 1: Backend full build + format check**

Run: `dotnet build backend/Anela.Heblo.sln --nologo --verbosity minimal && dotnet format backend/Anela.Heblo.sln --verify-no-changes --verbosity minimal`
Expected: Build succeeds; format reports no changes.

- [ ] **Step 2: Backend full test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore --nologo`
Expected: All tests pass.

- [ ] **Step 3: Frontend build (includes type check)**

Run: `cd frontend && npm run build`
Expected: Build succeeds.

- [ ] **Step 4: Frontend lint**

Run: `cd frontend && npm run lint`
Expected: No errors. (Warnings tolerated only if they exist on `main` already.)

- [ ] **Step 5: Frontend test suite (dashboard scope)**

Run: `cd frontend && npx jest src/components/dashboard`
Expected: All dashboard component and resolver tests pass.

- [ ] **Step 6: Cross-stack literal sweep**

Run:

```bash
grep -nE "\"/(data-quality|automation/data-quality|hangfire/jobs/failed)" backend/src \
  | grep -v "^backend/test"
```

Expected: No matches in production backend code (`backend/src`).

Run:

```bash
grep -nE "(href|url)\s*:\s*['\"]/" frontend/src/components/dashboard
```

Expected: No matches. (Filter-based tiles use `filters: {...}`, not `href:` / `url:`.)

- [ ] **Step 7: Manual smoke check (optional but encouraged)**

If a local backend + frontend are runnable, start both and visit the dashboard. Click each of the three affected tiles and confirm:
- `Kvalita dat` → `/automation/data-quality`
- `DQT včera` → `/automation/data-quality`
- `Failed background jobs` → opens `${apiUrl}/hangfire/jobs/failed` in a new tab

If manual smoke is not possible, note that in the PR description; tests cover the same paths.

- [ ] **Step 8: No commit; task closes the feature**

Audit task only — nothing to commit. Feature is ready for review.

---

## Self-Review Notes

Coverage of spec functional requirements:

- **FR-1** (`DashboardTileDrillDown` contract) → Task 1. The arch-review amendment about OpenAPI is honored: no acceptance criterion in this plan claims OpenAPI codegen will emit the type; the frontend mirrors by hand in Task 6.
- **FR-2** (`DataQualityStatusTile` refactor) → Task 4. Plus new test file per arch-review §7.
- **FR-3** (`DqtYesterdayStatusTile` refactor) → Task 2.
- **FR-4** (`FailedJobsTile` refactor) → Task 3. `tooltip` is preserved.
- **FR-5** (frontend route registry) → Task 6. File lives at `frontend/src/components/dashboard/drillDownRoutes.ts` per arch-review amendment §3, not `src/dashboard/` as the spec said.
- **FR-6** (frontend tile components consume route key) → Tasks 7, 8, 9.
- **FR-7** (unknown key fails safely) → covered in Task 6 (resolver test) and exercised in Tasks 8, 9 ("unknown key" component tests).
- **NFR-3** (maintainability) → after this PR, changing `/automation/data-quality` requires editing only `drillDownRoutes.ts`.
- **NFR-4** (test coverage) → new backend test file (Task 4), updated backend tests (Tasks 2, 3), new resolver tests (Task 6), updated/new frontend component tests (Tasks 7, 8, 9).
- **Hangfire cross-origin** (arch-review amendment §2) → Task 6 prepends `${apiUrl}` for `external` strategy and Task 7 opens in `_blank` to preserve current UX.
- **Two-shape divergence note** (arch-review amendment §5) → Task 10.
- **Audit pass for other tile URL literals** (spec "Out of Scope" line about audit) → Task 5 + Task 11 Step 6.
