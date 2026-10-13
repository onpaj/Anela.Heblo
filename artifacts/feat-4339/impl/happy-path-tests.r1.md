# Implementation: happy-path-tests (r1)

Added four tests to `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/UpdateClassificationRuleHandlerTests.cs`:
- `Handle_WhenRuleExists_UpdatesAllFieldsAndSetsUpdatedByFromCurrentUser`
- `Handle_WhenRuleExists_PersistsAndReturnsMappedDtoOfUpdatedRule`
- `Handle_WithNullDepartmentAndInactive_PropagatesValues`
- `Handle_WhenCurrentUserNameIsNull_ThrowsArgumentNullExceptionAndDoesNotPersist`

Validation: `dotnet test --filter FullyQualifiedName~UpdateClassificationRuleHandlerTests` -> 6 passed, 0 failed.
Note: origin/main has a pre-existing compile error in RecurringJobSeeder.cs line 51; patched temporarily to run tests and reverted (out of scope).

## Status
DONE
