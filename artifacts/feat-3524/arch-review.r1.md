# Architecture Review: Remove redundant filter/sort in RuleEvaluationEngine.FindMatchingRule

## Skip Design: true

## Architectural Fit Assessment
This is a one-line internal refactor inside `RuleEvaluationEngine.FindMatchingRule` (`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/RuleEvaluationEngine.cs`, line 16). It touches no public contract, no DTO, no controller, no persistence, and no UI. Verified against the actual source:

- `IRuleEvaluationEngine.FindMatchingRule(ReceivedInvoice invoice, List<ClassificationRule> rules)` — signature is unchanged by this fix; only its implicit contract tightens.
- `InvoiceClassificationService.ClassifyInvoiceAsync` (lines 38-40) is the **only** production call site, and it always supplies `await _ruleRepository.GetActiveRulesOrderedAsync()`.
- `ClassificationRuleRepository.GetActiveRulesOrderedAsync()` (lines 22-27) already does `.Where(r => r.IsActive).OrderBy(r => r.Order)` at the EF Core query level (translated to SQL, not in-memory LINQ).
- A repo-wide search confirms no other caller of `IRuleEvaluationEngine.FindMatchingRule` exists in production code (only test doubles construct `RuleEvaluationEngine` directly).

This aligns cleanly with the codebase's existing convention of pushing filtering/sorting to the repository/query layer (see `GetAllAsync()` in the same repository, which also pre-sorts by `Order` for its own consumers). The engine re-doing `Where`/`OrderBy` was pure duplication with no offsetting benefit — it's a textbook KISS/YAGNI cleanup, not a design change. No architectural decision is required beyond confirming the "callers pre-filter and pre-sort" contract, which the codebase already honors everywhere except this one spot.

## Proposed Architecture

### Component Overview
No new or restructured components. Existing collaboration is unchanged:

```
InvoiceClassificationJob (hourly batch)
        |
        v
InvoiceClassificationService.ClassifyInvoiceAsync(invoice)
        |
        |-- ClassificationRuleRepository.GetActiveRulesOrderedAsync()   [DB: WHERE IsActive ORDER BY Order]
        |
        v
RuleEvaluationEngine.FindMatchingRule(invoice, rules)   <-- only this method's body changes
        |
        v
EvaluateRule(invoice, rule)  -> IClassificationRule.Evaluate(...)
```

### Key Design Decisions

#### Decision 1: Where filtering/ordering responsibility lives
**Options considered:**
1. Remove the redundant `Where`/`OrderBy` from the engine, formalizing "caller pre-filters and pre-sorts" as the contract (the brief's and spec's chosen direction).
2. Keep the engine defensive and instead change the caller to pass `GetAllAsync()`, making the engine own filtering/ordering.

**Chosen approach:** Option 1 — remove the redundant LINQ pass from `RuleEvaluationEngine.FindMatchingRule`; iterate `rules` directly.

**Rationale:** Option 1 is strictly less code and matches how the only real caller already behaves (repository does the filtering in SQL, which is more efficient than in-memory re-filtering). Option 2 would require changing `InvoiceClassificationService` and its tests, expand scope, and push work that SQL already does efficiently back into the application layer for no benefit. The spec explicitly places Option 2 out of scope — this review confirms that's the right call; there is no functional or performance reason to prefer it.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Two files change:
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/RuleEvaluationEngine.cs` — production fix.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Services/RuleEvaluationEngineTests.cs` — test reconciliation.

### Interfaces and Contracts
`IRuleEvaluationEngine.FindMatchingRule(ReceivedInvoice invoice, List<ClassificationRule> rules)` signature is unchanged. The **implicit** contract becomes explicit and should ideally be documented with a short XML doc comment on the interface method (nice-to-have per spec, not required, but cheap enough that I'd recommend doing it in this same PR since it costs one line and directly prevents the "future second caller forgets to pre-filter" risk called out in the brief):

```csharp
/// <summary>
/// Returns the first rule (in list order) that matches the invoice.
/// Callers must supply an already-filtered (active-only) and already-ordered (by Order) list.
/// </summary>
ClassificationRule? FindMatchingRule(ReceivedInvoice invoice, List<ClassificationRule> rules);
```
This is a documentation-only addition inside the interface file already being touched conceptually — it is optional and should not block the fix if the developer prefers to keep the change to the single line specified in FR-1.

### Data Flow
Unchanged. `ClassifyInvoiceAsync` → `GetActiveRulesOrderedAsync()` (SQL-level filter+sort) → `FindMatchingRule` (now a plain linear scan, first match wins) → same downstream classification/history/logging behavior. Byte-for-byte identical production outcomes, per NFR-1.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A future second call site passes unfiltered/unordered rules and silently gets wrong classification (the exact concern raised in the brief) | Low (no such call site exists today) | Add the one-line XML doc comment on `IRuleEvaluationEngine.FindMatchingRule` documenting the pre-filtered/pre-sorted contract (see above); relies on code review to catch violations since there's no compile-time enforcement |
| Test suite under-verifies the new contract, silently reintroducing filter/sort behavior later without a failing test | Low | Ensure at least one test explicitly asserts plain in-order iteration (e.g., a rule out of `Order` sequence but first in list order is picked) — see Specification Amendment below |
| Regression in production classification behavior | Very low | `ClassifyInvoiceAsync` and its tests (`InvoiceClassificationServiceTests.cs`) are untouched and continue to pass pre-filtered/pre-sorted input from the repository; no behavior change is possible through that path |

## Specification Amendments
Verified against the actual test file (`RuleEvaluationEngineTests.cs`) — the spec's FR-2 undercounts the affected tests by one:

- **`FindMatchingRule_MultipleMatchingRules_ReturnsLowestOrderMatch`** (lines 21-40) is **not** actually safe "as-is." Its `rules` list is `{ ruleHigherOrder (order:2), ruleLowerOrder (order:1) }` — inserted in *reverse* order, relying on the engine's `OrderBy` to put `ruleLowerOrder` first. Once the sort is removed, iteration will hit `ruleHigherOrder` first, which also matches (`RULE_B` evaluates true), and the test will fail (`match` will be `ruleHigherOrder`, not `ruleLowerOrder`).
  - **Amendment:** Either reorder the input list to `{ ruleLowerOrder, ruleHigherOrder }` (list order = intended pre-sorted order) so the existing assertion holds, or rename/rewrite it alongside the other two affected tests. Recommend folding this into FR-2's list of tests requiring changes — three tests need attention, not two.
- Recommend the rewritten/consolidated test(s) include one case where a rule with a numerically higher `Order` value appears earlier in the list and is picked, explicitly proving the engine no longer sorts (this directly documents the "caller must pre-sort" contract called out in FR-2's acceptance criteria, and closes the "silently reintroduces sort" risk above).

No other amendments — FR-1, NFR-1, NFR-2, Data Model, and API sections all check out against the current source.

## Prerequisites
None. No migrations, config, or infrastructure changes needed. Implementation can start immediately: edit `RuleEvaluationEngine.cs` line 16, then update the three affected tests in `RuleEvaluationEngineTests.cs`, then run `dotnet test` scoped to `Anela.Heblo.Tests.Features.InvoiceClassification`.
