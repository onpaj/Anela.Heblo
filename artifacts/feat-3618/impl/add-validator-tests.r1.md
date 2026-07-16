# Implementation: add-validator-tests

## What was implemented
Added a new xUnit test class `CalculateBatchByIngredientRequestValidatorTests` covering all three FluentValidation rules on `CalculateBatchByIngredientRequestValidator`: the `DesiredIngredientAmount` lower bound (`> 0`) and upper bound (`<= 999999.99`), and the `ProductCode`/`IngredientCode` required + max-length(50) rules. No production code was modified.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchByIngredientRequestValidatorTests.cs` — 8 test methods (1 happy-path `[Fact]`, 4 `[Theory]` bound checks, 2 `[Fact]` max-length boundary checks, 1 more `[Theory]`), mirroring the conventions of the sibling `CalculateBatchPlanRequestValidatorTests.cs`.

## Tests
- `Validate_ValidRequest_PassesValidation` — happy path, no errors.
- `Validate_DesiredIngredientAmount_BelowOrEqualZero_FailsValidation` — `0`, `-1`, `-0.01`.
- `Validate_DesiredIngredientAmount_ValidPositiveValue_PassesValidation` — `0.01`, `100`, `999999.99` (upper boundary pass).
- `Validate_DesiredIngredientAmount_AboveUpperBound_FailsValidation` — `1000000`, `999999.991`.
- `Validate_ProductCode_Empty_FailsValidation` — `""`, `" "`, `null`.
- `Validate_ProductCode_MaxLength_Boundary` — 50 chars passes, 51 chars fails.
- `Validate_IngredientCode_Empty_FailsValidation` — `""`, `" "`, `null`.
- `Validate_IngredientCode_MaxLength_Boundary` — 50 chars passes, 51 chars fails.

## How to verify
```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CalculateBatchByIngredientRequestValidatorTests" --no-build
```
Result: 17/17 passed (8 test methods, several `[Theory]`-expanded).

Full `Anela.Heblo.Tests` suite was also run for regressions: 5851 passed, 76 failed, 4 skipped. All 76 failures are pre-existing Docker/Testcontainers-dependent integration tests (Postgres containers) that cannot run in this sandbox (no Docker daemon) — unrelated to this change. None reference `CalculateBatchByIngredient` or the `Manufacture.Validators` namespace.

## Notes
No deviations from the task plan. No production code, csproj, or any other file was touched — `git status` shows exactly one new file.

## PR Summary
Closes the 0% test-coverage gap on `CalculateBatchByIngredientRequestValidator` by adding a focused unit test suite covering its `DesiredIngredientAmount` bounds and the `ProductCode`/`IngredientCode` required + max-length rules. Test-only change, no production code modified.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchByIngredientRequestValidatorTests.cs` — new test file, 8 test methods covering all validator rules

## Status
DONE
