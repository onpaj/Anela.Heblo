# Implementation: move-rulename-into-classification-result

## What was implemented
Added a `RuleName` property to `InvoiceClassificationResult`, populated it in `InvoiceClassificationService.ClassifyInvoiceAsync` at the two branches where the matched rule is already in scope (Success and ABRA-update-failed Error), and removed `ClassifyInvoicesHandler`'s `IClassificationRuleRepository` dependency, switching its error-message enrichment to read `result.RuleName` synchronously instead of issuing a per-error `GetByIdAsync` DB lookup.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationResult.cs` — added `public string? RuleName { get; set; }`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs` — sets `RuleName = matchedRule.Name` in the Success and ABRA-failure branches
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs` — removed `IClassificationRuleRepository` field/constructor param; error message now built from `result.RuleName` directly, no repository call
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs` — removed `_ruleRepositoryMock`; added `Handle_WhenErrorResultHasRuleName_IncludesRuleNameInErrorMessage` and `Handle_WhenErrorResultHasNoRuleName_OmitsRuleSegmentFromErrorMessage`
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs` — added `RuleName` assertions to the four existing tests covering the Success, ABRA-failure, no-match, and exception branches

## Tests
- `ClassifyInvoicesHandlerTests.cs` — 7 tests (5 existing unchanged + 2 new), all covering the handler's error-message formatting and the removal of the repository dependency
- `InvoiceClassificationServiceTests.cs` — 4 tests, each now asserting `RuleName` alongside the existing `RuleId` assertion

## How to verify
```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClassifyInvoicesHandlerTests|FullyQualifiedName~InvoiceClassificationServiceTests"
```
Build succeeds with 0 errors; all 11 targeted tests pass (verified locally: Total 11, Passed 11).

## Notes
No DI registration changes needed — `IClassificationRuleRepository` stays registered for its other consumers (the service itself, and the classification-rule CRUD handlers). No HTTP contract or OpenAPI client changes, since nothing HTTP-facing changed.

## PR Summary
`ClassifyInvoicesHandler` was reaching past `IInvoiceClassificationService` into `IClassificationRuleRepository` just to re-look-up a rule name for an error message, causing one extra DB round trip per errored invoice. `InvoiceClassificationService` already has the matched rule in scope when it builds the result, so this moves the rule-name lookup there: `InvoiceClassificationResult` gained a `RuleName` field, the service populates it wherever `matchedRule` is known, and the handler now reads `result.RuleName` directly instead of querying the repository — removing the per-error DB call and the repository dependency from the handler entirely.

### Changes
- `InvoiceClassificationResult.cs` — new `RuleName` property
- `InvoiceClassificationService.cs` — populates `RuleName` at the two branches where the rule is in scope
- `ClassifyInvoicesHandler.cs` — drops `IClassificationRuleRepository`, uses `result.RuleName` synchronously
- `ClassifyInvoicesHandlerTests.cs` — repository mock removed, 2 new tests
- `InvoiceClassificationServiceTests.cs` — `RuleName` assertions added to 4 existing tests

## Status
DONE
