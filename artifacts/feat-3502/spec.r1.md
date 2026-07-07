# Specification: Fix misleading TargetAmount validation message and add test coverage for SubmitStockTakingRequestValidator

## Summary
`SubmitStockTakingRequestValidator` enforces an upper bound of 100,000 on `TargetAmount` but its `WithMessage` text incorrectly states "less than 1,000", misleading operators who trigger the rule. This spec covers correcting the message text to match the actual enforced limit and adding unit test coverage for all validation rules in this class, which currently sit at 0% line coverage against a 60% threshold.

## Background
`SubmitStockTakingRequestValidator` (`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequestValidator.cs`) validates incoming stock-taking submissions before they reach the domain layer. A weekly coverage-gap routine flagged this file for having zero test coverage and, in the process, surfaced a real bug: the `LessThan(100000)` rule's error message reads "Target amount must be less than 1,000" — a typo/copy-paste error that understates the actual limit by two orders of magnitude. Any operator who happens to trip the rule (submitting 100,000 or more) sees a confusing message implying the limit is 1,000, when values well above 1,000 (e.g. 500–99,999) are in fact accepted.

The maintainer has confirmed this is a message-only defect: the enforced rule (`LessThan(100000)`) is correct and must not change, since real users currently rely on submitting amounts up to just under 100,000. Only the message text needs correction, and test coverage needs to be added so this class of discrepancy (rule vs. message mismatch) is caught automatically in the future.

## Functional Requirements

### FR-1: Correct the TargetAmount upper-bound error message
The error message associated with the `LessThan(100000)` rule on `TargetAmount` must accurately state the enforced limit of 100,000, replacing the current incorrect text referencing 1,000.

**Acceptance criteria:**
- The `WithMessage` string following `.LessThan(100000)` reads a message referencing "100,000" (e.g. "Target amount must be less than 100,000"), not "1,000".
- The validation rule itself (`LessThan(100000)`) is unchanged — no behavior change to what values are accepted or rejected.
- A `TargetAmount` of `500` still passes validation (proving the effective limit remains 100,000, not 1,000).
- A `TargetAmount` of `100001` fails validation, and the returned error message contains the corrected "100,000" text.
- A `TargetAmount` of exactly `100000` fails validation (boundary is exclusive, per `LessThan`), consistent with existing behavior.

### FR-2: Add unit test coverage for TargetAmount upper bound
Add tests proving both the accepted range and the rejection boundary for the upper bound, and that the corrected message is returned on failure.

**Acceptance criteria:**
- Test: `TargetAmount = 500` → validation passes (`IsValid == true`), no error for `TargetAmount`.
- Test: `TargetAmount = 100001` → validation fails, and the error message for `TargetAmount` matches the corrected text (contains "100,000").
- Test: `TargetAmount = 99999` → validation passes (boundary just under the limit).
- Test: `TargetAmount = 100000` → validation fails (boundary is exclusive).

### FR-3: Add unit test coverage for TargetAmount lower bound
Add tests covering the `GreaterThanOrEqualTo(0)` rule, currently entirely uncovered.

**Acceptance criteria:**
- Test: `TargetAmount = 0` → validation passes (boundary is inclusive).
- Test: `TargetAmount = -1` → validation fails, and the error message for `TargetAmount` matches "Target amount must be greater than or equal to 0".
- Test: `TargetAmount = 1` → validation passes (a representative valid positive value).

### FR-4: Add unit test coverage for ProductCode rules
Add tests covering the `NotEmpty` and `MaximumLength(50)` rules on `ProductCode`, currently entirely uncovered.

**Acceptance criteria:**
- Test: `ProductCode = null` → validation fails, and the error message for `ProductCode` matches "Product code is required".
- Test: `ProductCode = ""` (empty string) → validation fails, and the error message for `ProductCode` matches "Product code is required".
- Test: `ProductCode` with length 51 (over the limit) → validation fails, and the error message for `ProductCode` matches "Product code cannot exceed 50 characters".
- Test: `ProductCode` with length 50 (at the limit) → validation passes.
- Test: `ProductCode` with a typical valid value (e.g. `"ABC123"`) → validation passes with no `ProductCode` error.

### FR-5: Combined valid-request test
Add at least one test exercising a fully valid `SubmitStockTakingRequest` (valid `ProductCode` and `TargetAmount`) to confirm the validator produces `IsValid == true` with zero errors overall, not just per-field.

**Acceptance criteria:**
- A request with `ProductCode = "ABC123"` and `TargetAmount = 500` produces `result.IsValid == true` and `result.Errors.Count == 0`.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a synchronous FluentValidation validator with no I/O; no performance targets beyond existing test-suite execution time norms (tests should run in milliseconds, consistent with other validator unit tests in the codebase).

### NFR-2: Security
Not applicable — no auth, no sensitive data handling changes. This is a validation-message and test-coverage fix confined to input format/range checks already in place.

## Data Model
No data model changes. `SubmitStockTakingRequest` (existing DTO, unchanged by this work) has at minimum:
- `ProductCode` (string) — required, max length 50.
- `TargetAmount` (numeric) — must be `>= 0` and `< 100000`.

## API / Interface Design
No API surface changes. This work is confined to:
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequestValidator.cs` — one-line message text correction.
- A new or existing unit test file (e.g. `SubmitStockTakingRequestValidatorTests.cs`) under the corresponding test project, following existing conventions for FluentValidation validator tests in this codebase (using FluentValidation's `TestValidate()` extension or equivalent).

## Dependencies
- FluentValidation (already in use).
- Existing test project and test framework conventions for this codebase (e.g. xUnit + FluentValidation.TestHelper, matching whatever pattern other validator tests in `Anela.Heblo.Application` tests use).

## Out of Scope
- Changing the enforced `TargetAmount` upper limit (`LessThan(100000)`) — explicitly confirmed as correct and final; not to be tightened to 1,000.
- Any changes to `ProductCode` or `TargetAmount` validation rules themselves beyond the message text correction on the upper bound.
- Broader refactoring of the `SubmitStockTaking` use case, handler, or DTO.
- Localization/i18n of validation messages (out of scope unless an existing pattern already requires it elsewhere in the codebase).

## Open Questions
None.

## Status: COMPLETE
