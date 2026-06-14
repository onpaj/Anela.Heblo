Plan saved to `docs/superpowers/plans/2026-06-08-remove-misleading-instance-lock-financial-analysis.md`.

**Summary of the plan:**

3 tasks, decomposed into bite-sized TDD steps with exact code and commands at every checkbox:

1. **Task 1 — Pin behavior with a regression test** (4 steps). Added *before* touching the lock so it documents the throttle behavior under the current (lock-present) code, then proves the lock removal didn't regress it. Test seeds `financial_last_refresh` inside the 10-minute window and asserts `Times.Never` on both `ILedgerService.GetLedgerItems` and `IStockValueService.GetStockValueChangesAsync`. Matches the seeding pattern already used in `FinancialAnalysisServiceTests.cs:174`.

2. **Task 2 — Remove the misleading lock** (9 steps). Deletes the `_refreshLock` field (line 22) and unwraps the `lock (_refreshLock) { ... }` block around the throttle check (lines 109-117). Each removal verified by a targeted grep. Build, format, test, and DI-module-unchanged checks all gated.

3. **Task 3 — Final verification** (4 steps). Whole-solution build + full test suite + a diff-shape assertion that the only touched files are the two in scope — guards against accidentally editing the unrelated `_refreshLock` symbol in `ManufactureBasedMaterialCostProvider.cs` (different module, static `SemaphoreSlim`, must not be touched per the arch-review).

The plan includes a spec-coverage table mapping every FR/NFR to its implementing step, the validation gate from `CLAUDE.md` (`dotnet build`, `dotnet format`, `dotnet test`), and the exact commit messages (one `test:` commit, one `refactor:` commit) so the diff history stays clean.

Per the pipeline instructions, the execution-handoff prompt is intentionally skipped — the plan file is the deliverable.