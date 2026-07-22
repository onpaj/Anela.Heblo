# Architecture Review: Deterministic cutoff date in InventoryCountTileBase

## Skip Design: true

## Architectural Fit Assessment
This is a one-line internal bug fix plus test coverage in an existing, well-established pattern: `InventoryCountTileBase` (`backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventoryCountTileBase.cs`) already receives `TimeProvider` via constructor injection and already uses it correctly for the `date`/`lastUpdated` fields — only the cutoff calculation on line 38 bypasses it with `DateTime.UtcNow`. No new component, interface, or dependency is introduced; the fix aligns the class with a pattern the codebase already uses elsewhere (e.g. `TodayProductionTile`/`NextDayProductionTile` in Manufacture, tested with `FakeTimeProvider`). No architectural amendments are needed — the spec is implementation-ready as written.

## Proposed Architecture

### Component Overview
No new components. Single-line change inside `InventoryCountTileBase.LoadDataAsync`, plus a new test file alongside the existing sibling test `InventorySummaryTileBaseTests.cs`.

```
InventoryCountTileBase.LoadDataAsync
  ├─ cutoffDate = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-DaysOffset)   [FIX: was DateTime.UtcNow]
  ├─ catalogItems = _catalogRepository.GetAllAsync()
  └─ count = catalogItems.Where(ItemFilter).Count(w => w.LastStockTaking.HasValue && w.LastStockTaking.Value >= cutoffDate)
```

### Key Design Decisions

#### Decision 1: Use `_timeProvider.GetUtcNow().UtcDateTime` for the cutoff
**Options considered:** (a) leave `DateTime.UtcNow` and test with wide tolerance windows (as `InventorySummaryTileBaseTests` was forced to do, per its own boundary-precision comment); (b) inject `TimeProvider` for the cutoff too, matching what the class already does for the display fields.
**Chosen approach:** (b), exactly as specified in FR-1.
**Rationale:** `TimeProvider` is already a constructor dependency of this exact class — this isn't introducing a new pattern, it's finishing one that was half-applied. It also removes the wall-clock coupling that made `InventorySummaryTileBaseTests` need a documented boundary-precision workaround; this test class won't need that workaround.

## Implementation Guidance

### Directory / Module Structure
- Fix: `backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventoryCountTileBase.cs`, line 38 only.
- New tests: `backend/test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/InventoryCountTileBaseTests.cs` — same directory as the existing `InventorySummaryTileBaseTests.cs` and `LowStockAlertTileTests.cs`.

### Interfaces and Contracts
No interface or contract changes. Test against the existing concrete subclass `ProductInventoryCountTile` (or `MaterialInventoryCountTile`) rather than adding a new test-only subclass — both already take `(ICatalogRepository, TimeProvider)` and set `DaysOffset = 30`, matching the constructor pattern used by `InventorySummaryTileBaseTests` (which instantiates `ProductInventorySummaryTile` directly). For FR-2 Test 4 (custom `DaysOffset`), add a small private nested test subclass of `InventoryCountTileBase` that sets a non-default `DaysOffset` in its constructor — same technique already used for tile subclassing elsewhere in this codebase.

Established test conventions to follow (verified in-repo, do not deviate):
- Mocking library: **Moq** (`Mock<ICatalogRepository>`), consistent with `InventorySummaryTileBaseTests.cs` and the rest of `backend/test`.
- Clock faking: **`Microsoft.Extensions.Time.Testing.FakeTimeProvider`**, from the already-referenced package `Microsoft.Extensions.TimeProvider.Testing` (`Anela.Heblo.Tests.csproj` line 26). Confirmed usage pattern in `UpcomingProductionTileTests.cs`: construct with a fixed `DateTimeOffset`, pass into the tile's constructor. Do not introduce a hand-rolled `TimeProvider` stub or a different package.
- Assembling test items: build `CatalogAggregate` and populate `StockTakingHistory` with `StockTakingRecord { Date = ... }` to set `LastStockTaking`, matching `InventorySummaryTileBaseTests.CreateItem`.
- Assertions: `FluentAssertions` + `JsonSerializer.Serialize`/`JsonDocument.Parse` to read the anonymous-object payload (`data.count`), matching the existing sibling test's approach — simpler than the reflection-based `GetAnonymousProperty` helper used in `UpcomingProductionTileTests.cs`, and consistent with the closer sibling (`InventorySummaryTileBaseTests`) since both test classes exercise the same `Catalog/DashboardTiles` payload shape.

### Data Flow
Unchanged. `LoadDataAsync` → `ICatalogRepository.GetAllAsync()` → filter by `ItemFilter` (type) → count by `LastStockTaking >= cutoffDate` → anonymous result object. Only the source of "now" for `cutoffDate` changes from `DateTime.UtcNow` to `_timeProvider.GetUtcNow().UtcDateTime`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| New tests silently rely on wall-clock time instead of the fake, reintroducing flakiness | Low | Freeze time via `FakeTimeProvider` constructed with a fixed `DateTimeOffset`; assert `cutoffDate` behavior purely relative to that frozen value, never `DateTime.UtcNow`. |
| Regression in `InventorySummaryTileBaseTests` (unrelated sibling class, still uses raw `DateTime.UtcNow`) | Negligible | Out of scope per spec; this fix doesn't touch `InventorySummaryTileBase`. Run full test suite to confirm no incidental breakage. |

## Specification Amendments
None. The spec's suggested test subclass approach and `FakeTimeProvider` reference match what's already used in this codebase (`UpcomingProductionTileTests.cs`), so no changes are needed. One clarification for the implementer: prefer testing through the existing concrete subclasses (`ProductInventoryCountTile`/`MaterialInventoryCountTile`) for Tests 1–3, and only add a minimal private test subclass for Test 4's custom-`DaysOffset` case — mirrors how `InventorySummaryTileBaseTests` tests the base class exclusively through a real concrete subclass.

## Prerequisites
None. No new packages (the `FakeTimeProvider` package is already referenced), no migrations, no config changes.
