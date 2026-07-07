# Specification: Remove redundant filter/sort in RuleEvaluationEngine.FindMatchingRule

## Summary
`RuleEvaluationEngine.FindMatchingRule` re-applies `.Where(r => r.IsActive).OrderBy(r => r.Order)` to a `rules` list that its only caller already fetches pre-filtered and pre-sorted via `IClassificationRuleRepository.GetActiveRulesOrderedAsync()`. This is a small code-quality fix: remove the redundant LINQ pass so the method iterates `rules` directly, and update the unit tests whose assertions depend on the engine performing its own filtering/sorting.

## Background
`InvoiceClassificationService.ClassifyInvoiceAsync` (line 38-40) always calls:
```csharp
var rules = await _ruleRepository.GetActiveRulesOrderedAsync();
var matchedRule = _ruleEngine.FindMatchingRule(invoice, rules);
```
`ClassificationRuleRepository.GetActiveRulesOrderedAsync()` (lines 22-27) already does `.Where(r => r.IsActive).OrderBy(r => r.Order)` at the EF Core query level. `RuleEvaluationEngine.FindMatchingRule` (lines 14-25) redundantly re-filters and re-sorts the already-clean list in memory. `IRuleEvaluationEngine.FindMatchingRule` has exactly one call site in production code, so the re-filter is a permanent no-op and the re-sort is unnecessary LINQ overhead on every invoice classification (this method runs per-invoice in an hourly batch job).

Beyond the minor performance cost, the current signature (`List<ClassificationRule> rules`) misleadingly implies the engine is responsible for filtering/ordering an arbitrary rule set, which is not exercised anywhere in the system today.

## Functional Requirements

### FR-1: Remove redundant filter/sort from FindMatchingRule
In `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/RuleEvaluationEngine.cs`, change:
```csharp
foreach (var rule in rules.Where(r => r.IsActive).OrderBy(r => r.Order))
```
to:
```csharp
foreach (var rule in rules)
```
No other logic in `FindMatchingRule` or `EvaluateRule` changes. The engine's contract becomes: "iterate `rules` in the order given and return the first one that evaluates true" — callers are responsible for supplying an already-filtered, already-ordered list.

**Acceptance criteria:**
- `FindMatchingRule` no longer calls `.Where(...)` or `.OrderBy(...)` on the input `rules` parameter.
- `InvoiceClassificationService.ClassifyInvoiceAsync` is unchanged and continues to pass the result of `GetActiveRulesOrderedAsync()` — end-to-end classification behavior (which rule matches a given invoice) is identical to before the change.
- `RuleEvaluationEngine` continues to short-circuit on the first matching rule (existing behavior preserved).

### FR-2: Reconcile existing unit tests with the new contract
`backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Services/RuleEvaluationEngineTests.cs` contains two tests that assert the engine itself performs filtering/sorting, which will no longer hold once FR-1 lands:
- `FindMatchingRule_SkipsInactiveRules` (lines 43-60) — passes an inactive rule with lower `Order` than an active one and asserts the inactive rule is skipped. Once filtering is removed, the engine will evaluate rules in list order regardless of `IsActive`, so this test's premise is no longer valid for the engine in isolation.
- `FindMatchingRule_SortsByOrder_NotByListInsertionOrder` (lines 117-135) — passes rules in reverse `Order` and asserts the engine picks the lowest-`Order` match. Once sorting is removed, the engine will evaluate in list (insertion) order, not `Order` value, so this test's premise is no longer valid.

These two tests must be updated or removed to reflect that filtering/ordering is now the caller's responsibility, not the engine's. The other four tests in the file (`MultipleMatchingRules_ReturnsLowestOrderMatch` — only valid if input is pre-sorted, `NoActiveRuleMatches_ReturnsNull`, `EmptyRulesList_ReturnsNull`, `UnknownRuleTypeIdentifier_DoesNotThrowAndReturnsNull`, `FirstMatch_ShortCircuitsSubsequentEvaluations`) remain valid as-is or with input already in final order, since they either pass single-rule lists, empty lists, or lists already in intended iteration order.

**Acceptance criteria:**
- No test in `RuleEvaluationEngineTests.cs` asserts that the engine filters out inactive rules or re-sorts input by `Order`.
- Test names/bodies that previously exercised filter/sort behavior are either removed, or rewritten to assert plain in-order iteration (e.g., renaming to something like `FindMatchingRule_IteratesInGivenOrder_ReturnsFirstMatch` with pre-ordered/pre-filtered input), so the suite documents the new "caller pre-filters and pre-sorts" contract.
- `InvoiceClassificationServiceTests.cs` (which mocks `IRuleEvaluationEngine` and `GetActiveRulesOrderedAsync()` directly, not `RuleEvaluationEngine`'s internals) requires no changes, since it never exercises the removed filter/sort logic.
- All tests in the `InvoiceClassification` test suite pass after the change (`dotnet test` scoped to the affected test class, or full backend suite).

## Non-Functional Requirements

### NFR-1: Behavior preservation
This is a pure refactor: production classification outcomes (which rule matches which invoice) must be byte-for-byte identical before and after the change, since the sole caller already supplies filtered/sorted input.

### NFR-2: Performance
Eliminates one redundant `Where` + `OrderBy` LINQ pass per `FindMatchingRule` call. No new allocations or algorithmic complexity introduced. No measurable performance target beyond "strictly less work than before."

## Data Model
No data model changes. `ClassificationRule` entity and `IClassificationRuleRepository` are untouched.

## API / Interface Design
No public API, controller, or DTO changes. `IRuleEvaluationEngine.FindMatchingRule(ReceivedInvoice invoice, List<ClassificationRule> rules)` signature is unchanged — only its documented/implicit contract (caller must pre-filter and pre-sort) is now enforced by the implementation rather than defensively re-checked.

## Dependencies
None beyond the existing `IClassificationRuleRepository` and `IRuleEvaluationEngine` abstractions already in place.

## Out of Scope
- Moving filtering/ordering responsibility into the engine (the alternative fix suggested in the arch-review finding, i.e. have the engine accept `GetAllAsync()` and own filtering). The brief selects the "remove redundant work from the engine" direction, not this alternative.
- Any change to `ClassificationRuleRepository`, `GetActiveRulesOrderedAsync()`, or `GetAllAsync()`.
- Any change to `InvoiceClassificationService.ClassifyInvoiceAsync` or its tests (`InvoiceClassificationServiceTests.cs`), which do not exercise the engine's internal filter/sort logic.
- Adding XML doc comments or README notes documenting the "pre-filtered, pre-sorted" contract (nice-to-have, not required for this surgical fix).

## Open Questions

None.
