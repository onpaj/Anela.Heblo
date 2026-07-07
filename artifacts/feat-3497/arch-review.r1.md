# Architecture Review: Unit test coverage for InventorySummaryTileBase age-bucket logic

## Skip Design: true

## Architectural Fit Assessment

This is a pure test-addition task with zero production code changes, confirmed by reading `backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventorySummaryTileBase.cs`. The class is a straightforward `ITile` implementation: it pulls `ICatalogRepository.GetAllAsync`, filters via an abstract `ItemFilter`, buckets by `(DateTime.UtcNow - item.LastStockTaking.Value).TotalDays` against two `const double` thresholds (`ThresholdCritical = 180`, `ThresholdWarning = 365`), and returns an anonymous-object JSON payload wrapped in a try/catch. This matches exactly the shape already covered by the sibling test `backend/test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/LowStockAlertTileTests.cs`, which tests `LowStockAlertTile` (a different concrete `ITile`) using `Mock<ICatalogRepository>` + `JsonSerializer`/`JsonDocument` round-tripping to assert on the anonymous response shape. No new abstractions, interfaces, or test infrastructure are needed — this fits the existing test conventions in the repo directly.

One material difference from the sibling class: `LowStockAlertTile` takes an injected `TimeProvider` (mockable, fixed `_fixedDateTime`), but `InventorySummaryTileBase` calls `DateTime.UtcNow` directly with no seam. The spec correctly identifies this — production code is out of scope, so tests must anchor to real-world `DateTime.UtcNow` at Arrange time.

## Proposed Architecture

### Component Overview

No new components. Tests exercise the existing `InventorySummaryTileBase.LoadDataAsync` through one of its existing concrete subclasses in `backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/` (`ProductInventorySummaryTile`, `MaterialWithExpirationInventorySummaryTile`, `MaterialWithoutExpirationInventorySummaryTile`) — no test double or new subclass is required.

### Key Design Decisions

#### Decision 1: Which concrete tile to instantiate
**Options considered:**
- Write a private test-only subclass of `InventorySummaryTileBase` inline in the test file (mirrors nothing in the codebase today; adds a throwaway type).
- Instantiate an existing production subclass, e.g. `ProductInventorySummaryTile`.

**Chosen approach:** Use `ProductInventorySummaryTile` (`backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/ProductInventorySummaryTile.cs`). Its constructor is `ProductInventorySummaryTile(ICatalogRepository catalogRepository)` — no `TimeProvider` or other dependency to fake, so setup is a single `Mock<ICatalogRepository>`. Its `ItemFilter` is `c => c.Type == ProductType.Product || c.Type == ProductType.Goods`, which conveniently gives a natural "excluded" type to use for FR-4 (`ProductType.Material` or `ProductType.SemiProduct` items are filtered out, exactly as `LowStockAlertTileTests` already does with `MAT001`/`SEMI001`).

**Rationale:** Reusing a real, already-registered subclass keeps the test honest (it exercises the actual composition the app ships, including the `[TileId(...)]` attribute path) and avoids introducing a new type purely for test convenience, consistent with "no production code changes" and "surgical changes" guidance.

#### Decision 2: How to set `LastStockTaking` on `CatalogAggregate`
**Options considered:**
- Look for a settable `LastStockTaking` property on `CatalogAggregate` and assign it directly.
- Populate `StockTakingHistory` with a `StockTakingRecord`.

**Chosen approach:** `CatalogAggregate.LastStockTaking` (`backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs:158`) is a **computed, get-only** property: `=> StockTakingHistory.OrderByDescending(o => o.Date).FirstOrDefault()?.Date`. It cannot be assigned directly. Tests must populate `StockTakingHistory` (a `List<StockTakingRecord>`, `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockTakingRecord.cs`) with one record whose `Date` is the desired offset from `DateTime.UtcNow`. For the "never" bucket (FR-3), leave `StockTakingHistory` empty (default `new()`), which makes `LastStockTaking` return `null`.

**Rationale:** This is a hard fact about the domain model that the spec's "Data Model note" doesn't call out; getting it wrong (e.g. trying to set a nonexistent `LastStockTaking` setter) would make the tests fail to compile.

#### Decision 3: Boundary date construction without a mockable clock
**Options considered:**
- Assert exact fractional-day boundaries (e.g. exactly 180.000 days) — infeasible and flaky, since `LoadDataAsync` captures its own `DateTime.UtcNow` at Act time, a few milliseconds after the test's Arrange-time `DateTime.UtcNow`.
- Use whole-day offsets one day off the threshold in each direction, matching the FR wording itself ("179→recent, 180→medium").

**Chosen approach:** Capture `var now = DateTime.UtcNow;` once at the top of each boundary test, then set `StockTakingHistory` dates as `now.AddDays(-179)` / `now.AddDays(-180)` / `now.AddDays(-365)` / `now.AddDays(-366)`. Because production code's internal `now` is always fractionally later than the test's captured `now`, a `-180` day offset always evaluates to slightly *more* than 180.0 elapsed days by the time `LoadDataAsync` runs (correctly landing in `medium`, since the comparison is `>= ThresholdCritical`), and a `-179` day offset always evaluates to comfortably less than 180 days (correctly landing in `recent`). This gives deterministic, non-flaky tests without needing to touch production code to inject a clock.

**Rationale:** Matches the spec's own FR wording (which already builds in a 1-day margin rather than demanding sub-second precision) and avoids the false step of trying to freeze `DateTime.UtcNow` globally (e.g. via `Microsoft.Extensions.Time.Testing.FakeTimeProvider` or shims), which isn't wired into this class and is explicitly out of scope.

## Implementation Guidance

### Directory / Module Structure

New file only:
```
backend/test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/InventorySummaryTileBaseTests.cs
```
Namespace: `Anela.Heblo.Tests.Features.Catalog.DashboardTiles` (matches `LowStockAlertTileTests.cs` in the same directory).

### Interfaces and Contracts

No new interfaces. Reuse exactly what `LowStockAlertTileTests` uses:
- `Anela.Heblo.Application.Features.Catalog.DashboardTiles` (for `ProductInventorySummaryTile`)
- `Anela.Heblo.Domain.Features.Catalog` (for `CatalogAggregate`, `ICatalogRepository`, `ProductType`)
- `Anela.Heblo.Domain.Features.Catalog.Stock` (for `StockTakingRecord`)
- `Moq`, `FluentAssertions`, `System.Text.Json`, `Xunit`

Suggested private fixture helper, mirroring `CreateProductWithStock` in the sibling file:
```csharp
private CatalogAggregate CreateItem(string code, ProductType type, DateTime? lastStockTaking)
{
    var item = new CatalogAggregate { ProductCode = code, Type = type };
    if (lastStockTaking.HasValue)
    {
        item.StockTakingHistory.Add(new StockTakingRecord { Date = lastStockTaking.Value });
    }
    return item;
}
```
(`StockTakingRecord.Code`/`AmountNew`/`AmountOld` are irrelevant to this logic and can be left at defaults — only `Date` matters for bucket assignment.)

### Data Flow

Test → `Mock<ICatalogRepository>.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(items)` → `new ProductInventorySummaryTile(catalogRepositoryMock.Object)` → `await tile.LoadDataAsync()` → serialize result via `JsonSerializer.Serialize` + `JsonDocument.Parse`, exactly as `LowStockAlertTileTests` does, then assert on `data.recent` / `data.medium` / `data.old` / `data.never` / `data.total` ints and `status` string.

Recommended test cases (one `[Fact]` each, or a `[Theory]` with `[InlineData]` for the four boundary directions if preferred — sibling file uses discrete `[Fact]`s, so match that):
1. `LoadDataAsync_ItemAt179Days_CountsAsRecent`
2. `LoadDataAsync_ItemAt180Days_CountsAsMedium` (FR-1)
3. `LoadDataAsync_ItemAt365Days_CountsAsMedium`
4. `LoadDataAsync_ItemAt366Days_CountsAsOld` (FR-2)
5. `LoadDataAsync_ItemWithNullLastStockTaking_CountsAsNever` (FR-3)
6. `LoadDataAsync_MixedItemsWithFilteredOutType_TotalExcludesFilteredItems` (FR-4) — include e.g. one `recent`, one `medium`, one `Material`-type item (excluded by `ItemFilter`), assert `total == recent+medium+old+never` and the Material item isn't counted anywhere.
7. `LoadDataAsync_HappyPath_ReturnsSuccessStatusAndExpectedShape` (FR-5) — assert `status == "success"` and that `data` has all five keys with sane values for a small mixed set.
8. Optionally, one exception-path test mirroring `LoadDataAsync_HandlesExceptions_ReturnsErrorStatus` from the sibling file (incidental coverage per the spec's "no exhaustive exception-path testing beyond incidental" note) — cheap to add since the try/catch is already generic and untested for this class.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Real-clock (`DateTime.UtcNow`) boundary tests could theoretically flake if a test runs exactly at a day-rollover instant | Low | Use whole-day offsets one day away from each threshold (179/180, 365/366) as designed in Decision 3 — margin comfortably absorbs sub-second test execution time |
| Choosing `ProductInventorySummaryTile` ties these "base class" tests to one concrete subclass's `ItemFilter`, slightly obscuring that the behavior under test is really in the base class | Low | Add a class-level XML doc comment on the test class stating it exercises `InventorySummaryTileBase.LoadDataAsync` via `ProductInventorySummaryTile` as a representative concrete instance |
| Future contributors might duplicate these bucket tests again per-subclass (`MaterialWithExpirationInventorySummaryTile`, etc.), inflating redundant coverage | Low | The doc comment above also signals "don't re-test bucket math per subclass" — only subclass-specific `ItemFilter`/`GenerateDrillDownFilters` differences warrant their own tests |

## Specification Amendments

None required — the spec is accurate and implementable as written. Two clarifications worth folding into the spec text (non-blocking, just so an implementer doesn't have to re-derive them):
- `CatalogAggregate.LastStockTaking` is a computed property backed by `StockTakingHistory` (`List<StockTakingRecord>`); it has no setter, so fixtures must add a `StockTakingRecord` with the desired `Date` rather than assigning `LastStockTaking` directly.
- Use `ProductInventorySummaryTile` (concrete, single-dependency constructor) as the test host — no new test double needed.

## Prerequisites

None. All required packages (Moq, FluentAssertions, System.Text.Json, Xunit) are already referenced by the test project (confirmed via `LowStockAlertTileTests.cs`), and no other files need to change before this test file can be added.
