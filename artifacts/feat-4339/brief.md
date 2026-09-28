## Module / File
`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/UpdateClassificationRule/UpdateClassificationRuleHandler.cs`

## Coverage
Line coverage: 30% (filter threshold: 60%)

## What's not tested
The handler has two main paths:

1. **Not-found path** (line 28): when `_ruleRepository.GetByIdAsync` returns `null`, the handler throws `ArgumentException("Classification rule with ID ... not found")`. This branch is not tested — there is no assertion that the exception is thrown with the correct message for a missing ID.
2. **Happy path** (lines 32–48): the call to `existingRule.Update(...)` with all request fields, and the mapping of the updated entity to the response DTO, are not covered.

## Why it matters
Without a test for the not-found branch, a refactor that accidentally removes or short-circuits the null check would silently pass and surface only as a `NullReferenceException` at runtime (instead of a clear `ArgumentException` that the global error handler can map to a 404). Similarly, if the `Update` method signature changes, no test would catch mismatched field assignments.

## Suggested approach
Unit tests with a mocked `IClassificationRuleRepository`:
- Rule exists → verify `existingRule.Update(...)` is called with the correct fields and the response DTO is populated from the updated entity
- Rule not found (repo returns null) → verify `ArgumentException` is thrown containing the missing ID

Effort: ~1 hour.

---
_Filed by weekly coverage-gap routine on 2026-09-28. Based on CI run #35977921040 (22bb3b8ff6194bdd055cb438d08b7c6633a85221)._