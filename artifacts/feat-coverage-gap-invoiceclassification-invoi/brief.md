## Module / File
`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs`

## Coverage
No direct unit tests. `ClassifyInvoicesHandlerTests` mocks `IInvoiceClassificationService` entirely, so `ClassifyInvoiceAsync` has never been exercised by the test suite.

## What's not tested
Four distinct outcome paths in `ClassifyInvoiceAsync`:
1. **No matching rule found** — marks invoice for manual review, records history with `ManualReviewRequired`, calls `MarkInvoiceForManualReviewAsync` on the client
2. **Rule matched, ABRA update succeeds** — records history with `Success`, returns rule ID and accounting template
3. **Rule matched, ABRA update fails** (`success == false`) — records history with `Error`, returns error result with the rule ID for display; the rule name lookup from `_ruleRepository.GetByIdAsync` is also untested
4. **Exception during classification** — records history with `Error` and exception message, returns error result

The history recording call (`RecordClassificationHistory`) fires on every path but is never asserted.

## Why it matters
`InvoiceClassificationService` drives automated invoice routing — it decides what accounting template and department every received invoice gets. A regression in the no-rule or ABRA-failure path could silently leave invoices unclassified or incorrectly recorded in history without any test catching it.

## Suggested approach
Unit-test `ClassifyInvoiceAsync` directly with mocked `IClassificationRuleRepository`, `IRuleEvaluationEngine`, `IInvoiceClassificationsClient`, `IClassificationHistoryRepository`, and `ICurrentUserService`. Four test methods covering each outcome. ~2–3 hours.

---
_Filed by weekly coverage-gap routine on 2026-06-08. Based on CI run #27104028537 (6568feba33640ae063b2cb6af3c81da31b3720e1)._