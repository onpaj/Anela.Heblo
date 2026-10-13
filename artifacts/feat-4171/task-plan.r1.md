# TransportBoxBaseTile Drill-Down Filter Unit Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise `backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/TransportBoxBaseTile.cs` from 0.0% line coverage to at least the 60% threshold by adding a new xUnit test file that exercises every branch of `GenerateDrillDownFilters()` and both outcomes (success/error) of `LoadDataAsync()`.

**Architecture:** Test-only change — no production code is modified. A single new test file defines a minimal test-only concrete subclass of the abstract `TransportBoxBaseTile` (`TestTransportBoxTile`) whose `FilterStates` is constructor-configurable, letting each test drive a specific branch. `ITransportBoxRepository` is mocked with Moq. Anonymous-typed return values are asserted by round-tripping through `System.Text.Json.JsonSerializer.Serialize` → `JsonDocument.Parse`, matching the existing precedent in `InventorySummaryTileBaseTests`.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions, `System.Text.Json`.

---

### task: add-transport-box-base-tile-generate-drilldown-filter-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs`

- [ ] **Step 1: Write the failing tests for all four `GenerateDrillDownFilters` branches**

Create `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs` with this content:

```csharp
using Anela.Heblo.Application.Features.Logistics.DashboardTiles;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Moq;
using System.Text.Json;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.DashboardTiles;

public class TransportBoxBaseTileTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;

    public TransportBoxBaseTileTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
    }

    [Fact]
    public void GenerateDrillDownFilters_SingleState_ReturnsThatStateAsFilter()
    {
        // Arrange
        var tile = new TestTransportBoxTile(_repositoryMock.Object, new[] { TransportBoxState.Error });

        // Act
        var result = tile.CallGenerateDrillDownFilters();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("state").GetString().Should().Be("Error");
    }

    [Fact]
    public void GenerateDrillDownFilters_MultipleStatesAllNonClosed_ReturnsActiveSentinel()
    {
        // Arrange
        var tile = new TestTransportBoxTile(
            _repositoryMock.Object,
            new[] { TransportBoxState.New, TransportBoxState.Opened, TransportBoxState.InTransit });

        // Act
        var result = tile.CallGenerateDrillDownFilters();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("state").GetString().Should().Be("ACTIVE");
    }

    [Fact]
    public void GenerateDrillDownFilters_MultipleStatesIncludingClosed_ReturnsFirstStateNotActiveSentinel()
    {
        // Arrange
        var tile = new TestTransportBoxTile(
            _repositoryMock.Object,
            new[] { TransportBoxState.InTransit, TransportBoxState.Closed });

        // Act
        var result = tile.CallGenerateDrillDownFilters();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var state = doc.RootElement.GetProperty("state").GetString();
        state.Should().Be("InTransit");
        state.Should().NotBe("ACTIVE");
    }

    [Fact]
    public void GenerateDrillDownFilters_EmptyFilterStates_ReturnsEmptyObject()
    {
        // Arrange
        var tile = new TestTransportBoxTile(_repositoryMock.Object, Array.Empty<TransportBoxState>());

        // Act
        var result = tile.CallGenerateDrillDownFilters();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.EnumerateObject().Should().BeEmpty();
    }

    // Test-only concrete subclass: TransportBoxBaseTile is abstract and its FilterStates is
    // fixed per real tile (see ErrorBoxesTile, InTransitBoxesTile, ReceivedBoxesTile), so no
    // existing concrete tile can express every branch under test here. This type exists only to
    // make FilterStates configurable per test case and to expose the protected
    // GenerateDrillDownFilters() method for direct invocation. It is defined in the test
    // assembly only, is never registered with the dashboard tile registry, and is invisible to
    // TileIdContractTests (which scans only the Anela.Heblo.Xcc and Anela.Heblo.Application
    // production assemblies), so it needs no [TileId(...)] attribute.
    private sealed class TestTransportBoxTile : TransportBoxBaseTile
    {
        public override string Title => "Test Tile";
        public override string Description => "Test Tile Description";

        private readonly TransportBoxState[] _filterStates;
        protected override TransportBoxState[] FilterStates => _filterStates;

        public TestTransportBoxTile(ITransportBoxRepository repository, TransportBoxState[] filterStates)
            : base(repository)
        {
            _filterStates = filterStates;
        }

        public object CallGenerateDrillDownFilters() => GenerateDrillDownFilters();
    }
}
```

- [ ] **Step 2: Run the new tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxBaseTileTests"`
Expected: 4 tests discovered (`GenerateDrillDownFilters_SingleState_ReturnsThatStateAsFilter`, `GenerateDrillDownFilters_MultipleStatesAllNonClosed_ReturnsActiveSentinel`, `GenerateDrillDownFilters_MultipleStatesIncludingClosed_ReturnsFirstStateNotActiveSentinel`, `GenerateDrillDownFilters_EmptyFilterStates_ReturnsEmptyObject`), all passing. (These tests target existing, already-correct production logic, so they are expected to pass immediately — there is no red/green cycle against unwritten production code here, only against the new test file itself; if any fails, the test file has a bug, not `TransportBoxBaseTile.cs`.)

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs
git commit -m "test(logistics): cover TransportBoxBaseTile.GenerateDrillDownFilters branches"
```

---

### task: add-transport-box-base-tile-load-data-async-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs`

- [ ] **Step 1: Add the required `using` directives**

At the top of `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs`, the current using block is:

```csharp
using Anela.Heblo.Application.Features.Logistics.DashboardTiles;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Moq;
using System.Text.Json;
using Xunit;
```

Change it to (adding `System.Linq.Expressions`, needed for `Expression<Func<TransportBox, bool>>` in the repository mock setup):

```csharp
using Anela.Heblo.Application.Features.Logistics.DashboardTiles;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Moq;
using System.Linq.Expressions;
using System.Text.Json;
using Xunit;
```

- [ ] **Step 2: Write the failing test for the `LoadDataAsync` success path**

Immediately after the `GenerateDrillDownFilters_EmptyFilterStates_ReturnsEmptyObject` test method (before the `TestTransportBoxTile` nested class), add:

```csharp

    [Fact]
    public async Task LoadDataAsync_RepositorySucceeds_ReturnsSuccessStatusWithCount()
    {
        // Arrange
        var boxes = new List<TransportBox> { new(), new(), new() };
        _repositoryMock
            .Setup(x => x.FindAsync(
                It.IsAny<Expression<Func<TransportBox, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(boxes);

        var tile = new TestTransportBoxTile(_repositoryMock.Object, new[] { TransportBoxState.Error });

        // Act
        var result = await tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt32().Should().Be(3);
    }
```

- [ ] **Step 3: Write the failing test for the `LoadDataAsync` exception path**

Immediately after the test added in Step 2, add:

```csharp

    [Fact]
    public async Task LoadDataAsync_RepositoryThrows_ReturnsErrorShapeWithExceptionMessage()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.FindAsync(
                It.IsAny<Expression<Func<TransportBox, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        var tile = new TestTransportBoxTile(_repositoryMock.Object, new[] { TransportBoxState.Error });

        // Act
        var result = await tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("error").GetString().Should().Be("Nepodařilo se načíst počet boxů");
        doc.RootElement.GetProperty("details").GetString().Should().Be("Database connection failed");
    }
```

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxBaseTileTests"`
Expected: 6 tests discovered (the 4 from the previous task plus `LoadDataAsync_RepositorySucceeds_ReturnsSuccessStatusWithCount` and `LoadDataAsync_RepositoryThrows_ReturnsErrorShapeWithExceptionMessage`), all passing.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs
git commit -m "test(logistics): cover TransportBoxBaseTile.LoadDataAsync success and error paths"
```

---

### task: verify-transport-box-base-tile-build-format-and-coverage

**Files:**
- None modified — this task only runs verification commands against the changes made in the two prior tasks.

- [ ] **Step 1: Full solution build**

Run: `cd backend && dotnet build`
Expected: `Build succeeded.` with 0 errors. Warning count must not increase versus the pre-change baseline.

- [ ] **Step 2: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: exits 0 (no formatting violations). If it reports violations in `TransportBoxBaseTileTests.cs`, run `dotnet format` (without `--verify-no-changes`), then re-stage and amend whichever of the two prior commits touched the file most recently — do not create a separate "fix formatting" commit for a change this small.

- [ ] **Step 3: Run the full backend test suite**

Run: `cd backend && dotnet test`
Expected: all tests pass, including the 6 new tests in `TransportBoxBaseTileTests`, with no regressions introduced elsewhere (this is a purely additive new test file — no production code or other test file changes, so no other test should be affected).

- [ ] **Step 4: Confirm coverage of `TransportBoxBaseTile.cs` clears the 60% threshold**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --collect:"XPlat Code Coverage"`
Then locate the generated `coverage.cobertura.xml` under `test/Anela.Heblo.Tests/TestResults/<run-guid>/` and check the line-rate for the `TransportBoxBaseTile` class:

Run: `grep -A 2 'filename=".*TransportBoxBaseTile.cs"' backend/test/Anela.Heblo.Tests/TestResults/*/coverage.cobertura.xml | head -5`

Expected: the `<class ... filename="...TransportBoxBaseTile.cs" line-rate="...">` entry shows a `line-rate` of at least `0.6`. All executable lines in `GenerateDrillDownFilters()` (three branch conditions + three returns) and in `LoadDataAsync()` (the `try` body's `return`, the `catch` body's `return`) are exercised by the 6 tests added in this plan; the only lines not exercised are the metadata-only property getters (`Title`, `Description`, `Size`, `Category`, etc. on `ITile`), which are trivial single-expression getters not called by any test path — if the resulting line-rate is still below 0.6 because of these, this is expected and does not indicate a gap in the branch/exception-path coverage this plan targets; re-check against the brief's specific concern (drill-down filter branches and the `LoadDataAsync` error path) rather than chasing 100%.

- [ ] **Step 5: Commit (only if Step 2 required a formatting fix that amended a prior commit; otherwise skip)**

```bash
git status
```

Expected: clean working tree (nothing to commit) if Steps 1–4 all passed and no formatting fix was needed.

---

## Self-Review

**Spec coverage:**
- FR-1 (single-state branch) → `add-transport-box-base-tile-generate-drilldown-filter-tests`, `GenerateDrillDownFilters_SingleState_ReturnsThatStateAsFilter`.
- FR-2 (multi-state, all non-Closed → ACTIVE sentinel) → `add-transport-box-base-tile-generate-drilldown-filter-tests`, `GenerateDrillDownFilters_MultipleStatesAllNonClosed_ReturnsActiveSentinel`.
- FR-3 (multi-state with Closed present → first state, not ACTIVE) → `add-transport-box-base-tile-generate-drilldown-filter-tests`, `GenerateDrillDownFilters_MultipleStatesIncludingClosed_ReturnsFirstStateNotActiveSentinel`.
- FR-4 (empty fallback) → `add-transport-box-base-tile-generate-drilldown-filter-tests`, `GenerateDrillDownFilters_EmptyFilterStates_ReturnsEmptyObject`.
- FR-5 (`LoadDataAsync` success shape) → `add-transport-box-base-tile-load-data-async-tests`, `LoadDataAsync_RepositorySucceeds_ReturnsSuccessStatusWithCount`.
- FR-6 (`LoadDataAsync` exception path) → `add-transport-box-base-tile-load-data-async-tests`, `LoadDataAsync_RepositoryThrows_ReturnsErrorShapeWithExceptionMessage`.
- NFR-1 (no production behavior change) → satisfied structurally: no task modifies `TransportBoxBaseTile.cs` or any other production file.
- NFR-2 (coverage ≥ 60%) → `verify-transport-box-base-tile-build-format-and-coverage`, Step 4.
- NFR-3 (framework/convention consistency) → satisfied structurally: xUnit/Moq/FluentAssertions/`JsonDocument` pattern matches `InventorySummaryTileBaseTests` exactly, per arch-review Decision 2 and 3.

**Placeholder scan:** No "TBD"/"TODO"/"handle edge cases" language used; every step shows complete, exact code or an exact command with an expected result.

**Type consistency:** `TestTransportBoxTile` constructor signature (`ITransportBoxRepository repository, TransportBoxState[] filterStates`) and its `CallGenerateDrillDownFilters()` method name are defined once in the first task and used identically, unchanged, in the second task's new tests.
