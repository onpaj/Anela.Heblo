# Implementation Plan: Correct misleading streaming comments in Analytics

**Goal:** Make comments truthful about memory behaviour. Comment-only change, no behaviour change.

### task: fix-comments
**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsRepository.cs`

- [ ] Step 1: In `MarginCalculator.cs`, replace the class summary ("🔒 PERFORMANCE FIX: Extracted margin calculation logic...") with a summary without any memory claim, and replace the `CalculateAsync` summary ("using streaming approach to minimize memory usage") with the wording in `design.r1.md` (retention in `GroupProducts`, O(products x sales history), why, and the O(n_groups) alternative).
- [ ] Step 2: In `GetProductMarginSummaryHandler.cs`, reword the class summary ("using streaming architecture ... reduced memory usage") and the step 3 comment ("reduces memory usage") to say streaming only avoids a duplicate list during fetch; reword the `GenerateTopProducts` summary only if it claims memory benefits.
- [ ] Step 3: In `AnalyticsRepository.cs`, reword the class summary ("Prevents memory overload") and method summary ("to avoid memory overload") to accurately describe delegating an async stream.
- [ ] Step 4: Verify `git diff` touches comment lines only. Run `cd backend && dotnet build && dotnet format --verify-no-changes` and `dotnet test --filter "FullyQualifiedName~Features.Analytics"`; all pass.
- [ ] Step 5: Commit `docs(analytics): correct misleading streaming/memory comments (#4421)`.

### task: update-process-doc
**Files:**
- Modify: `docs/processes/calc-product-margin-summary.md`

- [ ] Step 1: Read the doc; at line ~56 ("streams these through MarginCalculator...") add a note under Known quirks that `MarginCalculator` retains all accepted products per group in `GroupProducts` (peak memory O(products x sales history)); streaming only avoids a second full copy at fetch time.
- [ ] Step 2: Bump `verified_at` in the front matter to today's date.
- [ ] Step 3: Run `python3 scripts/process-docs/check.py index`; expect pass.
- [ ] Step 4: Commit `docs(processes): note GroupProducts retention in margin summary (#4421)`.
