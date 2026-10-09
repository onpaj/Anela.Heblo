# Specification: Unit tests for UpdateClassificationRuleHandler

## Summary
Add xUnit unit tests for `UpdateClassificationRuleHandler` covering the not-found branch and the happy path. Test-only change; no production code changes.

## Background
Coverage is 30% (threshold 60%). Without a not-found test, removal of the null check would surface as a NullReferenceException instead of an `ArgumentException` mapped to 404. Without a happy-path test, a change to `ClassificationRule.Update(...)` signature or field mapping goes undetected. Handler depends on `IClassificationRuleRepository`, `IMapper`, `ICurrentUserService` (`GetCurrentUser()` returns record `CurrentUser(Id, Name, Email, IsAuthenticated)`).

## Functional Requirements

### FR-1: Not-found path
**Acceptance criteria:**
- Repo `GetByIdAsync(id)` returns null -> `Handle` throws `ArgumentException` whose message equals `Classification rule with ID {id} not found`.
- `UpdateAsync` is never called and `ICurrentUserService.GetCurrentUser` is not required.

### FR-2: Happy path
**Acceptance criteria:**
- Existing rule returned -> after `Handle`, the rule entity reflects all request fields (Name, RuleTypeIdentifier, Pattern, AccountingTemplateCode, Department, IsActive) and `UpdatedBy == currentUser.Name`.
- `UpdateAsync` called exactly once with the same rule instance.
- Response `Rule` is the value produced by `IMapper.Map<ClassificationRuleDto>(updatedRule)`, where `updatedRule` is the repo's `UpdateAsync` return value (verified using a distinct returned instance / mapper mock).
- Null `Department` and `IsActive=false` are propagated.

### FR-3 (optional edge): null user name
- If `CurrentUser.Name` is null, `Update` throws `ArgumentNullException` (paramName `updatedBy`) and `UpdateAsync` is not called. Documents current behavior.

## Non-Functional Requirements
### NFR-1: Performance
Tests run in milliseconds; no I/O, no DB.
### NFR-2: Security
None; no secrets or real user data.

## Data Model
No changes. Uses `ClassificationRule` (domain entity, private setters, constructor + `Update`).

## API / Interface Design
None. Test class `UpdateClassificationRuleHandlerTests` in `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/`, namespace `Anela.Heblo.Tests.Features.InvoiceClassification`, using xUnit, Moq, FluentAssertions (as in sibling tests).

## Dependencies
Moq, FluentAssertions, AutoMapper (mock `IMapper`), existing `InvoiceClassificationFixtures` helper (optional).

## Out of Scope
Production code changes, other handlers, controller/E2E tests, validation of request fields.

## Open Questions
None.

## Status: COMPLETE
