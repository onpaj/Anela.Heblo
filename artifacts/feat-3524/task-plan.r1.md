# Task Plan: Remove redundant filter/sort in RuleEvaluationEngine.FindMatchingRule

## Scope
Tiny, surgical backend-only fix. One production line changes; three unit tests are
reconciled with the new (tightened) contract. No API, DTO, controller, persistence,
or UI changes. Design was skipped (see brief/spec/arch-review — no `design.r1.md`).

Given the size, this is implemented as a single task.

---

### task: remove-redundant-filter-sort-from-rule-evaluation-engine

**Context**

`RuleEvaluationEngine.FindMatchingRule` re-applies `.Where(r => r.IsActive).OrderBy(r => r.Order)`
to a `rules` list that its only production caller (`InvoiceClassificationService.ClassifyInvoiceAsync`)
already fetches pre-filtered and pre-sorted via `IClassificationRuleRepository.GetActiveRulesOrderedAsync()`
(filter/sort happens at the EF Core/SQL level). The re-filter is a permanent no-op and the
re-sort is unnecessary per-invoice LINQ overhead in an hourly batch job. This is a pure
refactor: production classification outcomes must be byte-for-byte identical before and
after the change (NFR-1 in spec.r1.md).

The engine's contract tightens from "defensively filters/sorts whatever it's given" to
"iterates `rules` in the order given and returns the first one that evaluates true; callers
must supply an already-filtered, already-ordered list." No interface signature changes.

**Files to touch**

1. `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/RuleEvaluationEngine.cs`
2. `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Services/RuleEvaluationEngineTests.cs`

Do **not** touch:
- `InvoiceClassificationService.cs` / `InvoiceClassificationServiceTests.cs` — out of scope, unaffected (mocks `IRuleEvaluationEngine` and `GetActiveRulesOrderedAsync()` directly, never exercises the engine's internal filter/sort logic).
- `ClassificationRuleRepository.cs`, `IClassificationRuleRepository` — out of scope.
- `IRuleEvaluationEngine` interface signature — unchanged (an XML doc comment documenting the new contract is optional/nice-to-have per arch-review, not required; skip it to keep the change minimal unless it's trivial to add).

**Step 1 — Production fix**

In `RuleEvaluationEngine.cs`, change line 16 from:

```csharp
foreach (var rule in rules.Where(r => r.IsActive).OrderBy(r => r.Order))
```

to:

```csharp
foreach (var rule in rules)
```

No other lines in `FindMatchingRule` or `EvaluateRule` change. The method still returns the
first rule for which `EvaluateRule` returns true, still short-circuits, still returns `null`
if nothing matches or `rules` is empty.

**Step 2 — Update the three affected tests**

The arch-review (arch-review.r1.md) identified that **three** tests in
`RuleEvaluationEngineTests.cs` depend on the engine's own filter/sort behavior (the spec
initially undercounted this at two — trust the arch-review's list, it was verified against
the actual test file):

1. **`FindMatchingRule_MultipleMatchingRules_ReturnsLowestOrderMatch`** (currently lines 20-40)
   Currently builds `rules = { ruleHigherOrder (order:2, RULE_B), ruleLowerOrder (order:1, RULE_A) }`
   — inserted in *reverse* of `Order`, relying on the engine's removed `OrderBy` to put
   `ruleLowerOrder` first. Once sorting is removed, iteration hits `ruleHigherOrder` first,
   which also evaluates true (`RULE_B`), so the existing assertion (`match.Should().BeSameAs(ruleLowerOrder)`)
   will fail.
   **Fix:** Reorder the list construction so list order matches the intended evaluation
   order — the list itself represents "already pre-sorted by caller" input:
   ```csharp
   var rules = new List<ClassificationRule> { ruleLowerOrder, ruleHigherOrder };
   ```
   Rename the test to `FindMatchingRule_MultipleMatchingRules_ReturnsFirstMatchInGivenOrder`
   to reflect that the engine no longer sorts — it just returns the first match in the list
   order it was given (which happens to be `ruleLowerOrder` because the caller pre-sorted it
   that way). Keep the rest of the test body (mocks, invoice, assertion) unchanged aside from
   the list construction order and the rename.

2. **`FindMatchingRule_SkipsInactiveRules`** (currently lines 42-60)
   Currently asserts the engine itself filters out an inactive rule. This premise is no
   longer valid — the engine no longer checks `IsActive` at all; that's now the caller's
   job (enforced via `GetActiveRulesOrderedAsync()`, untouched by this change).
   **Fix:** Replace the test with one that documents the new contract explicitly — that the
   engine evaluates whatever it's given, active or not, in list order. Rename to
   `FindMatchingRule_DoesNotFilterByIsActive_EvaluatesInGivenOrder` and rewrite so an
   *inactive* rule that matches is returned first, proving no filtering happens:
   ```csharp
   [Fact]
   public void FindMatchingRule_DoesNotFilterByIsActive_EvaluatesInGivenOrder()
   {
       // Arrange
       var strategy = CreateStrategyMock("RULE_A", evaluateResult: true);
       var sut = new RuleEvaluationEngine(new[] { strategy.Object });

       var invoice = InvoiceClassificationFixtures.CreateInvoice();
       var inactiveRule = InvoiceClassificationFixtures.CreateRule("RULE_A", pattern: "p", order: 1, isActive: false);
       var activeRule = InvoiceClassificationFixtures.CreateRule("RULE_A", pattern: "p", order: 2, isActive: true);

       // Inactive rule listed first: engine no longer filters by IsActive, so it must
       // still be evaluated and matched — filtering is the caller's responsibility now.
       var rules = new List<ClassificationRule> { inactiveRule, activeRule };

       // Act
       var match = sut.FindMatchingRule(invoice, rules);

       // Assert
       match.Should().BeSameAs(inactiveRule);
   }
   ```

3. **`FindMatchingRule_SortsByOrder_NotByListInsertionOrder`** (currently lines 116-135)
   Currently asserts the engine sorts by `Order` regardless of list insertion order. This
   premise is now false — the engine iterates in list order and ignores `Order` entirely.
   **Fix:** Replace/rename to `FindMatchingRule_IgnoresOrderField_IteratesInGivenListOrder`,
   flip the assertion to prove the *opposite*: the rule inserted first is returned even
   though its `Order` value is numerically higher:
   ```csharp
   [Fact]
   public void FindMatchingRule_IgnoresOrderField_IteratesInGivenListOrder()
   {
       // Arrange
       var strategy = CreateStrategyMock("RULE_A", evaluateResult: true);
       var sut = new RuleEvaluationEngine(new[] { strategy.Object });

       var invoice = InvoiceClassificationFixtures.CreateInvoice();
       var insertedFirstHighOrder = InvoiceClassificationFixtures.CreateRule("RULE_A", pattern: "p", order: 10);
       var insertedSecondLowOrder = InvoiceClassificationFixtures.CreateRule("RULE_A", pattern: "p", order: 1);

       // List insertion order deliberately does NOT match Order value ascending —
       // proves the engine no longer sorts by Order; it evaluates in given list order.
       var rules = new List<ClassificationRule> { insertedFirstHighOrder, insertedSecondLowOrder };

       // Act
       var match = sut.FindMatchingRule(invoice, rules);

       // Assert
       match.Should().BeSameAs(insertedFirstHighOrder);
   }
   ```

The other four tests in the file are unaffected and must be left exactly as-is:
`FindMatchingRule_NoActiveRuleMatches_ReturnsNull`, `FindMatchingRule_EmptyRulesList_ReturnsNull`,
`FindMatchingRule_UnknownRuleTypeIdentifier_DoesNotThrowAndReturnsNull`,
`FindMatchingRule_FirstMatch_ShortCircuitsSubsequentEvaluations` (this last one already uses
rules inserted in ascending `Order` = list order, so it remains valid unchanged).

**Verification**

- Run the scoped test class:
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RuleEvaluationEngineTests"
  ```
  All 7 tests (4 unchanged + 3 rewritten/renamed) must pass.
- Confirm no test in the file references `.Where(` / `.OrderBy(` expectations that
  contradict plain in-order iteration (i.e., grep the file for "Order" in assertions to
  double check nothing else implicitly relies on sorting).
- Run the full `InvoiceClassification` test suite to confirm `InvoiceClassificationServiceTests.cs`
  is unaffected:
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~InvoiceClassification"
  ```
- `dotnet build` and `dotnet format` (per repo validation rules) on the touched projects.
- Manually confirm `RuleEvaluationEngine.cs` no longer contains `.Where(` or `.OrderBy(` in
  `FindMatchingRule`, and that `EvaluateRule` is byte-for-byte unchanged.

**Acceptance criteria (from spec.r1.md / arch-review.r1.md)**

- `FindMatchingRule` no longer calls `.Where(...)` or `.OrderBy(...)` on `rules`.
- `InvoiceClassificationService.ClassifyInvoiceAsync` and its tests are untouched.
- No test in `RuleEvaluationEngineTests.cs` asserts the engine filters inactive rules or
  re-sorts by `Order`; at least one test explicitly proves in-order iteration ignoring
  `IsActive`, and at least one explicitly proves in-order iteration ignoring `Order`.
- All tests in the `InvoiceClassification` test suite pass.
- No public API, DTO, controller, or persistence changes.
