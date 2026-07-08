# Implementation: add-get-max-order-repository-method (revision 1)

## Summary

Replaced `CreateClassificationRuleHandler`'s full-table `GetAllAsync()` + in-memory `Max()` with a
single targeted `GetMaxOrderAsync()` repository call that executes as a `SELECT MAX([Order])`
database aggregate, exactly as specified in the task plan.

## Changes

- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationRuleRepository.cs`
  — added `Task<int> GetMaxOrderAsync();` to the interface, right after `GetActiveRulesOrderedAsync`.
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationRuleRepository.cs`
  — implemented `GetMaxOrderAsync()` as `await _context.ClassificationRules.MaxAsync(r => (int?)r.Order) ?? 0`.
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/CreateClassificationRule/CreateClassificationRuleHandler.cs`
  — replaced the two-line `GetAllAsync()` + in-memory `Max()` block with
  `var maxOrder = await _ruleRepository.GetMaxOrderAsync();`. No other line in the handler changed.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassificationRuleRepositoryTests.cs` (new)
  — repository-level tests against an EF Core InMemory `ApplicationDbContext`, following the sibling
  `ClassificationHistoryRepositoryTests` pattern:
  - `GetMaxOrderAsync_WithNoRules_ReturnsZero`
  - `GetMaxOrderAsync_WithMultipleRules_ReturnsHighestOrder`

`GetAllAsync()` itself is untouched and remains in use elsewhere (rule listing).

## Verification

- `dotnet build` on the full solution (`Anela.Heblo.sln`): **Build succeeded, 0 Error(s)**. (One
  pre-existing, unrelated warning from the `AccessMatrixGen` post-build tool crashing on an
  unrelated JSON file — present on `main` before this change, not touched by this task.)
- `dotnet test --filter "FullyQualifiedName~ClassificationRuleRepositoryTests"`:
  **Passed! - Failed: 0, Passed: 2, Skipped: 0**.

## Status: DONE
