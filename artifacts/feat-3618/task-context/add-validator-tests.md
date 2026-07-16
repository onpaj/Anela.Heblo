### task: add-validator-tests

**Goal**
Add a new xUnit test class `CalculateBatchByIngredientRequestValidatorTests` that closes the 0% coverage gap on `CalculateBatchByIngredientRequestValidator`, covering all three validation rules (`ProductCode`, `IngredientCode`, `DesiredIngredientAmount`) per FR-1 through FR-8 of `artifacts/feat-3618/spec.r1.md`.

**File to create**
`backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchByIngredientRequestValidatorTests.cs`

Do not modify any other file. In particular:
- Do NOT modify `backend/src/Anela.Heblo.Application/Features/Manufacture/Validators/CalculateBatchByIngredientRequestValidator.cs`
- Do NOT modify `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchByIngredient/CalculateBatchByIngredientRequest.cs`
- Do NOT modify any `.csproj` (no new package references are needed — `FluentValidation.TestHelper` is already available transitively, exactly as consumed by the sibling test file below)

**System under test (read-only reference — do not change)**

`backend/src/Anela.Heblo.Application/Features/Manufacture/Validators/CalculateBatchByIngredientRequestValidator.cs`:
```csharp
public class CalculateBatchByIngredientRequestValidator : AbstractValidator<CalculateBatchByIngredientRequest>
{
    public CalculateBatchByIngredientRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.IngredientCode)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.DesiredIngredientAmount)
            .GreaterThan(0)
            .LessThanOrEqualTo(999999.99);
    }
}
```
(`.WithMessage(...)` calls omitted above for brevity — actual file has them; tests must not assert on message text, only on which property has an error.)

`CalculateBatchByIngredientRequest` (in `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchByIngredient/CalculateBatchByIngredientRequest.cs`):
```csharp
public class CalculateBatchByIngredientRequest : IRequest<CalculateBatchByIngredientResponse>
{
    public string ProductCode { get; set; } = null!;
    public string IngredientCode { get; set; } = null!;
    public double DesiredIngredientAmount { get; set; }
}
```

**Convention to follow (mirror exactly)**

Sibling file: `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanRequestValidatorTests.cs`. Match its structure precisely:
- Namespace `Anela.Heblo.Tests.Features.Manufacture`, one public test class, no base class.
- Validator instantiated once via a `private readonly` field, assigned in the constructor (not `[Fact]`-local `new`):
  ```csharp
  private readonly CalculateBatchByIngredientRequestValidator _validator;

  public CalculateBatchByIngredientRequestValidatorTests()
  {
      _validator = new CalculateBatchByIngredientRequestValidator();
  }
  ```
- Every test method has `// Arrange`, `// Act`, `// Assert` comment blocks, in that order.
- Use `_validator.TestValidate(request)` for the Act step, and `ShouldNotHaveAnyValidationErrors()` / `ShouldHaveValidationErrorFor(x => x.Prop)` / `ShouldNotHaveValidationErrorFor(x => x.Prop)` for assertions (from `FluentValidation.TestHelper`).
- `[Theory]`/`[InlineData]` for parameterized cases (e.g. the sibling's `Validate_InvalidSemiproductCode_FailsValidation(string? semiproductCode)` with `[InlineData("")]`, `[InlineData(" ")]`, `[InlineData(null)]`).
- Required usings:
  ```csharp
  using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;
  using Anela.Heblo.Application.Features.Manufacture.Validators;
  using FluentValidation.TestHelper;
  using Xunit;
  ```
- When a test targets one field, keep the other fields set to valid values so a failure is unambiguously attributable to the rule under test (e.g. when testing `ProductCode` invalidity, still set a valid `IngredientCode` and a valid `DesiredIngredientAmount`).

**Test cases to implement** (from spec FR-2 through FR-8; method names are suggestions from `design.r1.md` — keep them or use equivalents that clearly convey the same scenario)

1. `Validate_ValidRequest_PassesValidation` — `[Fact]`. Valid request, e.g. `ProductCode = "PROD001"`, `IngredientCode = "ING001"`, `DesiredIngredientAmount = 100`. Assert `result.ShouldNotHaveAnyValidationErrors()`.

2. `Validate_DesiredIngredientAmount_BelowOrEqualZero_FailsValidation` — `[Theory]` with `[InlineData(0)]`, `[InlineData(-1)]`, `[InlineData(-0.01)]`. Other fields valid. Assert `result.ShouldHaveValidationErrorFor(x => x.DesiredIngredientAmount)`.

3. `Validate_DesiredIngredientAmount_ValidPositiveValue_PassesValidation` — `[Theory]` with `[InlineData(0.01)]`, `[InlineData(100)]`, `[InlineData(999999.99)]` (covers FR-3 passing case and the FR-4 upper boundary-passes case). Assert `result.ShouldNotHaveValidationErrorFor(x => x.DesiredIngredientAmount)`.

4. `Validate_DesiredIngredientAmount_AboveUpperBound_FailsValidation` — `[Theory]` with `[InlineData(1000000)]`, `[InlineData(999999.991)]`. Assert `result.ShouldHaveValidationErrorFor(x => x.DesiredIngredientAmount)`.

5. `Validate_ProductCode_Empty_FailsValidation` — `[Theory]` with `[InlineData("")]`, `[InlineData(" ")]`, `[InlineData(null)]`, parameter typed `string?`. Other fields valid (valid `IngredientCode`, valid `DesiredIngredientAmount`). Assert `result.ShouldHaveValidationErrorFor(x => x.ProductCode)`.

6. `Validate_ProductCode_MaxLength_Boundary` — one case with a 50-character `ProductCode` asserting `result.ShouldNotHaveValidationErrorFor(x => x.ProductCode)`, and one case with a 51-character `ProductCode` asserting `result.ShouldHaveValidationErrorFor(x => x.ProductCode)`. Implement as two `[Fact]`s or a `[Theory]` with a length/expected-outcome pair — either is acceptable; generate the strings with `new string('A', 50)` / `new string('A', 51)` rather than hardcoding literal 50/51-char strings.

7. `Validate_IngredientCode_Empty_FailsValidation` — `[Theory]` with `[InlineData("")]`, `[InlineData(" ")]`, `[InlineData(null)]`, parameter typed `string?`. Other fields valid. Assert `result.ShouldHaveValidationErrorFor(x => x.IngredientCode)`.

8. `Validate_IngredientCode_MaxLength_Boundary` — same pattern as case 6 but for `IngredientCode`: 50 chars passes, 51 chars fails.

**Definition of done**
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchByIngredientRequestValidatorTests.cs` exists with the 8 test cases above (or a superset that still satisfies every FR), following the sibling file's structure and naming style.
- No file other than this new test file is created or modified — verify with `git status` / `git diff --stat` showing only this one new file.
- `dotnet build` succeeds for the solution.
- `dotnet test --filter "FullyQualifiedName~CalculateBatchByIngredientRequestValidatorTests"` passes (all new tests green).
- Full `dotnet test` run for `Anela.Heblo.Tests` shows no regressions (all previously-passing tests still pass).
- `dotnet format` produces no changes to the new file (or is run and any formatting diffs are accepted before finishing).
