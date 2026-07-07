# Implementation: remove-redundant-filter-sort-from-rule-evaluation-engine

## What was implemented
Removed the redundant `.Where(r => r.IsActive).OrderBy(r => r.Order)` from
`RuleEvaluationEngine.FindMatchingRule`. The engine's only production caller
(`InvoiceClassificationService.ClassifyInvoiceAsync`) already fetches rules
pre-filtered and pre-sorted via `IClassificationRuleRepository.GetActiveRulesOrderedAsync()`
(filtering/sorting at the EF Core/SQL level), so the re-filter/re-sort was a
permanent no-op plus unnecessary per-invoice LINQ overhead in an hourly batch job.
The engine's contract tightens to "iterates `rules` in the order given and
returns the first one that evaluates true"; callers must supply an
already-filtered, already-ordered list. No interface signature changes.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/RuleEvaluationEngine.cs` — `FindMatchingRule` now does `foreach (var rule in rules)` instead of `foreach (var rule in rules.Where(r => r.IsActive).OrderBy(r => r.Order))`. `EvaluateRule` unchanged.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Services/RuleEvaluationEngineTests.cs` — rewrote the three tests that depended on the engine's own filter/sort behavior:
  - `FindMatchingRule_MultipleMatchingRules_ReturnsLowestOrderMatch` → renamed `FindMatchingRule_MultipleMatchingRules_ReturnsFirstMatchInGivenOrder`; list is now constructed in the intended evaluation order (`ruleLowerOrder` first).
  - `FindMatchingRule_SkipsInactiveRules` → renamed `FindMatchingRule_DoesNotFilterByIsActive_EvaluatesInGivenOrder`; now asserts an inactive rule listed first is still matched (engine no longer filters by `IsActive`).
  - `FindMatchingRule_SortsByOrder_NotByListInsertionOrder` → renamed `FindMatchingRule_IgnoresOrderField_IteratesInGivenListOrder`; assertion flipped to prove the rule inserted first wins regardless of its numerically higher `Order` value.
  - The other four tests (`FindMatchingRule_NoActiveRuleMatches_ReturnsNull`, `FindMatchingRule_EmptyRulesList_ReturnsNull`, `FindMatchingRule_UnknownRuleTypeIdentifier_DoesNotThrowAndReturnsNull`, `FindMatchingRule_FirstMatch_ShortCircuitsSubsequentEvaluations`) left unchanged as specified.

## Tests
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Services/RuleEvaluationEngineTests.cs` — 7 tests covering: first-match-in-given-order semantics, no filtering by `IsActive`, no re-sorting by `Order`, no-match/empty-list/unknown-identifier edge cases, and short-circuit evaluation.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs` — untouched; verified still passing (mocks `IRuleEvaluationEngine` and `GetActiveRulesOrderedAsync()` directly, unaffected by this change).

## How to verify
```bash
cd backend
dotnet build ../Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~RuleEvaluationEngineTests"   # 7/7 pass
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~InvoiceClassification"        # 86/86 pass
dotnet format ../Anela.Heblo.sln --include src/Anela.Heblo.Application/Features/InvoiceClassification/Services/RuleEvaluationEngine.cs test/Anela.Heblo.Tests/Features/InvoiceClassification/Services/RuleEvaluationEngineTests.cs
```
Manual check: `grep -n "Where\|OrderBy" backend/src/.../RuleEvaluationEngine.cs` returns nothing.

## Notes
- `dotnet build` succeeded with 0 errors (254 pre-existing nullable-reference warnings across the test project, unrelated to this change).
- `dotnet format` made no further changes to the two touched files (they were already compliant).
- No changes made to `InvoiceClassificationService.cs`, `InvoiceClassificationServiceTests.cs`, `ClassificationRuleRepository.cs`, or the `IRuleEvaluationEngine` interface, per scope.
- `artifacts/feat-3524/state.json` shows as modified in the worktree (pipeline bookkeeping updated externally); it was not touched by this task's code commit.

## PR Summary
This is a pure refactor removing dead defensive code from `RuleEvaluationEngine.FindMatchingRule`: the `.Where(IsActive).OrderBy(Order)` it applied to its `rules` parameter was a permanent no-op because its only caller already supplies a pre-filtered, pre-sorted list from the repository (filtering/sorting happens at the SQL level via `GetActiveRulesOrderedAsync()`). Removing it eliminates unnecessary per-invoice LINQ overhead in an hourly batch job with zero change to production classification outcomes. The engine's contract now explicitly requires callers to pass an already-filtered, already-ordered list — no interface signature changes. Three unit tests that previously exercised the engine's own filter/sort logic were rewritten to instead prove the new contract (in-order iteration, no `IsActive` filtering, no `Order` re-sorting); four unrelated tests were left untouched.

### Changes
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/RuleEvaluationEngine.cs` — removed `.Where(r => r.IsActive).OrderBy(r => r.Order)` from the `foreach` in `FindMatchingRule`.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Services/RuleEvaluationEngineTests.cs` — renamed and rewrote three tests to assert plain in-order iteration instead of engine-side filtering/sorting.

## Status
DONE
