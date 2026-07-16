# Design: Unit tests for CalculateBatchByIngredientRequestValidator

## Component Design

One new xUnit test class, no production code:

- **`CalculateBatchByIngredientRequestValidatorTests`** — `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchByIngredientRequestValidatorTests.cs`, namespace `Anela.Heblo.Tests.Features.Manufacture`. Field-inits `CalculateBatchByIngredientRequestValidator` once and exercises it via `FluentValidation.TestHelper.TestValidate(...)`, mirroring the sibling `CalculateBatchPlanRequestValidatorTests` in the same folder. Test methods:
  - `Validate_ValidRequest_PassesValidation` (`[Fact]`)
  - `Validate_DesiredIngredientAmount_BelowOrEqualZero_FailsValidation` (`[Theory]`: `0`, `-1`, `-0.01`)
  - `Validate_DesiredIngredientAmount_ValidPositiveValue_PassesValidation` (`[Theory]`: `0.01`, `100`, `999999.99`)
  - `Validate_DesiredIngredientAmount_AboveUpperBound_FailsValidation` (`[Theory]`: `1000000`, `999999.991`)
  - `Validate_ProductCode_Empty_FailsValidation` (`[Theory]`: `""`, `" "`, `null`)
  - `Validate_ProductCode_MaxLength_Boundary` (50 chars passes, 51 chars fails)
  - `Validate_IngredientCode_Empty_FailsValidation` (`[Theory]`: `""`, `" "`, `null`)
  - `Validate_IngredientCode_MaxLength_Boundary` (50 chars passes, 51 chars fails)

No other components are introduced or modified. `CalculateBatchByIngredientRequestValidator` and `CalculateBatchByIngredientRequest` are consumed as-is, unchanged.

## Data Schemas

None. Tests exercise the existing DTO only — no schema, API, or event payload changes:

```csharp
public class CalculateBatchByIngredientRequest : IRequest<CalculateBatchByIngredientResponse>
{
    public string ProductCode { get; set; } = null!;
    public string IngredientCode { get; set; } = null!;
    public double DesiredIngredientAmount { get; set; }
}
```
