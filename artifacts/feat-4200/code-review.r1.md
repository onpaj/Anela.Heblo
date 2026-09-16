# Code Review: feat-4200 (round 1)

Reviewed the full feature-branch diff against `origin/main` (merge-base
`2656da6f47e87d5d6fe11d84cbd2f4bef087edee`), against `spec.r1.md`'s intent
(move `AnalyzeStockItem`/`ShouldIncludeItem`/`SortItems`/`CalculateSummary`
out of `GetPurchaseStockAnalysisHandler` into `IStockAnalysisCalculator` /
`StockAnalysisCalculator`, as a pure, behavior-preserving refactor).

## Review Result: CLEAN

### Blocking (correctness)
- None

Confirmed by diffing the new `StockAnalysisCalculator.AnalyzeItem` /
`FilterItems` (→ private `ShouldIncludeItem`) / `SortItems` /
`CalculateSummary` against the pre-refactor handler's private
`AnalyzeStockItem` / `ShouldIncludeItem` / `SortItems` / `CalculateSummary`
line by line: the logic is byte-for-byte identical, only relocated. The
FR-4 ordering constraint (summary computed from the unfiltered
`allAnalysisItems`, not the filtered/searched list) is preserved in
`GetPurchaseStockAnalysisHandler.Handle` (line 77 still calls
`CalculateSummary(allAnalysisItems, ...)`, not `analysisItems`). The
`IStockSeverityCalculator` dependency correctly moved from the handler's
constructor into `StockAnalysisCalculator`'s constructor, and both
`GetPurchaseStockAnalysisHandlerTests` and
`GetPurchaseStockAnalysisHandlerDiacriticsTests` were updated consistently
at their construction call sites. `GetLastPurchaseInfo` moved together with
`AnalyzeItem` as the spec anticipated. The free-text search-term filter and
`MaterialCategoryResolver.Matches` correctly stayed in the handler, out of
scope per FR-5/Out of Scope.

New `StockAnalysisCalculatorTests` coverage is thorough: all six
`StockAnalysisSortBy` paths (including the default-fallback case and the
`descending` reversal), all `StockStatusFilter` values combined with
`OnlyConfigured`, the two `TotalInventoryValue`/severity-bucket summary
paths, and the `AnalyzeItem` mapping (including the null-`LastPurchase`
case) are all exercised.

### Advisory (cleanup)
- None

## Verification performed this round
- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors (256 pre-existing
  warnings, none touching the changed files).
- `dotnet format Anela.Heblo.sln --verify-no-changes` — clean, no
  violations.
- Full Purchase-scoped `dotnet test` re-run was started but the shared
  build machine was heavily contended by other concurrent pipeline workers
  (feat-4199, feat-4202) and it did not complete in a reasonable window; it
  was not needed to reach a verdict because the prior `full-suite-validation`
  task (this same branch, committed just before this round) already ran and
  recorded the full result: 375 passed / 1 pre-existing
  Testcontainers/Docker-dependent failure unrelated to Purchase, with the
  three directly relevant classes at 100% — `StockAnalysisCalculatorTests`
  29/29, `GetPurchaseStockAnalysisHandlerTests` 18/18,
  `GetPurchaseStockAnalysisHandlerDiacriticsTests` 9/9 — combined with this
  round's independent line-by-line diff confirmation of behavioral parity,
  that is sufficient signal for CLEAN.

## Note (not a finding)
The diff also contains a one-line, pre-existing `.agents/developer.md`
context-file-path fix (`da637061`) from an earlier orchestrator round on
this branch, unrelated to this refactor's scope — not a code-review
finding, just recorded for traceability per the prior unit's own note.
