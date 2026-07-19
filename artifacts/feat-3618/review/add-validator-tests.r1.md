# Code Review: add-validator-tests

## Summary
The new test class `CalculateBatchByIngredientRequestValidatorTests` implements all 8 required test cases from the spec, correctly asserting against the right properties for each of the three validation rules (`ProductCode`, `IngredientCode`, `DesiredIngredientAmount`). It mirrors the sibling `CalculateBatchPlanRequestValidatorTests.cs` structure precisely, and no production code was touched.

## Review Result: PASS

### task: add-validator-tests
**Status:** PASS

## Overall Notes
Verification performed:

- **Spec compliance**: All 8 test cases from the task spec are present with matching names and behavior:
  1. `Validate_ValidRequest_PassesValidation` — happy path, `ShouldNotHaveAnyValidationErrors()`.
  2. `Validate_DesiredIngredientAmount_BelowOrEqualZero_FailsValidation` — `[InlineData(0, -1, -0.01)]`.
  3. `Validate_DesiredIngredientAmount_ValidPositiveValue_PassesValidation` — `[InlineData(0.01, 100, 999999.99)]`.
  4. `Validate_DesiredIngredientAmount_AboveUpperBound_FailsValidation` — `[InlineData(1000000, 999999.991)]`.
  5. `Validate_ProductCode_Empty_FailsValidation` — `[InlineData("", " ", null)]`, `string?` param.
  6. `Validate_ProductCode_MaxLength_Boundary` — 50 chars passes, 51 chars fails, using `new string('A', 50/51)`.
  7. `Validate_IngredientCode_Empty_FailsValidation` — same pattern as #5.
  8. `Validate_IngredientCode_MaxLength_Boundary` — same pattern as #6.
  Each test that targets one field keeps the other fields set to valid values, per the spec's isolation requirement.

- **Architecture/convention adherence**: Namespace `Anela.Heblo.Tests.Features.Manufacture`, validator field instantiated in the constructor (not per-test `new`), `// Arrange` / `// Act` / `// Assert` blocks present in every test, `TestValidate` + `ShouldHaveValidationErrorFor`/`ShouldNotHaveValidationErrorFor`/`ShouldNotHaveAnyValidationErrors` used exclusively (no message-text assertions), required usings present — all matching the sibling file's conventions exactly.

- **No production code changes**: `git diff --stat HEAD~1 HEAD -- . ':!artifacts'` shows exactly one file changed: the new test file (190 insertions, 0 deletions). `git show --stat HEAD` confirms the only non-artifact file touched is `CalculateBatchByIngredientRequestValidatorTests.cs`; the validator, request DTO, and all `.csproj` files are untouched.

- **Build and tests pass**: Ran `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CalculateBatchByIngredientRequestValidatorTests"` from the repo root. Build succeeded (only pre-existing nullable-reference warnings in unrelated files, plus two expected `CS8601` warnings in the new file itself from assigning `null` to `string?`-typed local variables in the `[Theory]` boundary tests — consistent with the identical pattern already present in the sibling file). Result: `Passed! - Failed: 0, Passed: 17, Skipped: 0, Total: 17, Duration: 29 ms`, matching the developer's reported outcome.

No issues found. No documentation updates are required for this test-only change.
