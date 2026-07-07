## Module
InvoiceClassification

## Finding
`RuleEvaluationEngine.FindMatchingRule` (`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/RuleEvaluationEngine.cs`, lines 15–16) applies:
```csharp
foreach (var rule in rules.Where(r => r.IsActive).OrderBy(r => r.Order))
```

The only caller is `InvoiceClassificationService.ClassifyInvoiceAsync` (line 40), which always passes the result of `_ruleRepository.GetActiveRulesOrderedAsync()` — a repository method that already filters to active rules and orders by `Order` (confirmed in `ClassificationRuleRepository`, lines 22–27).

The re-filtering is therefore always a no-op. The re-sort imposes unnecessary LINQ overhead on every classification call.

More importantly, the method signature (`List rules`) implies to any future caller that the engine handles unfiltered, unordered rule sets — a false contract. A developer who adds a second call path (e.g., classifying against a subset of rules for preview/testing) could easily forget to pre-filter and get silently correct behavior only because the engine double-checks; or they might pass an already-ordered slice and be surprised by the re-sort's cost.

## Why it matters
- KISS violation: two redundant LINQ passes on each of potentially many invoices in the hourly batch job.
- Misleading contract: the engine's interface suggests it handles mixed active/inactive input, but the system never exercises that path.

## Suggested fix
Remove the redundant `.Where(r => r.IsActive).OrderBy(r => r.Order)` from `RuleEvaluationEngine.FindMatchingRule`. Iterate `rules` directly:
```csharp
foreach (var rule in rules)
```

If the engine should own filtering responsibility (valid design), update the caller to pass all rules via `GetAllAsync()` and document clearly that filtering is the engine's job — but do not do both.

---
_Filed by daily arch-review routine on 2026-07-07._
