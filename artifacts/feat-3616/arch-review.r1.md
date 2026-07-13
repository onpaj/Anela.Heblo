# Architecture Review: Unit tests for UpdateRuleRequestValidator (Photobank)

## Skip Design: true

## Architectural Fit Assessment

This is a pure test-authoring task closing a coverage gap on an existing FluentValidation validator. No production code, no contracts, no data flow, and no UI are touched. The validator (`UpdateRuleRequestValidator`) and its dependency (`PhotobankValidationHelpers.BeValidRegex`) already exist and already follow this codebase's established conventions — a `AbstractValidator<T>` registered per use case in `Features/Photobank/Validators/`, tested via `FluentValidation.TestHelper`. The `Anela.Heblo.Tests` project already has direct precedent for this exact shape of validator (`GetPhotosRequestValidatorTests` tests the same `BeValidRegex` dependency via a sibling validator; `BulkAddPhotoTagRequestValidatorTests` follows the same `TestValidate`/`ShouldHaveValidationErrorFor` idiom). `InternalsVisibleTo("Anela.Heblo.Tests")` is already granted on `Anela.Heblo.Application.csproj`, so calling the `internal static` `BeValidRegex` directly requires no project changes. There is nothing architecturally novel here — the work is to add two test files (or one, per the spec's flexibility) in the existing pattern.

## Proposed Architecture

### Component Overview

No new components. Two test artifacts are added to the existing test project:

- `UpdateRuleRequestValidatorTests` — exercises `UpdateRuleRequestValidator` end-to-end per FR-1.
- `PhotobankValidationHelpersTests` — exercises `PhotobankValidationHelpers.BeValidRegex` directly per FR-2.

### Key Design Decisions

1. **Two separate test classes, not one.** The spec explicitly allows either placement, but a dedicated `PhotobankValidationHelpersTests` class is the better default: it mirrors the 1:1 file-to-test-class convention already used everywhere else in this test project (`GetPhotosRequestValidatorTests` ↔ `GetPhotosRequestValidator`, etc.), and `PhotobankValidationHelpers` is a shared dependency of *two* validators (`AddRuleRequestValidator` and `UpdateRuleRequestValidator`), so pinning its contract in its own file makes it discoverable and reusable regardless of which validator changes next. Keeping it nested inside `UpdateRuleRequestValidatorTests` would misleadingly suggest the helper is private to that validator.
2. **Use `[Theory]`/`[InlineData]` for the null/empty/whitespace `BeValidRegex` cases** (mirrors `BulkAddPhotoTagRequestValidatorTests.TagNameEmptyOrWhitespace_FailsValidation`) instead of three near-duplicate `[Fact]`s.
3. **Assert exact error messages via `WithErrorMessage(...)`** wherever the validator defines a custom message (all rules except `SortOrder`, which has no `.WithMessage()` and should only be asserted via `ShouldHaveValidationErrorFor(x => x.SortOrder)` without a message check) — consistent with `GetPhotosRequestValidatorTests` and `BulkAddPhotoTagRequestValidatorTests`.
4. **No mocking, no DI container.** The validator is instantiated directly in the test class constructor, matching every existing Photobank validator test.

## Implementation Guidance

### Directory / Module Structure

Add both files under the existing flat Photobank test folder (no subfolders are used elsewhere in this directory):

```
backend/test/Anela.Heblo.Tests/Features/Photobank/UpdateRuleRequestValidatorTests.cs   (new)
backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankValidationHelpersTests.cs   (new)
```

Namespace: `Anela.Heblo.Tests.Features.Photobank` (matches every sibling file).

### Interfaces and Contracts

No new interfaces. Test surface is:
- `UpdateRuleRequestValidator` (public, existing) — instantiate directly, call `.TestValidate(request)`.
- `PhotobankValidationHelpers.BeValidRegex(string?)` (internal static, existing) — call directly by fully-qualified static reference; visible to the test assembly via the existing `InternalsVisibleTo`.
- `UpdateRuleRequest` (existing DTO) — construct with object initializers per case; only vary the field under test, keep all other fields at valid values (per spec's "fully valid request" baseline) to avoid cross-field noise.

### Data Flow

N/A — synchronous, in-memory, no I/O, no DI.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| FluentValidation's `MaximumLength` boundary (500/501 chars) combined with `Must(BeValidRegex)` could produce two errors on the same property if the 501-char test string isn't also a syntactically valid regex prefix, muddying the "single error" assertion. | Low | Build the >500-char `PathPattern` test values from a benign literal (e.g. repeated `"a"`), which is always a valid regex, so only the length rule fires; assert with `ShouldHaveValidationErrorFor` (not `ShouldHaveExactlyOneValidationErrorFor`) to stay robust either way, matching existing tests' style. |
| Test class name collision or ambiguity between the two new test files if `PhotobankValidationHelpersTests` is later perceived as testing the whole `Validators` folder rather than one helper. | Low | Keep the class scoped strictly to `BeValidRegex` test cases from FR-2's acceptance criteria; do not add unrelated helper tests speculatively. |

## Specification Amendments

None. The spec is complete, unambiguous, and directly actionable; no production code changes are implied or needed.

## Prerequisites

None — `FluentValidation.TestHelper`, `xunit`, and the `InternalsVisibleTo` grant are already in place. No new NuGet packages or project references required.
