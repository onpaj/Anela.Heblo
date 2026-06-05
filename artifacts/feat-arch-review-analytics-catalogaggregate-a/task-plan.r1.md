Plan saved to `docs/superpowers/plans/2026-06-03-consolidate-analyticsproduct-mapping-sales-projection.md`.

**Summary**

The arch review's central finding is verified against the current branch: PR #1805 already shipped FR-1 through FR-5 of the original spec. The mapping helper exists on `CatalogAnalyticsSourceAdapter.MapToAnalyticsProduct`, both call sites route through it, `SalesHistory` is pre-filtered, and the test class covers the listed scenarios — re-implementing the spec verbatim would actively regress the module boundary.

The plan therefore executes only the one justified follow-up the review identified as "delta (1)":

- **Task 1** — verification gates (greps + baseline build/test) to abort if the codebase has shifted again since the review was written.
- **Task 2** — adds a regression test that pins "the helper itself owns the date filter," committed *before* the refactor so it acts as a permanent guard against future drift in either direction.
- **Task 3** — folds the duplicated 6-line `SalesHistory.Where(...).Select(...).ToList()` projection from both call sites into the helper, dropping the `salesHistory` parameter. Explicit guards in the steps to preserve `M1_A` (not `M1`) and `MapProductType(product.Type)` (not raw `product.Type`) — the two changes the review flagged as semantic landmines.
- **Task 4** — post-refactor grep checks to confirm no projection escaped and no load-bearing decision regressed.

Two commits total: one test, one refactor. No public API change, no module boundary change, no behavior change.