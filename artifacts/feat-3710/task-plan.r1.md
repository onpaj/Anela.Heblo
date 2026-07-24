# Implementation Plan: Deterministic cutoff date in InventoryCountTileBase

### task: fix-cutoff-timeprovider-and-add-tests

## Goal
Replace the non-deterministic `DateTime.UtcNow` cutoff calculation in `InventoryCountTileBase.LoadDataAsync` with the already-injected `TimeProvider`, and add unit tests that pin "now" to cover the previously-untested filter logic (inclusive boundary, exclusion, null-guard, custom `DaysOffset`).

## Context
`InventoryCountTileBase` (`backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventoryCountTileBase.cs`) already takes `TimeProvider` via constructor injection and uses it correctly for the `date`/`lastUpdated` fields (lines 51, 55), but line 38 independently computes the filter cutoff with `DateTime.UtcNow.AddDays(-DaysOffset)`. This makes the count filter untestable and inconsistent with the "now" shown to the user. Full current method body (lines 34–74) confirmed by reading the file:

```csharp
public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
{
    try
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-DaysOffset);   // line 38 — bug
        var catalogItems = await _catalogRepository.GetAllAsync(cancellationToken);

        var count = catalogItems
            .Where(ItemFilter)
            .Count(w => w.LastStockTaking.HasValue && w.LastStockTaking.Value >= cutoffDate);
        ...
```

Fix: change line 38 to `var cutoffDate = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-DaysOffset);`. No other line changes — return shape, error handling, and the `date`/`lastUpdated` fields are already correct.

Concrete subclasses to test through: `ProductInventoryCountTile` and `MaterialInventoryCountTile` (both in the same directory), constructed as `new ProductInventoryCountTile(catalogRepositoryMock.Object, timeProvider)` with `DaysOffset` fixed at 30 in their own constructors. For the custom-`DaysOffset` case (Test 4), add a minimal private nested test subclass of `InventoryCountTileBase` inside the test file that sets a non-default `DaysOffset`.

Established test conventions (verified in-repo, follow exactly, do not deviate):
- Mocking: **Moq** (`Mock<ICatalogRepository>`).
- Clock faking: **`Microsoft.Extensions.Time.Testing.FakeTimeProvider`** (already referenced in `Anela.Heblo.Tests.csproj`). Construct with a fixed `DateTimeOffset` and pass into the tile's constructor — pattern confirmed in `backend/test/Anela.Heblo.Tests/Features/Manufacture/DashboardTiles/UpcomingProductionTileTests.cs` (e.g. `new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero))`).
- Building test items: construct `CatalogAggregate { ProductCode = ..., Type = ... }` and add a `StockTakingRecord { Date = ... }` to `StockTakingHistory` to set `LastStockTaking` — pattern confirmed in `backend/test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/InventorySummaryTileBaseTests.cs` (`CreateItem` helper, lines 239–247). For the null case, simply don't add a `StockTakingRecord`.
- Assertions: `FluentAssertions` + `JsonSerializer.Serialize(result)` / `JsonDocument.Parse(json)` to read `data.count` from the anonymous-object payload — same approach as `InventorySummaryTileBaseTests.cs`. Do not use the reflection-based `GetAnonymousProperty` helper from `UpcomingProductionTileTests.cs`; the JSON approach is the closer sibling convention for this payload shape.
- Do **not** reintroduce a wall-clock-relative boundary workaround like the comment block at the top of `InventorySummaryTileBaseTests.cs` (lines 11–18) — that workaround exists only because `InventorySummaryTileBase` still uses raw `DateTime.UtcNow`; this fix removes the need for it here by pinning time via `FakeTimeProvider`.

## Files to create/modify
- `backend/src/Anela.Heblo.Application/Features/Catalog/DashboardTiles/InventoryCountTileBase.cs` — line 38: change `DateTime.UtcNow.AddDays(-DaysOffset)` to `_timeProvider.GetUtcNow().UtcDateTime.AddDays(-DaysOffset)`.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/InventoryCountTileBaseTests.cs` (new) — new test file, same directory as `InventorySummaryTileBaseTests.cs`.

## Implementation steps
1. In `InventoryCountTileBase.cs`, change line 38 from `var cutoffDate = DateTime.UtcNow.AddDays(-DaysOffset);` to `var cutoffDate = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-DaysOffset);`. No other lines change.
2. Create `InventoryCountTileBaseTests.cs` in `backend/test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/`, namespace `Anela.Heblo.Tests.Features.Catalog.DashboardTiles`, using `Moq`, `FluentAssertions`, `Microsoft.Extensions.Time.Testing`, `System.Text.Json`, `Xunit`.
3. Define a frozen `DateTimeOffset` constant (e.g. `2026-06-15T12:00:00Z`) used to build a `FakeTimeProvider` per test (or shared via constructor, matching whichever sibling pattern is cleaner — `UpcomingProductionTileTests` builds it per test via a `static readonly` field).
4. Add a private nested test subclass of `InventoryCountTileBase` for Test 4, e.g.:
   ```csharp
   private class CustomOffsetInventoryCountTile : InventoryCountTileBase
   {
       public CustomOffsetInventoryCountTile(ICatalogRepository repo, TimeProvider tp, int daysOffset) : base(repo, tp)
       {
           DaysOffset = daysOffset;
       }
       public override string Title => "Test";
       public override string Description => "Test";
       protected override Func<CatalogAggregate, bool> ItemFilter => c => true;
       protected override object GenerateDrillDownFilters() => new { };
   }
   ```
5. Write the four tests described below, each constructing `Mock<ICatalogRepository>`, a `FakeTimeProvider` pinned to the frozen time, the tile under test (`ProductInventoryCountTile` or the nested subclass), calling `LoadDataAsync()`, and asserting `data.count` via `JsonSerializer`/`JsonDocument`.
6. Run `dotnet build` and `dotnet format` on the backend; run the new test file plus the full `Anela.Heblo.Tests` suite to confirm no regressions (in particular `InventorySummaryTileBaseTests`, which is untouched and must still pass).

## Tests to write
1. **Item at exact cutoff is included**: `LastStockTaking` set to exactly `frozenNow.AddDays(-30)` (matching the tile's `DaysOffset`) → `data.count == 1` (boundary inclusive per existing `>=`).
2. **Item one second before cutoff is excluded**: `LastStockTaking` set to `frozenNow.AddDays(-30).AddSeconds(-1)` → `data.count == 0`.
3. **Null `LastStockTaking` is excluded**: item with no `StockTakingRecord` added (so `LastStockTaking == null`) → `data.count == 0`, no exception thrown.
4. **Custom `DaysOffset` shifts the cutoff**: using the nested test subclass with e.g. `DaysOffset = 7`, an item at `frozenNow.AddDays(-10)` (excluded under default 30-day window's own arithmetic is irrelevant here — construct the case so it's excluded under a 30-day window's boundary but included under the custom window, or more simply: use `DaysOffset = 7` and an item at `frozenNow.AddDays(-10)` should be **excluded** while an item at `frozenNow.AddDays(-5)` should be **included** — assert `data.count == 1` for a mixed set of both, proving the custom offset (not the default 30) drove the filter).

All four tests must construct `FakeTimeProvider` with a fixed `DateTimeOffset` and derive all item timestamps from that same frozen value — never from `DateTime.UtcNow`.

## Acceptance criteria
- [ ] Line 38 of `InventoryCountTileBase.cs` no longer references `DateTime.UtcNow`; cutoff is `_timeProvider.GetUtcNow().UtcDateTime.AddDays(-DaysOffset)`.
- [ ] No other behavior of `LoadDataAsync` changed (return shape, error handling, `date`/`lastUpdated` fields unchanged).
- [ ] `InventoryCountTileBaseTests.cs` exists with the 4 tests above, all passing deterministically regardless of wall-clock time (verify by running twice or reasoning that no test reads `DateTime.UtcNow`).
- [ ] Existing tests, especially `InventorySummaryTileBaseTests.cs`, still pass unmodified.
- [ ] `dotnet build` and `dotnet format` succeed with no new warnings/diffs in touched files.
- [ ] Full `Anela.Heblo.Tests` suite passes.
