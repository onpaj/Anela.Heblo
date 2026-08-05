# Review: `CatalogMergeService`/`CatalogAggregate` clone-based isolation fix

## Diff reviewed
Commit `b5fcf141` (working tree is clean, this is the sole implementation commit):
- `CatalogMergeService.cs:82` — `products = catalogData;` → `products = catalogData.Select(p => p.Clone()).ToList();`
- `CatalogAggregate.cs` — new `Clone()` instance method
- `ManufactureDifficultyConfiguration.cs` — new `Clone()` instance method
- `CatalogMergeServiceTests.cs` — 6 new tests covering instance isolation across merge passes and the `Current`/`Stale` swap

## Design conformance
Implementation is a verbatim match of `design-01.md`'s `Clone()` code blocks — same field treatment (`MemberwiseClone` baseline; explicit `with { Lots = ... }` for `Stock`; `with { }` for `Properties`; `.Clone()` for `ManufactureDifficultySettings`; `.ToList()` for `StockTakingHistory`; fresh instance + dictionary copy for the three history summaries). The one-line `Merge()` change matches exactly what the design and plan specified. No scope creep, no unrelated changes.

## Correctness checks performed
- Read `CatalogAggregate.cs`, `StockData.cs` (record, `Lots: List<CatalogLot>`), `CatalogProperties.cs` (record), `ManufactureDifficultyConfiguration.cs` (private setters, `Clone()` legally uses object-initializer from within the same type), `SaleHistorySummary.cs` (public setters) — all match the assumptions the design relied on.
- Re-read `CatalogMergeService.cs` in full: every `Merge*` helper is unchanged and still does in-place field mutation on `product`/`product.Stock`/`product.Properties`/`product.ManufactureDifficultySettings` — correct, since post-`Clone()` these are fresh, unshared instances per pass.
- Verified the design's load-bearing assumption — that `EshopPrice`/`ErpPrice`/`SalesHistory`/`ConsumedHistory`/`PurchaseHistory`/`ManufactureHistory`/`Margins` are only ever wholesale-reassigned, never mutated in place anywhere in `backend/src` — via grep; no `.Add`/`.Clear`/property-mutation call sites found on these fields outside the setters themselves. Confirms `MemberwiseClone()` reference-sharing these fields until overlaid is safe.
- Confirmed `StockTakingHistory`/`Stock.Lots` cloning is necessary and sufficient given `SyncStockTaking`'s in-place `.Add()`/`.Clear()`/`.AddRange()` calls (an out-of-scope call site per the plan, but one the `Clone()` design correctly neutralizes for isolation purposes without touching that method itself).
- Traced all four FRs from `plan-01.md` against the diff: FR-1 (no shared mutation) — satisfied, `Clone()` runs before any `Merge*` mutation. FR-2 (copy-forward semantics unchanged) — satisfied, `MemberwiseClone` preserves prior field values for products missing from a source map this pass. FR-3 (nested mutable members independent) — satisfied and exceeded (also covers `StockTakingHistory` and the three history summaries, per the design's own broadened scope, justified by grep evidence in `design-01.md`). FR-4 (`SetLastMergeDateTime()` unaffected) — untouched, still called at the same point.

## Validation run (this step)
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — **Build succeeded, 0 errors** (85 pre-existing warnings, none in touched files, none introduced by this change).
- `dotnet test --filter "FullyQualifiedName~Catalog" --no-build` — **801 passed, 0 failed**, including all 6 new `CatalogMergeServiceTests` isolation tests (cross-pass non-mutation, missing-source-map field preservation, sale-history-summary isolation, manufacture-difficulty isolation, stock-taking-history isolation, and `Stale`-generation isolation after cache swap).
- `dotnet format Anela.Heblo.sln --verify-no-changes --include <4 changed files>` — exit code 0, no formatting violations.

## Findings
None. The implementation is a faithful, minimal realization of the approved design, the load-bearing invariants it depends on were independently re-verified against current source (not just trusted from the design doc), and build/tests/format all pass clean.

## Verdict
Approved — no changes requested.
