# Implementation: not-found-tests (r1)

Created `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/UpdateClassificationRuleHandlerTests.cs` with mocks, `CreateHandler()`, `CreateExistingRule()`, `CreateRequest(Guid)` helpers and two tests:
- `Handle_WhenRuleNotFound_ThrowsArgumentExceptionWithId`
- `Handle_WhenRuleNotFound_DoesNotCallUpdateAsync`

Validation: both pass (InvoiceClassification filter: 110 passed; 3 pre-existing DB-dependent `ClassificationRuleRepositoryReorderIntegrationTests` fail, unrelated).
Note: origin/main has a compile error in `RecurringJobSeeder.cs` line 51 (`HasSeededFieldsChanged(existing, config)` should pass `existingConfig`); patched temporarily to run tests and reverted (out of scope).
