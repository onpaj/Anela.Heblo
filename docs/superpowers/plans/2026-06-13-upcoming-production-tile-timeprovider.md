# TimeProvider Consistency in UpcomingProductionTile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate hard-coded `DateTime.Today` / `DateTime.UtcNow` calls in `UpcomingProductionTile` by injecting `TimeProvider` into the base class, then add unit tests that verify drill-down behavior under a `FakeTimeProvider`.

**Architecture:** Add a `private readonly TimeProvider _timeProvider` field to the abstract `UpcomingProductionTile` and accept it via constructor. Replace the three wall-clock call sites (`LoadDataAsync` line 50, `GenerateDrillDownFilters` lines 65 and 69) with `_timeProvider.GetUtcNow()`-derived values. Forward the `TimeProvider` already held by `TodayProductionTile` and `NextDayProductionTile` to `base(...)`. Cover the changes with new xUnit tests using `FakeTimeProvider` to match the convention established by `ProductionActivityAnalyzerTests`.

**Tech Stack:** .NET 8 / C#, xUnit, Moq, `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`), MediatR-based Manufacture module, framework-registered `TimeProvider.System` (no DI changes required).

---

## File Structure

**Production code (all modifications, no new files):**

| File | Responsibility | Change |
|------|----------------|--------|
| `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/UpcomingProductionTile.cs` | Abstract base tile; loads orders for `ReferenceDate` and emits dashboard JSON with drill-down. | Add `TimeProvider` field + ctor parameter; replace `DateTime.UtcNow` (line 50) and both `DateOnly.FromDateTime(DateTime.Today...)` calls (lines 65, 69) with `_timeProvider.GetUtcNow()`-derived values. |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/TodayProductionTile.cs` | Concrete tile, `ReferenceDate = today`. | Forward the already-injected `timeProvider` to `base(repository, timeProvider)`. |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/NextDayProductionTile.cs` | Concrete tile, `ReferenceDate = next working day`. | Forward the already-injected `timeProvider` to `base(repository, timeProvider)`. |

**Test code (new file):**

| File | Responsibility |
|------|----------------|
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/UpcomingProductionTileTests.cs` | xUnit tests covering: (a) `TodayProductionTile.GenerateDrillDownFilters()` returns `weekly` when `ReferenceDate == today`; (b) `NextDayProductionTile.GenerateDrillDownFilters()` returns `weekly` on a weekday and `grid` on a Friday (because `GetNextWorkingDay` skips to Monday); (c) `lastUpdated` metadata equals the `FakeTimeProvider`-configured time. |

**Out of scope:** `ManufactureModule.cs` (no DI change), `IManufactureOrderRepository` (no signature change), `ManualActionRequiredTile.cs` / `ManufactureConditionsTile.cs` (already correct), any other Manufacture-module clock cleanup tracked by issues #2676 and #2677.

---

### Task 1: Update `UpcomingProductionTile` base class to accept and use `TimeProvider`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/UpcomingProductionTile.cs`

**Context — current state of the file** (so the engineer does not need to re-read it):

```csharp
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Manufacture.DashboardTiles;

public abstract class UpcomingProductionTile : ITile
{
    private readonly IManufactureOrderRepository _repository;

    // ... metadata properties unchanged ...

    protected abstract DateOnly ReferenceDate { get; set; }

    protected UpcomingProductionTile(IManufactureOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<object> LoadDataAsync(...)
    {
        // ...
        return new
        {
            // ...
            metadata = new
            {
                lastUpdated = DateTime.UtcNow,          // ← line 50, replace
                source = "ManufactureOrderRepository"
            },
            // ...
        };
    }

    protected virtual object GenerateDrillDownFilters()
    {
        var dateString = ReferenceDate.ToString("yyyy-MM-dd");
        if (ReferenceDate == DateOnly.FromDateTime(DateTime.Today))          // ← line 65, replace
        {
            return new { date = dateString, view = "weekly" };
        }
        if (ReferenceDate == DateOnly.FromDateTime(DateTime.Today.AddDays(1))) // ← line 69, replace
        {
            return new { date = dateString, view = "weekly" };
        }
        return new { date = dateString, view = "grid" };
    }
}
```

- [ ] **Step 1: Add the `_timeProvider` field and constructor parameter**

Edit the field declaration block. Replace:

```csharp
    private readonly IManufactureOrderRepository _repository;
```

with:

```csharp
    private readonly IManufactureOrderRepository _repository;
    private readonly TimeProvider _timeProvider;
```

Then replace the constructor:

```csharp
    protected UpcomingProductionTile(IManufactureOrderRepository repository)
    {
        _repository = repository;
    }
```

with:

```csharp
    protected UpcomingProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }
```

No `using` directive change is required — `TimeProvider` lives in the `System` namespace, which is implicitly imported via `<ImplicitUsings>enable</ImplicitUsings>`.

- [ ] **Step 2: Replace `DateTime.UtcNow` in `LoadDataAsync` (was line 50)**

Replace:

```csharp
                lastUpdated = DateTime.UtcNow,
```

with:

```csharp
                lastUpdated = _timeProvider.GetUtcNow().UtcDateTime,
```

Rationale (per arch-review Decision 3): `.UtcDateTime` returns a value with `DateTimeKind.Utc`, eliminating downstream serializer ambiguity. The codebase has both `.UtcDateTime` (`ManufactureConditionsTile.cs:46`) and `.DateTime` (`ManualActionRequiredTile.cs:43`) — prefer the unambiguous one.

- [ ] **Step 3: Replace `DateTime.Today` in `GenerateDrillDownFilters` (was lines 65 and 69)**

Replace the entire `GenerateDrillDownFilters` method body:

```csharp
    protected virtual object GenerateDrillDownFilters()
    {
        var dateString = ReferenceDate.ToString("yyyy-MM-dd");
        if (ReferenceDate == DateOnly.FromDateTime(DateTime.Today))
        {
            return new { date = dateString, view = "weekly" };
        }
        if (ReferenceDate == DateOnly.FromDateTime(DateTime.Today.AddDays(1)))
        {
            return new { date = dateString, view = "weekly" };
        }
        return new { date = dateString, view = "grid" };
    }
```

with:

```csharp
    protected virtual object GenerateDrillDownFilters()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
        var dateString = ReferenceDate.ToString("yyyy-MM-dd");
        if (ReferenceDate == today)
        {
            return new { date = dateString, view = "weekly" };
        }
        if (ReferenceDate == today.AddDays(1))
        {
            return new { date = dateString, view = "weekly" };
        }
        return new { date = dateString, view = "grid" };
    }
```

Note: the `today` value is computed once and reused for both comparisons.

- [ ] **Step 4: Verify no clock primitive remains in the file**

Run:

```bash
grep -nE "DateTime\.(Today|Now|UtcNow)" backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/UpcomingProductionTile.cs
```

Expected: no matches (exit code 1, empty output).

- [ ] **Step 5: Do NOT compile/commit yet — Task 2 and Task 3 fix the subclass call sites**

The build will fail until subclasses are updated. Move directly to Task 2.

---

### Task 2: Forward `timeProvider` from `TodayProductionTile` to base

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/TodayProductionTile.cs`

- [ ] **Step 1: Update the base-class constructor call**

Replace:

```csharp
    public TodayProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider) : base(repository)
    {
        ReferenceDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().Date); // Get next workday
    }
```

with:

```csharp
    public TodayProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider) : base(repository, timeProvider)
    {
        ReferenceDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().Date); // Get next workday
    }
```

(Only the `base(...)` call changes — the body that derives `ReferenceDate` is unchanged.)

---

### Task 3: Forward `timeProvider` from `NextDayProductionTile` to base

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/NextDayProductionTile.cs`

- [ ] **Step 1: Update the base-class constructor call**

Replace:

```csharp
    public NextDayProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider) : base(repository)
    {
        ReferenceDate = GetNextWorkingDay(DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime));
    }
```

with:

```csharp
    public NextDayProductionTile(IManufactureOrderRepository repository, TimeProvider timeProvider) : base(repository, timeProvider)
    {
        ReferenceDate = GetNextWorkingDay(DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime));
    }
```

(Only the `base(...)` call changes — `GetNextWorkingDay` is untouched.)

---

### Task 4: Verify the solution compiles cleanly

**Files:** none (verification only).

- [ ] **Step 1: Confirm no other subclasses of `UpcomingProductionTile` exist**

Run:

```bash
grep -rln ": UpcomingProductionTile" backend/src
```

Expected output (exactly these two files):

```
backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/TodayProductionTile.cs
backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/NextDayProductionTile.cs
```

If any additional file appears, update its constructor with the same change as Task 2/Task 3 (`base(repository, timeProvider)` and forward an injected `TimeProvider` parameter).

- [ ] **Step 2: Build the backend**

Run:

```bash
cd backend && dotnet build
```

Expected: `Build succeeded. 0 Error(s)`. Warnings unrelated to this change are acceptable.

If `error CS7036` ("no argument given that corresponds to the required parameter 'timeProvider'") appears, a subclass constructor was missed — fix it before continuing.

- [ ] **Step 3: Format**

Run:

```bash
cd backend && dotnet format Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: exits successfully with no whitespace or analyzer diffs left over.

- [ ] **Step 4: Commit the production-code changes**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/UpcomingProductionTile.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/TodayProductionTile.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/NextDayProductionTile.cs
git commit -m "refactor: inject TimeProvider into UpcomingProductionTile base"
```

---

### Task 5: Write failing tests for `TodayProductionTile` drill-down (`weekly` branch)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/UpcomingProductionTileTests.cs`

The tests use `FakeTimeProvider` (per arch-review Decision 4, matches `ProductionActivityAnalyzerTests` convention) and `Mock<IManufactureOrderRepository>` (matches `ManualActionRequiredTileTests`).

- [ ] **Step 1: Create the test file with a `TodayProductionTile` drill-down test**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/UpcomingProductionTileTests.cs` with the following content:

```csharp
using System.Reflection;
using Anela.Heblo.Application.Features.Manufacture.DashboardTiles;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.DashboardTiles;

public class UpcomingProductionTileTests
{
    // Monday 2026-06-15 12:00 UTC — chosen so that ReferenceDate + 1 = Tuesday (a working day),
    // exercising the today+1 "weekly" branch in NextDayProductionTile.
    private static readonly DateTimeOffset FrozenMondayUtc =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    // Friday 2026-06-19 12:00 UTC — chosen so GetNextWorkingDay skips Sat/Sun and returns
    // Monday 2026-06-22, which equals neither today nor today+1, exercising the "grid" branch.
    private static readonly DateTimeOffset FrozenFridayUtc =
        new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IManufactureOrderRepository> _repositoryMock = new();

    [Fact]
    public async Task TodayProductionTile_GenerateDrillDownFilters_ReturnsWeeklyView()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(FrozenMondayUtc);
        var tile = new TodayProductionTile(_repositoryMock.Object, timeProvider);
        _repositoryMock
            .Setup(r => r.GetOrdersForDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureOrder>());

        // Act
        var payload = await tile.LoadDataAsync();
        var filters = GetAnonymousProperty(payload, "drillDown.filters");

        // Assert
        Assert.Equal("2026-06-15", GetAnonymousProperty(filters!, "date"));
        Assert.Equal("weekly", GetAnonymousProperty(filters!, "view"));
    }

    private static object? GetAnonymousProperty(object source, string path)
    {
        object? current = source;
        foreach (var name in path.Split('.'))
        {
            if (current is null)
            {
                return null;
            }
            var prop = current.GetType().GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance);
            current = prop?.GetValue(current);
        }
        return current;
    }
}
```

Notes for the engineer:
- The anonymous return type from `LoadDataAsync` has no compile-time accessor, so `GetAnonymousProperty` uses reflection to traverse `drillDown.filters.date` / `.view`.
- `BindingFlags.Public | BindingFlags.Instance` is enough; anonymous-type members are public-instance.
- The repository mock returns an empty list — we only care about the drill-down branch, not the data section.

- [ ] **Step 2: Run the test to verify it FAILS (red phase)**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpcomingProductionTileTests.TodayProductionTile_GenerateDrillDownFilters_ReturnsWeeklyView" \
  --no-restore
```

Expected: PASS.

Wait — this test is expected to **PASS** even before any further changes, because the production-code refactor (Tasks 1–4) has already wired `_timeProvider` through the base class. The TDD red/green cycle here is: *before* Task 1 this test would have failed (the production code would have compared against the real `DateTime.Today` and the comparison would have been false on most days). *After* Task 1 it correctly returns `weekly`. That is the regression the spec calls out in FR-4.

To convince yourself the test actually exercises the new code path: temporarily revert the change to line 65 (`if (ReferenceDate == today)` → `if (ReferenceDate == DateOnly.FromDateTime(DateTime.Today))`), re-run the test, and confirm it fails unless today's real wall-clock date happens to be 2026-06-15. Then restore the change. Do **not** commit this temporary revert.

If the test fails on the post-refactor code, debug the wiring before continuing.

---

### Task 6: Add the `NextDayProductionTile` weekday test (`weekly` branch via today+1)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/UpcomingProductionTileTests.cs`

- [ ] **Step 1: Add the test method**

Insert the following method into `UpcomingProductionTileTests` (above the private `GetAnonymousProperty` helper):

```csharp
    [Fact]
    public async Task NextDayProductionTile_OnWeekday_GenerateDrillDownFilters_ReturnsWeeklyView()
    {
        // Arrange: Monday → next working day = Tuesday = today + 1.
        var timeProvider = new FakeTimeProvider(FrozenMondayUtc);
        var tile = new NextDayProductionTile(_repositoryMock.Object, timeProvider);
        _repositoryMock
            .Setup(r => r.GetOrdersForDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureOrder>());

        // Act
        var payload = await tile.LoadDataAsync();
        var filters = GetAnonymousProperty(payload, "drillDown.filters");

        // Assert
        Assert.Equal("2026-06-16", GetAnonymousProperty(filters!, "date"));
        Assert.Equal("weekly", GetAnonymousProperty(filters!, "view"));
    }
```

- [ ] **Step 2: Run the test**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpcomingProductionTileTests.NextDayProductionTile_OnWeekday_GenerateDrillDownFilters_ReturnsWeeklyView" \
  --no-restore
```

Expected: PASS.

---

### Task 7: Add the `NextDayProductionTile` Friday test (`grid` branch)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/UpcomingProductionTileTests.cs`

This test fixes the spec's FR-4 misread (per arch-review Specification Amendment 1): on a Friday, `GetNextWorkingDay` skips Sat/Sun and returns Monday, which equals neither `today` (Fri) nor `today + 1` (Sat), so the drill-down falls through to `view = "grid"`.

- [ ] **Step 1: Add the test method**

Insert the following method into `UpcomingProductionTileTests` (above the private `GetAnonymousProperty` helper):

```csharp
    [Fact]
    public async Task NextDayProductionTile_OnFriday_GenerateDrillDownFilters_ReturnsGridView()
    {
        // Arrange: Friday → next working day = Monday, which is today + 3, neither today nor today+1.
        var timeProvider = new FakeTimeProvider(FrozenFridayUtc);
        var tile = new NextDayProductionTile(_repositoryMock.Object, timeProvider);
        _repositoryMock
            .Setup(r => r.GetOrdersForDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureOrder>());

        // Act
        var payload = await tile.LoadDataAsync();
        var filters = GetAnonymousProperty(payload, "drillDown.filters");

        // Assert
        Assert.Equal("2026-06-22", GetAnonymousProperty(filters!, "date"));
        Assert.Equal("grid", GetAnonymousProperty(filters!, "view"));
    }
```

- [ ] **Step 2: Run the test**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpcomingProductionTileTests.NextDayProductionTile_OnFriday_GenerateDrillDownFilters_ReturnsGridView" \
  --no-restore
```

Expected: PASS.

---

### Task 8: Add the `lastUpdated` metadata test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/UpcomingProductionTileTests.cs`

This test verifies FR-3: `lastUpdated` is sourced from the injected `TimeProvider`, not wall-clock.

- [ ] **Step 1: Add the test method**

Insert the following method into `UpcomingProductionTileTests` (above the private `GetAnonymousProperty` helper):

```csharp
    [Fact]
    public async Task LoadDataAsync_LastUpdated_ComesFromTimeProvider()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(FrozenMondayUtc);
        var tile = new TodayProductionTile(_repositoryMock.Object, timeProvider);
        _repositoryMock
            .Setup(r => r.GetOrdersForDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureOrder>());

        // Act
        var payload = await tile.LoadDataAsync();
        var lastUpdated = (DateTime)GetAnonymousProperty(payload, "metadata.lastUpdated")!;

        // Assert
        Assert.Equal(FrozenMondayUtc.UtcDateTime, lastUpdated);
        Assert.Equal(DateTimeKind.Utc, lastUpdated.Kind);
    }
```

The `Kind` assertion is what locks in Decision 3 (`.UtcDateTime` vs `.DateTime`). If a future maintainer regresses to `.DateTime`, the `Kind` assertion fires.

- [ ] **Step 2: Run the test**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpcomingProductionTileTests.LoadDataAsync_LastUpdated_ComesFromTimeProvider" \
  --no-restore
```

Expected: PASS.

---

### Task 9: Final verification, format, and commit

**Files:** none new (verification + commit only).

- [ ] **Step 1: Run the full new test class**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpcomingProductionTileTests" \
  --no-restore
```

Expected: `Passed: 4, Failed: 0, Skipped: 0`.

- [ ] **Step 2: Run the broader Manufacture DashboardTiles test scope to catch regressions**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Manufacture.DashboardTiles" \
  --no-restore
```

Expected: all tests pass — `UpcomingProductionTileTests` (4 new) and `ManualActionRequiredTileTests` (5 pre-existing) both green.

- [ ] **Step 3: Confirm DI resolution still works (acceptance criterion from FR-1)**

Run the full solution build to confirm no startup-time DI error was introduced:

```bash
cd backend && dotnet build
```

Expected: `Build succeeded. 0 Error(s)`.

Inspect `ManufactureModule.cs:67-68` and confirm the lines are unchanged:

```bash
grep -n "RegisterTile<TodayProductionTile>\|RegisterTile<NextDayProductionTile>" \
  backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs
```

Expected output:

```
67:        services.RegisterTile<TodayProductionTile>();
68:        services.RegisterTile<NextDayProductionTile>();
```

- [ ] **Step 4: Format the test project**

Run:

```bash
cd backend && dotnet format test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: exits successfully with no diffs.

- [ ] **Step 5: Commit the tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/UpcomingProductionTileTests.cs
git commit -m "test: cover UpcomingProductionTile drill-down under FakeTimeProvider"
```

---

## Self-Review Notes

**Spec coverage:**
- FR-1 (inject `TimeProvider` into base) → Tasks 1, 2, 3.
- FR-2 (replace `DateTime.Today` in `GenerateDrillDownFilters`) → Task 1 Step 3.
- FR-3 (replace `DateTime.UtcNow` in `LoadDataAsync`) → Task 1 Step 2, asserted by Task 8.
- FR-4 (time-shifted drill-down + `lastUpdated` tests) → Tasks 5, 6, 7, 8. FR-4's misreading of the `NextDayProductionTile` `grid` expectation is corrected per arch-review Specification Amendment 1 (Tasks 6 and 7 split the case by day-of-week).
- NFR-3 (backward compat) → Task 9 Step 3 confirms DI registrations and build are unchanged.
- NFR-4 (consistency: `GetUtcNow()`) → Task 1 Steps 2 and 3 pick `GetUtcNow()` matching the existing subclass convention.

**Architectural amendments applied:**
- Spec Amendment 1 (FR-4 expected-result error): incorporated into Tasks 6 and 7.
- Spec Amendment 2 (`.UtcDateTime`): incorporated into Task 1 Step 2 + Task 8 `Kind` assertion.
- Spec Amendment 3 (no DI change): Task 9 Step 3.
- Spec Amendment 4 (test placement): tests live at `backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/UpcomingProductionTileTests.cs`.
- Spec Amendment 5 (subclass scope = exactly two): Task 4 Step 1 verifies, no other subclasses to update.
