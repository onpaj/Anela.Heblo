Implementation plan saved to `docs/superpowers/plans/2026-06-08-extract-period-totals-calculation.md`.

**Summary of the plan (5 tasks, 4 commits expected):**

1. **Characterization test first** — add a single `[Fact]` covering all six FR-4 ledger-item cases (debit-5, credit-5, credit-6, debit-6, null account numbers, irrelevant prefixes) through `GetFinancialOverviewAsync` (real-time path). Test passes against current code, locking in behavior before the refactor.
2. **Helper + Site 1** — add `private static (decimal income, decimal expenses) CalculatePeriodTotals(...)` to `FinancialAnalysisService.cs` and replace the block in `RefreshMonthlyDataAsync` (lines 219–233). Shipped together so the helper is never unused.
3. **Site 2** — replace the block in `GetHybridWithCurrentMonthAsync` (lines 303–309).
4. **Site 3** — replace the block in `GetFinancialOverviewRealTimeAsync` (lines 506–521). Flagged as CRITICAL: must pass `monthDebitItems` / `monthCreditItems` (per-month locals), NOT the outer `debitItems` / `creditItems` — the only literal deviation from the brief and the highest-severity risk in the refactor.
5. **Validation gates** — `dotnet build` (zero new warnings), `dotnet format --verify-no-changes`, full `dotnet test`, verify no frontend/generated changes.

Each task ends with a verification step (build + targeted test run) and a focused commit. Predicate preservation is enforced byte-for-byte; no `.ToList()` may be added (NFR-1).