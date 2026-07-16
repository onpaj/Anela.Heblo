# Specification: Unit Test Coverage for CreateManufactureDifficultyRequestValidator

## Summary
`CreateManufactureDifficultyRequestValidator` (FluentValidation validator for the "create manufacture difficulty" use case in the Catalog module) currently has 0% line coverage. This work adds a focused xUnit test suite covering all validation rules in the validator, including the cross-field `ValidFrom`/`ValidTo` date invariant, so that regressions in this validation logic are caught automatically instead of silently reaching production.

## Background
A weekly automated coverage-gap routine flagged `backend/src/Anela.Heblo.Application/Features/Catalog/Validators/CreateManufactureDifficultyRequestValidator.cs` as having no test coverage (filter threshold: 60%, actual: 0.0%), based on CI run #28968007617. The validator guards creation of `ManufactureDifficulty` records, which carry a validity window (`ValidFrom`/`ValidTo`) used to determine which difficulty value is "active" for a product at a given point in time. If the validator fails to reject an inverted or degenerate date range (`ValidFrom >= ValidTo`), a record can be created whose validity window can never be satisfied, or is ambiguous, causing any downstream "active difficulty as of date X" query to silently return no result or the wrong result. This is a pure test-authoring task: no changes to the validator or DTO are required or expected — the goal is to codify current, correct behavior as regression tests, and to explicitly document present-but-arguably-underspecified behavior (single-sided date ranges) as intentional via a passing test, per the brief's request to "confirm intended."

## Functional Requirements

### FR-1: Test project and file placement
Add a new test class `CreateManufactureDifficultyRequestValidatorTests` to the existing test project, following the established convention for other Catalog validators in this codebase (e.g. `SubmitStockTakingRequestValidatorTests`, `GetCatalogDetailRequestValidatorTests`).

**Acceptance criteria:**
- New file at `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/CreateManufactureDifficultyRequestValidatorTests.cs`.
- Namespace `Anela.Heblo.Tests.Features.Catalog.Validators`, matching sibling files in the same directory.
- Uses xUnit (`[Fact]`, `[Theory]`/`[InlineData]`) and `FluentValidation.TestHelper` (`TestValidate`, `ShouldHaveValidationErrorFor`, `ShouldNotHaveValidationErrorFor`, `WithErrorMessage`), matching the pattern used by every existing `*ValidatorTests.cs` file in `backend/test`.
- Test class instantiates `CreateManufactureDifficultyRequestValidator` once (constructor or a private field), matching the pattern in `SubmitStockTakingRequestValidatorTests` and `CalculateBatchPlanRequestValidatorTests`.
- A private helper (e.g. `ValidRequest()`) builds a baseline valid `CreateManufactureDifficultyRequest` (valid `ProductCode`, non-negative `DifficultyValue`, `ValidFrom`/`ValidTo` both null or both set with `ValidFrom < ValidTo`) that individual tests mutate — matching the `ValidRequest()` helper pattern in `SubmitStockTakingRequestValidatorTests`.

### FR-2: `ProductCode` validation coverage
Cover the `NotEmpty` and `MaximumLength(50)` rules on `ProductCode`.

**Acceptance criteria:**
- `ProductCode = null` → validation error on `ProductCode` with message `"Product code is required"`.
- `ProductCode = ""` (empty string) → validation error on `ProductCode` with message `"Product code is required"`.
- A typical valid code (e.g. `"PROD001"`) → no validation error on `ProductCode`.
- `ProductCode` of exactly 50 characters → no validation error on `ProductCode` (upper boundary, inclusive).
- `ProductCode` of exactly 51 characters → validation error on `ProductCode` with message `"Product code cannot exceed 50 characters"` (upper boundary, exclusive).
- Note: FluentValidation's default `NotEmpty` also rejects whitespace-only strings; a whitespace-only `ProductCode` (e.g. `" "`) is out of scope unless the implementer wants an extra `[InlineData]` case — not required, but permitted as an additional case under FR-2 since it exercises the same rule.

### FR-3: `DifficultyValue` validation coverage
Cover the `GreaterThanOrEqualTo(0)` rule on `DifficultyValue`.

**Acceptance criteria:**
- `DifficultyValue = -1` → validation error on `DifficultyValue` with message `"Difficulty value must be non-negative"`.
- `DifficultyValue = 0` → no validation error on `DifficultyValue` (lower boundary, inclusive — confirms `GreaterThanOrEqualTo` semantics).
- `DifficultyValue = 1` (or another typical positive value) → no validation error on `DifficultyValue`.

### FR-4: `ValidFrom` / `ValidTo` cross-field date range coverage
Cover both `RuleFor(x => x.ValidFrom)` and `RuleFor(x => x.ValidTo)` rules, each guarded by `.When(x => x.ValidFrom.HasValue && x.ValidTo.HasValue)`.

**Acceptance criteria:**
- `ValidFrom` and `ValidTo` both set, `ValidFrom < ValidTo` (e.g. one day apart) → no validation error on either `ValidFrom` or `ValidTo`.
- `ValidFrom` and `ValidTo` both set and exactly equal → validation error on `ValidFrom` (message `"ValidFrom must be earlier than ValidTo"`) AND validation error on `ValidTo` (message `"ValidTo must be later than ValidFrom"`), since both rules fire independently for the equal case (`LessThan`/`GreaterThan` are strict/exclusive).
- `ValidFrom` and `ValidTo` both set, `ValidFrom > ValidTo` → validation error on both `ValidFrom` and `ValidTo` with the respective messages above.
- Only `ValidFrom` set (`ValidTo = null`) → no validation error on `ValidFrom` or `ValidTo` (the `.When` guard short-circuits the cross-field rule; this test documents/confirms the current behavior is intentional, per the brief).
- Only `ValidTo` set (`ValidFrom = null`) → no validation error on `ValidFrom` or `ValidTo` (same rationale as above).
- Neither `ValidFrom` nor `ValidTo` set (both null) → no validation error on either field.

### FR-5: Whole-request happy-path coverage
At least one test validates a fully-populated, entirely valid request produces zero validation errors overall (not just per-field), to guard against an unrelated future rule addition silently breaking an otherwise-valid request.

**Acceptance criteria:**
- A `CreateManufactureDifficultyRequest` with a valid `ProductCode`, `DifficultyValue >= 0`, and either no dates or a valid `ValidFrom < ValidTo` pair → `result.IsValid` is `true` and `result.Errors` is empty (mirrors the `ValidRequest_PassesAllValidation` test in `SubmitStockTakingRequestValidatorTests`).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — these are pure in-memory unit tests against a FluentValidation validator with no I/O, database, or network dependencies. Entire suite should execute in well under 1 second.

### NFR-2: Security
Not applicable — no new production code, no new attack surface. Tests do not touch auth, secrets, or external systems.

### NFR-3: Maintainability / Consistency
Test naming should follow the `MethodOrField_Scenario_ExpectedOutcome` convention observed across existing validator test files in this codebase (e.g. `ProductCode_Exactly50Characters_PassesValidation`, `TargetAmount_Negative_HasCorrectErrorMessage`). Prefer `[Theory]`/`[InlineData]` for parameterized boundary cases (mirrors `SubmitStockTakingRequestValidatorTests`) and `[Fact]` for single-scenario cross-field cases (mirrors the `Validate_InvalidDateRange_FailsValidation` style in `CalculateBatchPlanRequestValidatorTests`).

## Data Model
No data model changes. Tests operate directly against the existing DTO:

```csharp
public class CreateManufactureDifficultyRequest : IRequest<CreateManufactureDifficultyResponse>
{
    public string ProductCode { get; set; } = null!;
    public int DifficultyValue { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}
```

Note: the DTO also carries `[Required]` / `[Range(0, int.MaxValue, ...)]` data-annotation attributes on `ProductCode` and `DifficultyValue` respectively. These are enforced by ASP.NET model binding at the HTTP boundary, not by FluentValidation, and are **not** in scope for this validator's unit tests — this spec covers only the `CreateManufactureDifficultyRequestValidator` (FluentValidation) rules.

## API / Interface Design
No new or changed API surface. This is test-only work against an existing, unmodified validator class:

`backend/src/Anela.Heblo.Application/Features/Catalog/Validators/CreateManufactureDifficultyRequestValidator.cs`

No production code in `CreateManufactureDifficultyRequestValidator.cs`, `CreateManufactureDifficultyRequest.cs`, `CreateManufactureDifficultyResponse.cs`, or `CreateManufactureDifficultyHandler.cs` is to be modified as part of this task.

## Dependencies
- `FluentValidation` and `FluentValidation.TestHelper` (already referenced by the test project — see existing `*ValidatorTests.cs` files).
- `xUnit` (already the test framework in use across `backend/test/Anela.Heblo.Tests`).
- No new NuGet packages, no new test project, no changes to `backend/test/Anela.Heblo.Tests.csproj` expected.

## Out of Scope
- Any change to `CreateManufactureDifficultyRequestValidator`'s actual validation rules or error messages.
- Any change to the DTO, handler, controller, or persistence layer for manufacture difficulty.
- Integration/end-to-end tests exercising the full create-manufacture-difficulty HTTP flow.
- Testing the DTO's `[Required]`/`[Range]` data annotations (these are model-binding concerns, separate from the FluentValidation validator under test).
- Deciding/changing whether single-sided date ranges (`ValidFrom` set without `ValidTo`, or vice versa) *should* be rejected — this spec only requires documenting current behavior via a passing test, not changing it.
- Coverage of any other validator in the Catalog module.

## Open Questions
None.

## Status: COMPLETE
