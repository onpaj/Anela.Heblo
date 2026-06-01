# Dashboard Tile — Yesterday's DQT Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `DqtYesterdayStatusTile` that surfaces the result of yesterday's scheduled `IssuedInvoiceComparison` DQT run, distinct from the existing "latest run" tile.

**Architecture:** Add one new `ITile` implementation that depends on `IDqtRunRepository` (extended with one query method) and `TimeProvider`. Wire it through `RegisterTile<T>()` in `DashboardModule`. Render via a new React component switched into `TileContent.tsx`. No new endpoints, no schema changes, no migrations.

**Tech Stack:** .NET 8 (xUnit, Moq, FluentAssertions, EF Core in-memory provider for repository tests, MediatR/dashboard tile pipeline already in place); React + TypeScript + Tailwind + lucide-react + react-router-dom + Jest/RTL on the frontend.

---

## File Structure

**Backend — new files**
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` — `ITile` implementation; reads yesterday's run via repo and maps to the dashboard payload.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs` — unit tests for `LoadDataAsync` (no_data, success, warning–mismatches, warning–running, error–failed run, error–exception).
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DqtRunRepositoryTests.cs` — EF Core in-memory tests for the new repo method.

**Backend — modified files**
- `backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs` — add `GetLatestByTestTypeAndCoveredDateAsync`.
- `backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunRepository.cs` — implement that method.
- `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` — register the new tile.

**Frontend — new files**
- `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx` — render the four states with the Running differentiation.
- `frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx` — RTL tests for each state and the click handler.

**Frontend — modified files**
- `frontend/src/components/dashboard/tiles/TileContent.tsx` — add import and switch case.
- `frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx` — add coverage for the new case.

---

## Conventions / Reference Patterns

- `ITile` lives at `backend/src/Anela.Heblo.Xcc/Services/Dashboard/ITile.cs` — **no** `GetTileId()` method exists. Tile ids are derived from class name by `TileExtensions.GetTileId`: `Type.Name.ToLowerInvariant().Replace("tile", "")`. Class name `DqtYesterdayStatusTile` → tile id `"dqtyesterdaystatus"`. Do not add a `GetTileId()` method.
- `TimeProvider` is registered as a singleton in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (`services.AddSingleton(TimeProvider.System)`). Inject by constructor; do not add a fallback to `DateTimeOffset.Now`.
- Sibling tile `DataQualityStatusTile` (`backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs`) is the structural model. Reuse the anonymous-payload shape `{ status, data, drillDown }`.
- `DqtRun` is constructed via factory `DqtRun.Start(testType, dateFrom, dateTo, triggerType)` then mutated by `Complete(totalChecked, totalMismatches)` or `Fail(errorMessage)`. The default ctor is private.
- Repository ordering rule: `OrderByDescending(r => r.StartedAt)` only — matches existing `GetLatestByTestTypeAsync`. Do not introduce a `CompletedAt` tie-breaker.
- Drill-down href for the new tile: `"/automation/data-quality"` (the actual frontend route). Do not touch `DataQualityStatusTile`'s wrong `"/data-quality"` href — that's out of scope.
- Czech copy values, locked: `"DQT včera"` (title), `"Stav včerejšího DQT testu faktur"` (description), `"Žádná data"`, `"Včerejší test neproběhl"`, `"probíhá"`, `"neshod"`, `"vše OK"`, `"z X faktur"`, `"Chyba při načítání dat"`.
- Backend tests live under `backend/test/Anela.Heblo.Tests/`; xUnit + Moq + FluentAssertions are already referenced by the test project.
- Frontend tests live alongside the component in `__tests__/` and use `BrowserRouter` to wrap components that call `useNavigate`.

---

## Task 1: Repository interface — add `GetLatestByTestTypeAndCoveredDateAsync`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs`

- [ ] **Step 1: Append the new method to the interface**

Edit `backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs` so the body becomes:

```csharp
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.DataQuality;

public interface IDqtRunRepository : IRepository<DqtRun, Guid>
{
    Task<DqtRun?> GetLatestByTestTypeAsync(DqtTestType testType, CancellationToken cancellationToken = default);
    Task<DqtRun?> GetLatestByTestTypeAndCoveredDateAsync(
        DqtTestType testType,
        DateOnly coveredDate,
        CancellationToken cancellationToken = default);
    Task<(List<DqtRun> Items, int TotalCount)> GetPaginatedAsync(
        DqtTestType? testType,
        DqtRunStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<DqtRun?> GetWithResultsAsync(Guid id, int resultPage, int resultPageSize, CancellationToken cancellationToken = default);
    Task AddResultsAsync(IEnumerable<InvoiceDqtResult> results, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Verify the build fails because `DqtRunRepository` no longer satisfies the interface**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: FAIL — `'DqtRunRepository' does not implement interface member 'IDqtRunRepository.GetLatestByTestTypeAndCoveredDateAsync(...)'`

(Build failure is the proof the interface change took effect; do not commit yet — implementation is part of Task 2.)

---

## Task 2: Repository implementation + tests

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/DataQuality/DqtRunRepositoryTests.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunRepository.cs`

- [ ] **Step 1: Create the failing repository test file**

Create `backend/test/Anela.Heblo.Tests/Features/DataQuality/DqtRunRepositoryTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.DataQuality;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class DqtRunRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly DqtRunRepository _repository;

    public DqtRunRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new DqtRunRepository(_context);
    }

    [Fact]
    public async Task GetLatestByTestTypeAndCoveredDateAsync_ReturnsRunCoveringDate()
    {
        // Arrange
        var yesterday = new DateOnly(2026, 5, 5);
        var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Scheduled);
        run.Complete(totalChecked: 100, totalMismatches: 0);
        _context.Set<DqtRun>().Add(run);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
            DqtTestType.IssuedInvoiceComparison, yesterday);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(run.Id);
    }

    [Fact]
    public async Task GetLatestByTestTypeAndCoveredDateAsync_ReturnsNullWhenNoRunCoversDate()
    {
        // Arrange
        var run = DqtRun.Start(
            DqtTestType.IssuedInvoiceComparison,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 3),
            DqtTriggerType.Scheduled);
        _context.Set<DqtRun>().Add(run);
        await _context.SaveChangesAsync();

        // Act — yesterday is outside the run's range
        var result = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
            DqtTestType.IssuedInvoiceComparison, new DateOnly(2026, 5, 5));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestByTestTypeAndCoveredDateAsync_FiltersByTestType()
    {
        // Arrange — there is only one DqtTestType today, but assert the predicate is wired.
        var yesterday = new DateOnly(2026, 5, 5);
        var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Scheduled);
        _context.Set<DqtRun>().Add(run);
        await _context.SaveChangesAsync();

        // Act
        var sameType = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
            DqtTestType.IssuedInvoiceComparison, yesterday);

        // Assert
        sameType.Should().NotBeNull();
        sameType!.TestType.Should().Be(DqtTestType.IssuedInvoiceComparison);
    }

    [Fact]
    public async Task GetLatestByTestTypeAndCoveredDateAsync_ReturnsMostRecentByStartedAt()
    {
        // Arrange — two runs both cover yesterday; the later StartedAt wins.
        var yesterday = new DateOnly(2026, 5, 5);
        var earlier = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Scheduled);
        // mutate StartedAt via reflection — DqtRun has private setter and ctor sets it to UtcNow
        typeof(DqtRun).GetProperty(nameof(DqtRun.StartedAt))!
            .SetValue(earlier, new DateTime(2026, 5, 6, 6, 0, 0, DateTimeKind.Utc));

        var later = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, yesterday, yesterday, DqtTriggerType.Manual);
        typeof(DqtRun).GetProperty(nameof(DqtRun.StartedAt))!
            .SetValue(later, new DateTime(2026, 5, 6, 8, 0, 0, DateTimeKind.Utc));

        _context.Set<DqtRun>().AddRange(earlier, later);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
            DqtTestType.IssuedInvoiceComparison, yesterday);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(later.Id);
    }

    [Fact]
    public async Task GetLatestByTestTypeAndCoveredDateAsync_MatchesWideRangeCoveringDate()
    {
        // Arrange — run with a multi-day range that includes yesterday.
        var run = DqtRun.Start(
            DqtTestType.IssuedInvoiceComparison,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 7),
            DqtTriggerType.Manual);
        _context.Set<DqtRun>().Add(run);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
            DqtTestType.IssuedInvoiceComparison, new DateOnly(2026, 5, 5));

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(run.Id);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
```

- [ ] **Step 2: Run repository tests to verify they fail to compile (method missing)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DqtRunRepositoryTests"`
Expected: BUILD FAIL — `'DqtRunRepository' does not contain a definition for 'GetLatestByTestTypeAndCoveredDateAsync'` (or interface compile error from Task 1).

- [ ] **Step 3: Implement the repository method**

Edit `backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunRepository.cs`. Append after `GetLatestByTestTypeAsync` (so it sits next to the sibling query):

```csharp
public async Task<DqtRun?> GetLatestByTestTypeAndCoveredDateAsync(
    DqtTestType testType,
    DateOnly coveredDate,
    CancellationToken cancellationToken = default)
{
    return await DbSet
        .Where(r => r.TestType == testType
                    && r.DateFrom <= coveredDate
                    && r.DateTo >= coveredDate)
        .OrderByDescending(r => r.StartedAt)
        .FirstOrDefaultAsync(cancellationToken);
}
```

- [ ] **Step 4: Run repository tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DqtRunRepositoryTests"`
Expected: PASS — all 5 tests green.

- [ ] **Step 5: Build + format**

Run: `dotnet build backend/Anela.Heblo.sln && dotnet format backend/Anela.Heblo.sln --verify-no-changes` (run `dotnet format backend/Anela.Heblo.sln` if `--verify-no-changes` reports diffs, then re-verify).
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs \
        backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/DqtRunRepositoryTests.cs
git commit -m "$(cat <<'EOF'
feat: add IDqtRunRepository.GetLatestByTestTypeAndCoveredDateAsync

Returns the most recent run (by StartedAt desc) of a given test type
whose covered range includes the supplied date. Used by the upcoming
"yesterday DQT status" dashboard tile.
EOF
)"
```

---

## Task 3: `DqtYesterdayStatusTile` — backend tile class + tests

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs`

- [ ] **Step 1: Create the failing tile test file**

Create `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.DataQuality.DashboardTiles;

public class DqtYesterdayStatusTileTests
{
    // Pinned "today" = 2026-05-06 → yesterday = 2026-05-05
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 6, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Yesterday = new(2026, 5, 5);

    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly DqtYesterdayStatusTile _tile;

    public DqtYesterdayStatusTileTests()
    {
        _timeProviderMock.Setup(x => x.GetLocalNow()).Returns(FixedNow);

        _tile = new DqtYesterdayStatusTile(
            _repositoryMock.Object,
            _timeProviderMock.Object,
            NullLogger<DqtYesterdayStatusTile>.Instance);
    }

    [Fact]
    public async Task LoadDataAsync_NoRunCoveringYesterday_ReturnsNoData()
    {
        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun?)null);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("no_data");
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("drillDown").GetProperty("href").GetString()
            .Should().Be("/automation/data-quality");
        doc.RootElement.GetProperty("drillDown").GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task LoadDataAsync_CompletedWithZeroMismatches_ReturnsSuccess()
    {
        var run = CreateRun(yesterday: Yesterday, status: DqtRunStatus.Completed, totalChecked: 123, totalMismatches: 0);

        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("runStatus").GetString().Should().Be("Completed");
        data.GetProperty("totalChecked").GetInt32().Should().Be(123);
        data.GetProperty("totalMismatches").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_CompletedWithMismatches_ReturnsWarning()
    {
        var run = CreateRun(yesterday: Yesterday, status: DqtRunStatus.Completed, totalChecked: 123, totalMismatches: 4);

        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("warning");
        doc.RootElement.GetProperty("data").GetProperty("totalMismatches").GetInt32().Should().Be(4);
        doc.RootElement.GetProperty("data").GetProperty("runStatus").GetString().Should().Be("Completed");
    }

    [Fact]
    public async Task LoadDataAsync_RunningRun_ReturnsWarningWithRunningStatus()
    {
        var run = CreateRun(yesterday: Yesterday, status: DqtRunStatus.Running, totalChecked: 0, totalMismatches: 0);

        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("warning");
        doc.RootElement.GetProperty("data").GetProperty("runStatus").GetString().Should().Be("Running");
    }

    [Fact]
    public async Task LoadDataAsync_FailedRun_ReturnsError()
    {
        var run = CreateRun(yesterday: Yesterday, status: DqtRunStatus.Failed, totalChecked: 0, totalMismatches: 0);

        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("data").GetProperty("runStatus").GetString().Should().Be("Failed");
    }

    [Fact]
    public async Task LoadDataAsync_RepositoryThrows_ReturnsErrorAndDoesNotPropagate()
    {
        _repositoryMock
            .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("drillDown").GetProperty("href").GetString()
            .Should().Be("/automation/data-quality");
    }

    [Fact]
    public void TileMetadata_MatchesSpec()
    {
        _tile.Title.Should().Be("DQT včera");
        _tile.Description.Should().Be("Stav včerejšího DQT testu faktur");
        _tile.Size.Should().Be(Anela.Heblo.Xcc.Services.Dashboard.TileSize.Medium);
        _tile.Category.Should().Be(Anela.Heblo.Xcc.Services.Dashboard.TileCategory.DataQuality);
        _tile.DefaultEnabled.Should().BeTrue();
        _tile.AutoShow.Should().BeFalse();
        _tile.RequiredPermissions.Should().BeEmpty();
    }

    private static DqtRun CreateRun(DateOnly yesterday, DqtRunStatus status, int totalChecked, int totalMismatches)
    {
        var run = DqtRun.Start(
            DqtTestType.IssuedInvoiceComparison,
            yesterday,
            yesterday,
            DqtTriggerType.Scheduled);

        if (status == DqtRunStatus.Completed)
        {
            run.Complete(totalChecked, totalMismatches);
        }
        else if (status == DqtRunStatus.Failed)
        {
            run.Fail("simulated failure");
        }
        // Running: leave as-is (DqtRun.Start sets Status = Running).
        return run;
    }

    private static JsonDocument ToJsonDoc(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload));
}
```

- [ ] **Step 2: Run tile tests to verify they fail (class missing)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DqtYesterdayStatusTileTests"`
Expected: BUILD FAIL — `DqtYesterdayStatusTile` not defined.

- [ ] **Step 3: Implement the tile class**

Create `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs`:

```csharp
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.DashboardTiles;

public class DqtYesterdayStatusTile : ITile
{
    private const string DrillDownHref = "/automation/data-quality";

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
        var yesterday = DateOnly.FromDateTime(_timeProvider.GetLocalNow().LocalDateTime).AddDays(-1);

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
                    drillDown = new { href = DrillDownHref, enabled = true }
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
                drillDown = new { href = DrillDownHref, enabled = true }
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
                drillDown = new { href = DrillDownHref, enabled = true }
            };
        }
    }
}
```

- [ ] **Step 4: Run tile tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DqtYesterdayStatusTileTests"`
Expected: PASS — all 7 tests green.

- [ ] **Step 5: Build + format**

Run: `dotnet build backend/Anela.Heblo.sln && dotnet format backend/Anela.Heblo.sln --verify-no-changes` (re-format if needed).
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs
git commit -m "$(cat <<'EOF'
feat: add DqtYesterdayStatusTile dashboard tile

Surfaces yesterday's IssuedInvoiceComparison DQT run with status
mapped to success / warning (mismatches or still running) / error
(failed run or repo exception) / no_data. Drill-down href points to
/automation/data-quality.
EOF
)"
```

---

## Task 4: Register the tile in `DashboardModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`

- [ ] **Step 1: Append `RegisterTile<DqtYesterdayStatusTile>()` after the existing DataQuality registration**

Edit `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` so the body becomes:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Tiles;
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the ApplicationModule

        // Register dashboard tiles
        services.RegisterTile<PurchaseOrdersInTransitTile>();
        services.RegisterTile<DataQualityStatusTile>();
        services.RegisterTile<DqtYesterdayStatusTile>();

        return services;
    }
}
```

- [ ] **Step 2: Build + run application startup tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ApplicationStartupTests"`
Expected: PASS — DI graph still resolves; new tile is registered.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs
git commit -m "feat: register DqtYesterdayStatusTile in DashboardModule"
```

---

## Task 5: Frontend — `DqtYesterdayStatusTile.tsx` + tests

**Files:**
- Test: `frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx`
- Create: `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx`

- [ ] **Step 1: Create the failing component test file**

Create `frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { DqtYesterdayStatusTile } from '../DqtYesterdayStatusTile';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => {
  const actual = jest.requireActual('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

const renderTile = (data: any) =>
  render(
    <BrowserRouter>
      <DqtYesterdayStatusTile data={data} />
    </BrowserRouter>
  );

beforeEach(() => {
  mockNavigate.mockReset();
});

describe('DqtYesterdayStatusTile', () => {
  it('renders no_data state', () => {
    renderTile({ status: 'no_data', data: null });

    expect(screen.getByText('Žádná data')).toBeInTheDocument();
    expect(screen.getByText('Včerejší test neproběhl')).toBeInTheDocument();
  });

  it('renders error state and does not navigate on click', () => {
    const { container } = renderTile({ status: 'error', data: null });

    expect(screen.getByText('Chyba při načítání dat')).toBeInTheDocument();
    fireEvent.click(container.firstChild as Element);
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
    });

    expect(screen.getByText('včera')).toBeInTheDocument();
  });

  it('navigates to /automation/data-quality when clicked (success state)', () => {
    const { container } = renderTile({
      status: 'success',
      data: {
        runId: 'r5',
        runStatus: 'Completed',
        dateFrom: '2026-05-05',
        dateTo: '2026-05-05',
        totalChecked: 50,
        totalMismatches: 0,
      },
    });

    fireEvent.click(container.firstChild as Element);
    expect(mockNavigate).toHaveBeenCalledWith('/automation/data-quality');
  });
});
```

- [ ] **Step 2: Run frontend tests to verify they fail (component missing)**

Run: `cd frontend && npm test -- --testPathPattern="DqtYesterdayStatusTile" --watchAll=false`
Expected: FAIL — `Cannot find module '../DqtYesterdayStatusTile'`.

- [ ] **Step 3: Implement the component**

Create `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx`:

```tsx
import React from 'react';
import { useNavigate } from 'react-router-dom';
import { ShieldCheck, AlertTriangle, XCircle, Clock } from 'lucide-react';

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
  drillDown?: {
    href: string;
    enabled: boolean;
  };
}

interface DqtYesterdayStatusTileProps {
  data: DqtYesterdayStatusTileData;
}

const formatYesterdayLabel = (iso?: string): string => {
  if (!iso) return 'včera';
  const parts = iso.split('-');
  if (parts.length !== 3) return 'včera';
  return `${parts[2]}.${parts[1]}.${parts[0]}`;
};

export const DqtYesterdayStatusTile: React.FC<DqtYesterdayStatusTileProps> = ({ data }) => {
  const navigate = useNavigate();
  const handleClick = () => navigate('/automation/data-quality');

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

- [ ] **Step 4: Run frontend tests to verify they pass**

Run: `cd frontend && npm test -- --testPathPattern="DqtYesterdayStatusTile" --watchAll=false`
Expected: PASS — all 7 tests green.

- [ ] **Step 5: Build + lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: PASS — no TypeScript errors, no lint errors in the new file.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx \
        frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx
git commit -m "$(cat <<'EOF'
feat: add DqtYesterdayStatusTile React component

Renders four states (no_data / error / warning / success) with a
distinct visual for runStatus === 'Running' vs. completed-with-
mismatches. Clicking navigates to /automation/data-quality.
EOF
)"
```

---

## Task 6: Wire the new component into `TileContent.tsx`

**Files:**
- Modify: `frontend/src/components/dashboard/tiles/TileContent.tsx`
- Modify: `frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx`

- [ ] **Step 1: Add a failing dispatcher test**

Edit `frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx`:

After the existing `jest.mock('../DefaultTile', ...)` block, add:

```tsx
jest.mock('../DqtYesterdayStatusTile', () => ({
  DqtYesterdayStatusTile: ({ data }: any) => <div data-testid="dqt-yesterday-tile">{JSON.stringify(data)}</div>
}));
```

Then, near the bottom of the `describe('TileContent', ...)` block (e.g. just before the existing `'should render DefaultTile for unknown tile types'` test), add:

```tsx
it('should render DqtYesterdayStatusTile for dqtyesterdaystatus', () => {
  const data = { status: 'success', data: { totalMismatches: 0 } };
  const tile = createMockTile('dqtyesterdaystatus', data);
  render(<TileContent tile={tile} />);

  expect(screen.getByTestId('dqt-yesterday-tile')).toBeInTheDocument();
  expect(screen.getByText(JSON.stringify(data))).toBeInTheDocument();
});
```

- [ ] **Step 2: Run dispatcher test to verify it fails**

Run: `cd frontend && npm test -- --testPathPattern="TileContent" --watchAll=false`
Expected: FAIL — `default-tile` rendered instead of `dqt-yesterday-tile`.

- [ ] **Step 3: Add the import and switch case**

Edit `frontend/src/components/dashboard/tiles/TileContent.tsx`:

After the line `import { DataQualityTile } from './DataQualityTile';`, add:

```tsx
import { DqtYesterdayStatusTile } from './DqtYesterdayStatusTile';
```

Inside the switch, after the `case 'dataqualitystatus':` block, add:

```tsx
    case 'dqtyesterdaystatus':
      return <DqtYesterdayStatusTile data={tile.data} />;
```

- [ ] **Step 4: Run dispatcher test to verify it passes**

Run: `cd frontend && npm test -- --testPathPattern="TileContent" --watchAll=false`
Expected: PASS — including the new case.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/dashboard/tiles/TileContent.tsx \
        frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx
git commit -m "feat: dispatch dqtyesterdaystatus to DqtYesterdayStatusTile"
```

---

## Task 7: End-to-end validation

**Files:** none modified — verification only.

- [ ] **Step 1: Backend full build + test sweep**

Run: `dotnet build backend/Anela.Heblo.sln && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: build OK; all tests pass (no regressions).

- [ ] **Step 2: Backend formatting verification**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: PASS (or run `dotnet format backend/Anela.Heblo.sln` to fix and amend the most recent commit only if formatting was missed in earlier tasks).

- [ ] **Step 3: Frontend full build + lint + test sweep**

Run: `cd frontend && npm run build && npm run lint && npm test -- --watchAll=false`
Expected: build OK, no lint errors, all tests pass.

- [ ] **Step 4: Manual smoke-check of registry payload (optional but recommended)**

Start the backend (`dotnet run --project backend/src/Anela.Heblo.API`) and either:
- hit `GET /api/dashboard/tiles` (or whichever endpoint exposes the registry) and confirm an entry with `tileId: "dqtyesterdaystatus"` and `category: "DataQuality"`; or
- open the dashboard settings panel in the frontend and confirm the new tile appears under "Kvalita dat".

This step is exploratory — no commit. If the tile doesn't appear, re-check Task 4.

- [ ] **Step 5: No-op final commit if validation surfaced no fixes**

If steps 1–4 pass without changes, skip this step. Otherwise, add small fix-up commits per issue (do **not** amend prior commits).

---

## Self-Review

**Spec coverage**

| Spec requirement | Task |
| --- | --- |
| FR-1: backend tile class with metadata | Task 3 (impl) + `TileMetadata_MatchesSpec` test |
| FR-2: repository method `GetLatestByTestTypeAndCoveredDateAsync` | Tasks 1–2 |
| FR-3: `LoadDataAsync` payload + status mapping | Task 3 (all 5 truth-table branches + exception path covered by tests) |
| FR-4: DI registration in `DashboardModule` | Task 4 |
| FR-5: frontend tile component, four states, Running differentiation, navigation | Task 5 |
| FR-6: dispatcher `case 'dqtyesterdaystatus'` in `TileContent.tsx` | Task 6 |
| FR-7: settings-panel discoverability (data-driven from registry) | Satisfied transitively by Task 4 (no FE changes needed) |
| NFR-1: performance / index | Reuses existing `IX_DqtRuns_TestType_StartedAt`; no new migration (per arch review) |
| NFR-2: security / no new endpoints | No new endpoints — existing dashboard auth applies |
| NFR-3: testable time source via `TimeProvider` | Task 3 (constructor injection + Moq for tests) |
| NFR-4: structured logging on failure | Task 3 implementation `_logger.LogError(...)` |
| NFR-5: Czech copy | Task 5 component, plus locked Czech values in conventions |
| NFR-6: backend + frontend test coverage | Tasks 2, 3, 5, 6 |

**Placeholder scan:** No `TBD`, no "implement later", no "similar to Task N", no naked "add error handling" — every code-changing step shows the actual code.

**Type / name consistency:**
- Repository method name `GetLatestByTestTypeAndCoveredDateAsync` is identical in interface (Task 1), implementation (Task 2), and call sites (Task 3 backend tile + Task 3 tile tests). 
- Tile class name `DqtYesterdayStatusTile` is identical in: tile file (Task 3), tile tests (Task 3), `DashboardModule` (Task 4), frontend dispatcher mock + case (Task 6), and component file (Task 5). 
- Frontend `runStatus` literals (`'Running' | 'Completed' | 'Failed'`) match backend `run.Status.ToString()` output (the .NET enum names). 
- Frontend `status` union (`'success' | 'warning' | 'error' | 'no_data'`) matches all `statusStr` values produced by the tile's switch and the early returns. 
- Drill-down href `"/automation/data-quality"` is identical in backend `DrillDownHref` constant, all backend tests, and frontend `handleClick`. 
- Czech copy strings match between component implementation and frontend tests. 

Plan saved.
