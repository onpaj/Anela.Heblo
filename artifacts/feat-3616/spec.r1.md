# Specification: Unit tests for UpdateRuleRequestValidator (Photobank)

## Summary
Add a dedicated unit test suite for `UpdateRuleRequestValidator`, the FluentValidation validator behind the "update photobank tagging rule" use case, which currently has 0% line coverage. The suite must exercise all four validated fields (`Id`, `PathPattern`, `TagName`, `SortOrder`) and, transitively, the previously-untested `PhotobankValidationHelpers.BeValidRegex` helper that backs the `PathPattern` regex check.

## Background
`UpdateRuleRequestValidator` (`backend/src/Anela.Heblo.Application/Features/Photobank/Validators/UpdateRuleRequestValidator.cs`) validates `UpdateRuleRequest` before a photobank tagging rule is persisted. A weekly coverage-gap scan (CI run #28968007617) flagged this file at 0% line coverage against a 60% threshold. The validator's most consequential rule is that `PathPattern` must be a syntactically valid .NET regex (via `PhotobankValidationHelpers.BeValidRegex`) — an invalid pattern that slips through would be stored and later crash the photobank sync/matching process when compiled at runtime (see `TagRuleMatcher`). `BeValidRegex` itself has no test coverage anywhere in the codebase, making it a silent, unverified dependency. This work is a pure test-authoring task: no production code changes are required or expected.

## Functional Requirements

### FR-1: Test suite for UpdateRuleRequestValidator
Create `UpdateRuleRequestValidatorTests` covering every `RuleFor` clause defined in `UpdateRuleRequestValidator`, using FluentValidation's `TestValidate` extension (`FluentValidation.TestHelper`), consistent with existing Photobank validator tests (e.g. `GetPhotosRequestValidatorTests`, `BulkAddPhotoTagRequestValidatorTests`).

Rules under test, as implemented in the validator today:
- `Id`: must be `GreaterThan(0)` — message `"Id must be a positive integer"`.
- `PathPattern`: `NotEmpty()` — message `"PathPattern is required"`; `MaximumLength(500)` — message `"PathPattern cannot exceed 500 characters"`; `Must(PhotobankValidationHelpers.BeValidRegex)` — message `"Invalid regular expression pattern."`. FluentValidation evaluates chained rules on the same property independently (not short-circuited across separate `.WithMessage()` calls unless empty), so both an empty pattern and an invalid regex pattern must be tested as distinct cases.
- `TagName`: `NotEmpty()` — message `"TagName is required"`; `MaximumLength(100)` — message `"TagName cannot exceed 100 characters"`.
- `SortOrder`: `GreaterThanOrEqualTo(0)` (no custom message — default FluentValidation message applies).

**Acceptance criteria:**
- A fully valid `UpdateRuleRequest` (e.g. `Id = 1`, `PathPattern = "^[a-z]+\\.png$"`, `TagName = "summer"`, `SortOrder = 0`, `IsActive = true`) produces no validation errors (`ShouldNotHaveAnyValidationErrors`).
- `Id = 0` and `Id = -1` each produce a validation error on `Id` with message `"Id must be a positive integer"`.
- `PathPattern = ""` and `PathPattern = null` each produce a validation error on `PathPattern` with message `"PathPattern is required"`.
- `PathPattern` with length 501 (any characters) produces a validation error on `PathPattern` with message `"PathPattern cannot exceed 500 characters"`.
- `PathPattern` with length exactly 500 (valid regex content) produces no validation error on `PathPattern` (boundary case).
- `PathPattern = "["` (syntactically invalid regex) produces a validation error on `PathPattern` with message `"Invalid regular expression pattern."`.
- `PathPattern = "^[a-z]+"` (syntactically valid regex) produces no validation error on `PathPattern`.
- `TagName = ""` and `TagName = null` each produce a validation error on `TagName` with message `"TagName is required"`.
- `TagName` with length 101 produces a validation error on `TagName` with message `"TagName cannot exceed 100 characters"`.
- `SortOrder = -1` produces a validation error on `SortOrder`.
- `SortOrder = 0` produces no validation error on `SortOrder` (boundary case, since the rule is `>= 0`).
- Each error-path test asserts the exact error message via `WithErrorMessage(...)` where the validator defines one; tests use `ShouldHaveValidationErrorFor` / `ShouldNotHaveValidationErrorFor` per-property assertions (not just aggregate `IsValid`), matching the pattern in `GetPhotosRequestValidatorTests`.

### FR-2: Test coverage for PhotobankValidationHelpers.BeValidRegex
Because `BeValidRegex` (`backend/src/Anela.Heblo.Application/Features/Photobank/Validators/PhotobankValidationHelpers.cs`) is `internal static` and has no direct tests anywhere, and the `Anela.Heblo.Application` project already grants `InternalsVisibleTo("Anela.Heblo.Tests")`, add focused unit tests calling it directly (in addition to the indirect coverage from FR-1), so its behavior is verified in isolation from any one validator.

**Acceptance criteria:**
- `BeValidRegex(null)` returns `true` (helper treats null/whitespace as "nothing to validate").
- `BeValidRegex("")` and `BeValidRegex("   ")` return `true`.
- `BeValidRegex("^[a-z]+$")` (valid pattern) returns `true`.
- `BeValidRegex("[")` (unbalanced bracket, invalid pattern) returns `false`.
- `BeValidRegex("(unclosed")` (unbalanced parenthesis, invalid pattern) returns `false`.
- These tests may live in the same test class as FR-1 (e.g. as a nested `BeValidRegex`-focused fact group) or in a separate `PhotobankValidationHelpersTests` class — either satisfies this requirement; the reviewing architect should pick one placement, but a separate class mirroring the helper's own file is preferred for discoverability given the helper is shared by both `AddRuleRequestValidator` and `UpdateRuleRequestValidator`.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — these are synchronous, in-memory FluentValidation unit tests with no I/O. Each test must run in well under 100ms; the full new test class should add negligible time to the existing `dotnet test` run.

### NFR-2: Security
Not applicable — no new production code, no auth, no data sensitivity concerns. `PathPattern` regex validation itself is a defense against runtime crashes/ReDoS-adjacent issues, but scoping a catastrophic-backtracking detector is explicitly out of scope (see Out of Scope).

## Data Model
No data model changes. Tests operate on the existing `UpdateRuleRequest` DTO (`backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/UpdateRule/UpdateRuleRequest.cs`):
```
public class UpdateRuleRequest : IRequest<UpdateRuleResponse>
{
    public int Id { get; set; }
    public string PathPattern { get; set; } = null!;
    public string TagName { get; set; } = null!;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}
```

## API / Interface Design
Not applicable — no controller, endpoint, or contract changes. This is test-only work against an existing internal validator class.

## Dependencies
- `FluentValidation.TestHelper` (already referenced by `Anela.Heblo.Tests`, used by existing validator test classes).
- `xunit` (existing test framework in `Anela.Heblo.Tests`).
- `Anela.Heblo.Application` → `Anela.Heblo.Tests` `InternalsVisibleTo` grant (already present in `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`), required for FR-2 to call the `internal` `PhotobankValidationHelpers.BeValidRegex` directly.
- No new NuGet packages or project references are required.

## Out of Scope
- Any change to `UpdateRuleRequestValidator.cs`, `PhotobankValidationHelpers.cs`, `UpdateRuleRequest.cs`, or `UpdateRuleHandler.cs` production logic.
- Tests for `UpdateRuleHandler` or the controller/endpoint that consumes `UpdateRuleRequest` (handler-level tests are a separate concern and not part of this coverage gap).
- Defending against catastrophic-backtracking (ReDoS) regex patterns — `BeValidRegex` only checks syntactic validity via `Regex` construction, not runtime safety; adding ReDoS protection would be a separate feature.
- Tests for `AddRuleRequestValidator` (a sibling validator that also uses `BeValidRegex`) — out of scope unless its own coverage gap is filed separately; FR-2's direct helper tests provide the missing coverage without needing to touch that file.
- Integration/E2E tests — this is a pure unit-test task at the validator layer.

## Open Questions
None.

## Status: COMPLETE
