# Implementation: add-updaterulerequestvalidator-tests

## What was implemented
Added unit tests closing the coverage gap on `UpdateRuleRequestValidator` and its dependency
`PhotobankValidationHelpers.BeValidRegex`. Pure test-authoring task — no production code was
changed.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Photobank/UpdateRuleRequestValidatorTests.cs` — FluentValidation `TestValidate`-based tests covering every rule in `UpdateRuleRequestValidator` (Id, PathPattern, TagName, SortOrder), including boundary cases (500/501-char PathPattern, 100/101-char TagName, SortOrder 0/-1, Id 0/-1) and regex validity checks.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankValidationHelpersTests.cs` — direct unit tests for the internal static `PhotobankValidationHelpers.BeValidRegex(string?)` helper (null/empty/whitespace, valid regex, invalid regex).

## Tests
- `UpdateRuleRequestValidatorTests`:
  - `ValidRequest_PassesValidation` — baseline fully-valid request produces no errors.
  - `Id_NotPositive_FailsValidation` (Theory: 0, -1) — asserts `"Id must be a positive integer"`.
  - `PathPattern_Empty_FailsValidation` (Theory: "", null) — asserts `"PathPattern is required"`.
  - `PathPattern_TooLong_FailsValidation` (501 chars) — asserts `"PathPattern cannot exceed 500 characters"`.
  - `PathPattern_MaxLength_PassesValidation` (500 chars, boundary) — no error.
  - `PathPattern_InvalidRegex_FailsValidation` (`"["`) — asserts `"Invalid regular expression pattern."`.
  - `PathPattern_ValidRegex_PassesValidation` (`"^[a-z]+"`) — no error.
  - `TagName_Empty_FailsValidation` (Theory: "", null) — asserts `"TagName is required"`.
  - `TagName_TooLong_FailsValidation` (101 chars) — asserts `"TagName cannot exceed 100 characters"`.
  - `SortOrder_Negative_FailsValidation` (-1) — asserts error exists (no custom message on this rule).
  - `SortOrder_Zero_PassesValidation` (boundary) — no error.
- `PhotobankValidationHelpersTests`:
  - `BeValidRegex_NullOrWhitespace_ReturnsTrue` (Theory: null, "", "   ") — returns true.
  - `BeValidRegex_ValidPattern_ReturnsTrue` (`"^[a-z]+$"`) — returns true.
  - `BeValidRegex_InvalidPattern_ReturnsFalse` (Theory: `"["`, `"(unclosed"`) — returns false.

Total: 20 new test cases (11 methods across the two files, several parameterized via `[Theory]`).

## How to verify
1. `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — build succeeds.
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Photobank.UpdateRuleRequestValidatorTests|FullyQualifiedName~Anela.Heblo.Tests.Features.Photobank.PhotobankValidationHelpersTests"` — `Passed! - Failed: 0, Passed: 20, Skipped: 0, Total: 20`.
3. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — full suite run; the only failures observed (76) are pre-existing Testcontainers/Docker-dependent integration tests (`LeafletRepositoryIntegrationTests`, `AuthorizationIntegrationTests`) that fail because no Docker daemon is available in this sandbox (`docker ps` confirms: "failed to connect to the docker API"). No test under `Features/Photobank` failed or was affected.
4. `git status --short` shows only the two new test files added (plus a pre-existing, pipeline-managed change to `artifacts/feat-3616/state.json` that predates this task and was left untouched).

## Notes
- `dotnet format ... --include <the two files>` was run and reported "Format complete" with no diffs, confirming the new files already match the repo's formatting conventions.
- No production source file was modified; `UpdateRuleRequestValidator.cs`, `PhotobankValidationHelpers.cs`, `UpdateRuleRequest.cs`, and `UpdateRuleHandler.cs` are untouched.
- `artifacts/feat-3616/state.json` had a pending modification (pipeline status tracking) present before this task started; it was not touched or committed as part of this change.

## Status
DONE
