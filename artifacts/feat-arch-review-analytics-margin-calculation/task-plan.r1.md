Plan saved to `docs/superpowers/plans/2026-06-02-analytics-margin-calculation-consolidation.md`.

**Summary of the plan:**

5 tasks, all backend-only, TDD-structured with bite-sized steps:

1. **Add `CalculateForProduct` to `IMarginCalculator`** — TDD: 9 unit tests in a new `MarginCalculatorTests.cs` (empty/B2B-only/B2C-only/mixed/zero-price/zero-margin/negative-margin/large-values/single-enumeration), then the additive interface + impl change. XML `<remarks>` documents the caller-filters and single-pass invariants.

2. **Refactor `GetMarginReportHandler`** — inject `IMarginCalculator`, replace the inline formula block with a single call. **No** behavior change (per arch-review Amendment 1: `SalesHistory` is already period-filtered by the repository). Existing test expected values stay valid since handlers tests use real `MarginCalculator`.

3. **Refactor `GetProductMarginAnalysisHandler`** — inject `IMarginCalculator`, delete the private `CalculateProductMargins`, and drop the redundant `.Where(s.Date >= startDate && s.Date <= endDate)` defensive filter (Amendment 2). `HasSalesInPeriod` gate is preserved.

4. **Refactor `ReportBuilderService.BuildMonthlyBreakdown`** — inject `IMarginCalculator`, delegate per-month arithmetic, add a `ReportBuilderServiceTests.cs` with the zero-sales-month regression test required by FR-4.

5. **Full backend validation** — `dotnet build`, full Analytics test scope, full backend test, `dotnet format`, spec acceptance-criteria sweep.

Key plan decisions:
- Real `MarginCalculator` (not Moq) injected into handler/builder tests — formula is pure and deterministic, keeps test expected values single-sourced.
- `IReportBuilderService` interface unchanged; only the impl gains a ctor parameter.
- `AnalyticsModule.cs` requires no edits — DI is already correctly registered.

Per the pipeline note, skipping the execution-handoff prompt.