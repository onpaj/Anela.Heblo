# Specification: Unit tests for CalculateBatchByIngredientRequestValidator

## Summary
Add a focused xUnit + FluentValidation.TestHelper test suite for `CalculateBatchByIngredientRequestValidator`, covering the `DesiredIngredientAmount` numeric bounds and the `ProductCode`/`IngredientCode` required + max-length(50) rules. This closes a 0% coverage gap on a validator that guards inputs feeding directly into manufacture batch planning.

## Background
`CalculateBatchByIngredientRequestValidator` (`backend/src/Anela.Heblo.Application/Features/Manufacture/Validators/CalculateBatchByIngredientRequestValidator.cs`) validates `CalculateBatchByIngredientRequest` before it reaches the batch calculation service. It currently has no tests (0.0% line coverage, filter threshold 60%), flagged by the weekly coverage-gap routine on 2026-07-13 (CI run #28968007617). The validator enforces three rules:

- `ProductCode`: `NotEmpty()`, `MaximumLength(50)`
- `IngredientCode`: `NotEmpty()`, `MaximumLength(50)`
- `DesiredIngredientAmount` (double): `GreaterThan(0)`, `LessThanOrEqualTo(999999.99)`

If a refactor silently drops or weakens the `DesiredIngredientAmount` bounds, an invalid amount (zero, negative, or unreasonably large) could flow into batch calculations, risking division-by-zero or negative/overflowing batch sizes downstream. This task adds regression tests only — no production code changes.

A sibling test in the same module, `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanRequestValidatorTests.cs`, establishes the local convention: xUnit `[Fact]`/`[Theory]`, `FluentValidation.TestHelper`'s `TestValidate(request)`, and assertions via `ShouldHaveValidationErrorFor(x => ...)` / `ShouldNotHaveValidationErrorFor(x => ...)` / `ShouldNotHaveAnyValidationErrors()`. The new test class follows this same pattern.

## Functional Requirements

### FR-1: New test file `CalculateBatchByIngredientRequestValidatorTests.cs`
Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchByIngredientRequestValidatorTests.cs`, namespace `Anela.Heblo.Tests.Features.Manufacture`, class `CalculateBatchByIngredientRequestValidatorTests`, instantiating `CalculateBatchByIngredientRequestValidator` once (constructor or field init), matching the style of the existing `CalculateBatchPlanRequestValidatorTests` in the same directory.

**Acceptance criteria:**
- File exists at the path above and compiles as part of `Anela.Heblo.Tests`.
- Uses `FluentValidation.TestHelper` (`TestValidate`) and `Xunit`, no new test dependencies introduced.
- No production/source files are modified.

### FR-2: Happy-path validation
A fully valid request (non-empty `ProductCode` and `IngredientCode` within 50 chars, `DesiredIngredientAmount` a positive value at or below 999999.99) produces no validation errors.

**Acceptance criteria:**
- `[Fact] Validate_ValidRequest_PassesValidation` builds a valid request (e.g. `ProductCode = "PROD001"`, `IngredientCode = "ING001"`, `DesiredIngredientAmount = 100`) and asserts `result.ShouldNotHaveAnyValidationErrors()`.

### FR-3: `DesiredIngredientAmount` lower bound (`> 0`)
Zero and negative amounts must fail validation on `DesiredIngredientAmount`; small positive amounts must pass.

**Acceptance criteria:**
- `[Theory]` with `[InlineData(0)]` and `[InlineData(-1)]` (plus a representative fractional negative, e.g. `-0.01`, is optional but recommended) asserts `result.ShouldHaveValidationErrorFor(x => x.DesiredIngredientAmount)`.
- A passing case for a small positive value (e.g. `0.01` or `100`) asserts `result.ShouldNotHaveValidationErrorFor(x => x.DesiredIngredientAmount)`.

### FR-4: `DesiredIngredientAmount` upper bound (`<= 999999.99`)
The boundary value `999999.99` must pass; any value strictly greater than it must fail.

**Acceptance criteria:**
- `[Fact]` or `[Theory]` case with `DesiredIngredientAmount = 999999.99` asserts `result.ShouldNotHaveValidationErrorFor(x => x.DesiredIngredientAmount)`.
- `[Theory]` covering values over the cap (at minimum `1000000`; `999999.991` is a good tight-boundary addition) asserts `result.ShouldHaveValidationErrorFor(x => x.DesiredIngredientAmount)`.

### FR-5: `ProductCode` required
An empty, whitespace-only, or `null` `ProductCode` must fail validation on `ProductCode`; other fields in the request stay valid so the failure is attributable to this rule.

**Acceptance criteria:**
- `[Theory]` with `[InlineData("")]`, `[InlineData(" ")]`, `[InlineData(null)]` asserts `result.ShouldHaveValidationErrorFor(x => x.ProductCode)`, mirroring the `CalculateBatchPlanRequestValidatorTests.Validate_InvalidSemiproductCode_FailsValidation` pattern.

### FR-6: `ProductCode` max length 50
A `ProductCode` of exactly 50 characters must pass; 51+ characters must fail.

**Acceptance criteria:**
- A case with a 50-character `ProductCode` asserts `result.ShouldNotHaveValidationErrorFor(x => x.ProductCode)`.
- A case with a 51-character `ProductCode` asserts `result.ShouldHaveValidationErrorFor(x => x.ProductCode)`.

### FR-7: `IngredientCode` required
An empty, whitespace-only, or `null` `IngredientCode` must fail validation on `IngredientCode`.

**Acceptance criteria:**
- `[Theory]` with `[InlineData("")]`, `[InlineData(" ")]`, `[InlineData(null)]` asserts `result.ShouldHaveValidationErrorFor(x => x.IngredientCode)`.

### FR-8: `IngredientCode` max length 50
A `IngredientCode` of exactly 50 characters must pass; 51+ characters must fail.

**Acceptance criteria:**
- A case with a 50-character `IngredientCode` asserts `result.ShouldNotHaveValidationErrorFor(x => x.IngredientCode)`.
- A case with a 51-character `IngredientCode` asserts `result.ShouldHaveValidationErrorFor(x => x.IngredientCode)`.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — these are in-memory unit tests with no I/O; the full suite should run in well under 1 second.

### NFR-2: Security
Not applicable — no auth, no data sensitivity; validator operates on plain request DTOs.

## Data Model
No new or changed data model. Tests exercise the existing `CalculateBatchByIngredientRequest` DTO (`backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchByIngredient/CalculateBatchByIngredientRequest.cs`):

```csharp
public class CalculateBatchByIngredientRequest : IRequest<CalculateBatchByIngredientResponse>
{
    public string ProductCode { get; set; } = null!;
    public string IngredientCode { get; set; } = null!;
    public double DesiredIngredientAmount { get; set; }
}
```

## API / Interface Design
Not applicable — no API or UI surface changes. This is a backend unit-test-only addition targeting `CalculateBatchByIngredientRequestValidator`.

## Dependencies
- Existing test project `backend/test/Anela.Heblo.Tests` (xUnit, `FluentValidation.TestHelper`) — already referenced by sibling validator tests in the same project, no new package references required.
- No dependency on other in-flight features or external services.

## Out of Scope
- Any change to `CalculateBatchByIngredientRequestValidator.cs` or `CalculateBatchByIngredientRequest.cs` itself.
- Testing the `CalculateBatchByIngredient` handler/use case or `CalculateBatchCalculationService` logic — this task is validator-only.
- Integration/E2E tests — this is a pure unit-test task.
- Broader coverage improvements elsewhere in the `Manufacture` module.

## Open Questions
None.

## Status: COMPLETE
