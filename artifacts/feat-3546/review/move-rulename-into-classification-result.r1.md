# Code Review: move-rulename-into-classification-result

## Summary
The implementation follows the task spec precisely: `RuleName` was added to `InvoiceClassificationResult`, populated in the two `InvoiceClassificationService.ClassifyInvoiceAsync` branches where `matchedRule` is in scope, and `ClassifyInvoicesHandler` was slimmed to 4 constructor parameters with the per-error DB lookup replaced by a synchronous read of `result.RuleName`. Both target test files were updated exactly as prescribed, including the two new handler tests covering the with/without-`RuleName` error-message paths.

## Review Result: PASS

### task: move-rulename-into-classification-result
**Status:** PASS

Verification performed:
- `git show 49c5575` confirms all 5 files listed in the spec were touched, with no unrelated files changed.
- `InvoiceClassificationResult.cs`: `RuleName` added as a plain `string?` auto-property immediately after `RuleId`, as specified.
- `InvoiceClassificationService.cs`: `RuleName = matchedRule.Name` added to exactly the Success and ABRA-update-failed `Error` branches; the `ManualReviewRequired` and outer-exception branches are untouched (verified by test assertions of `RuleName.Should().BeNull()` in those paths).
- `ClassifyInvoicesHandler.cs`: `IClassificationRuleRepository` field, constructor param, and the `await _ruleRepository.GetByIdAsync(...)` call are fully removed; `grep -n "IClassificationRuleRepository|_ruleRepository"` on the handler file returns no matches. Error-message construction now branches synchronously on `string.IsNullOrEmpty(result.RuleName)`, preserving the exact original message formats (`"Invoice {n} (Rule: {name}): {msg}"` / `"Invoice {n}: {msg}"`).
- `ClassifyInvoicesHandlerTests.cs`: `_ruleRepositoryMock` field/instantiation/constructor-arg removed; `grep` for `_ruleRepositoryMock` in the test file returns no matches. Two new tests added exactly as specified (`Handle_WhenErrorResultHasRuleName_IncludesRuleNameInErrorMessage`, `Handle_WhenErrorResultHasNoRuleName_OmitsRuleSegmentFromErrorMessage`), each asserting the correct message shape.
- `InvoiceClassificationServiceTests.cs`: `RuleName` assertions added alongside `RuleId` assertions in all 4 specified tests (Success, ABRA-failure, no-match, exception branches).
- `dotnet build` on the full solution: 0 errors (250 pre-existing warnings, unrelated to this change).
- `dotnet test --filter "FullyQualifiedName~ClassifyInvoicesHandlerTests|FullyQualifiedName~InvoiceClassificationServiceTests"`: 11/11 passed (7 handler + 4 service), matching the spec's expected count.
- No DI registration changes were made, consistent with the spec (`IClassificationRuleRepository` remains registered for its other consumers).

No issues found.

## Overall Notes
Clean, minimal, surgical diff — exactly the 5 files specified, no scope creep. The refactor correctly eliminates the per-error `IClassificationRuleRepository.GetByIdAsync` DB round trip while preserving identical error-message output shape.
