# Design: Unit Test Coverage for TransportBoxBaseTile Drill-Down Filters

## Component Design

### `TransportBoxBaseTileTests` (new test class)
- Location: `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs`
- Namespace: `Anela.Heblo.Tests.Features.Logistics.DashboardTiles`
- Responsibility: exercises all branches of `TransportBoxBaseTile.GenerateDrillDownFilters()` (single-state, multi-state active/ACTIVE, multi-state with `Closed` present, empty fallback) and both outcomes of `TransportBoxBaseTile.LoadDataAsync()` (success shape, exception/error shape).
- Dependencies: `Mock<ITransportBoxRepository>` (Moq), constructed fresh per test via the class's default constructor pattern (no shared mutable state between tests — each `[Fact]` builds its own mock and tile instance, consistent with `InventorySummaryTileBaseTests`, which instead shares one `_tile`/`_catalogRepositoryMock` pair built in the test class constructor; either per-test or per-class-constructor instantiation is acceptable, the developer should pick whichever keeps each `[Fact]`'s `FilterStates` value clearly visible in the test body — likely per-test construction here since `FilterStates` varies by test, unlike the shared-fixture case in `InventorySummaryTileBaseTests`).

### `TestTransportBoxTile` (new private test-double class)
- Location: nested private class inside `TransportBoxBaseTileTests.cs` (test-only, not registered anywhere, invisible to `TileIdContractTests` since it only scans production assemblies — confirmed in arch-review).
- Responsibility: makes `TransportBoxBaseTile.FilterStates` configurable per test case (the abstract base class has no other way to vary this), and exposes the `protected virtual GenerateDrillDownFilters()` method for direct, repository-mock-free invocation in FR-1–FR-4.
- Interface:
  ```csharp
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
  ```
- No production code is touched or added. `TransportBoxBaseTile.cs` remains unchanged.

## Data Schemas

No new data schemas, DTOs, or API payload shapes are introduced. Tests assert against the two existing anonymous-typed shapes already produced by `TransportBoxBaseTile`, via `System.Text.Json.JsonSerializer.Serialize(result)` → `JsonDocument.Parse(json)` (per arch-review Decision 2):

**`GenerateDrillDownFilters()` return shapes (asserted directly via `CallGenerateDrillDownFilters()`):**
- Single-state / multi-state-with-Closed fallback: `{ "state": "<TransportBoxState enum name>" }`
- Multi-state, all non-`Closed`: `{ "state": "ACTIVE" }`
- Empty (`FilterStates.Length == 0`): `{}` — no properties.

**`LoadDataAsync()` return shapes (asserted via mocked `ITransportBoxRepository.FindAsync`):**
- Success:
  ```json
  {
    "status": "success",
    "data": { "count": <int> },
    "metadata": { "lastUpdated": "<datetime>", "source": "TransportBoxRepository" },
    "drillDown": { "filters": { ... }, "enabled": true, "tooltip": "<string>" }
  }
  ```
  Tests assert `status` and `data.count` only (per FR-5) — `metadata`/`drillDown`/`tooltip` shape is out of scope for this coverage fix.
- Error (repository throws):
  ```json
  {
    "status": "error",
    "error": "Nepodařilo se načíst počet boxů",
    "details": "<exception.Message>"
  }
  ```
  Tests assert all three fields (per FR-6).
