## Module
InvoiceClassification

## Finding
`ClassifyInvoicesHandler` (`ClassifyInvoicesHandler.cs:13, 99–103`) injects `IClassificationRuleRepository` alongside `IInvoiceClassificationService`. The repository is used for exactly one purpose: looking up the rule name to enrich an error message when the service reports `ClassificationResult.Error` with a non-null `RuleId`:

```csharp
var rule = await _ruleRepository.GetByIdAsync(result.RuleId.Value);
if (rule != null)
{
    errorMessage = $"Invoice {invoice.InvoiceNumber} (Rule: {rule.Name}): {result.ErrorMessage}";
}
```

Meanwhile `IInvoiceClassificationService` already has access to the matched rule when it records the failure (it calls `_ruleRepository.GetActiveRulesOrderedAsync()` internally). The result type `InvoiceClassificationResult` (`InvoiceClassificationResult.cs`) carries `RuleId` but not `RuleName`, so the handler has to re-query the DB per errored invoice.

## Why it matters
- Breaks the handler's contract with the service abstraction: the handler reaches past `IInvoiceClassificationService` into the same data layer the service owns.
- Introduces per-error DB round trips inside the classification loop (one `GetByIdAsync` for every invoice that fails with a known rule).
- Makes the handler harder to test: mocking two repositories for a single use-case handler instead of one service.

## Suggested fix
Add `string? RuleName` to `InvoiceClassificationResult` and populate it inside `InvoiceClassificationService.ClassifyInvoiceAsync` at the point where the matched rule is already in scope (before the response is returned). Remove `IClassificationRuleRepository` from `ClassifyInvoicesHandler`'s constructor entirely.

---
_Filed by daily arch-review routine on 2026-07-07._
