# Design: Fix misleading TargetAmount validation message and add SubmitStockTakingRequestValidator test coverage

## Component Design

### `SubmitStockTakingRequestValidator`
- **Path**: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequestValidator.cs`
- **Responsibility**: FluentValidation validator for `SubmitStockTakingRequest`. No behavioral change to validation rules.
- **Change**: Line 19 `WithMessage` string only. The `LessThan(100000)` rule condition is untouched — only the human-readable message text is corrected to state "100,000" instead of the incorrect "1,000".
  - Before: message references "1,000" (mismatched with the actual `100000` threshold).
  - After: message references "100,000" (matches the threshold enforced by the rule).
- **Existing rules retained as-is** (documented here only to scope the test plan, not to be modified):
  - `ProductCode`: `NotEmpty()`, `MaximumLength(50)`.
  - `TargetAmount`: `GreaterThanOrEqualTo(0)`, `LessThan(100000)` (message corrected as above).

### `SubmitStockTakingRequestValidatorTests` (new)
- **Path**: `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/SubmitStockTakingRequestValidatorTests.cs`
- **Responsibility**: Unit test coverage for `SubmitStockTakingRequestValidator`, following the centralized validator-test convention already established by `GetCatalogDetailRequestValidatorTests.cs` and `UpdateProductCompositionOrderRequestValidatorTests.cs` in the same folder.
- **Framework/style**: xUnit + FluentValidation.TestHelper (`TestValidate()`), asserting via `ShouldHaveValidationErrorFor(...).WithErrorMessage(...)` and `ShouldNotHaveAnyValidationErrors()`. No mocks/dependencies required — the validator is constructed directly.
- **Interface/contract**: Standard xUnit test class, one `[Fact]` (or `[Theory]` with `[InlineData]` for boundary permutations) per scenario below. No public API beyond the test methods themselves; not consumed by other components.

## Data Schemas

No database, API, or DTO schema changes. `SubmitStockTakingRequest` shape is unchanged:

```csharp
public class SubmitStockTakingRequest
{
    public string ProductCode { get; set; }   // NotEmpty, MaximumLength(50)
    public decimal TargetAmount { get; set; } // GreaterThanOrEqualTo(0), LessThan(100000)
    // ...other existing members, unchanged
}
```

Note (out of scope for this change, not to be touched): `TargetAmount` also carries a DataAnnotations `[Range(0, 999999.99)]` attribute that disagrees with the FluentValidation `LessThan(100000)` rule. This inconsistency is flagged for a future follow-up only.

### Test scenario matrix (drives the new test file's cases; not new schema, documented for completeness)

| Field | Case | Value | Expected |
|---|---|---|---|
| TargetAmount | valid, well below upper bound | 500 | valid |
| TargetAmount | invalid, above upper bound | 100001 | invalid, error message references "100,000" (corrected text) |
| TargetAmount | valid, just below upper bound | 99999 | valid |
| TargetAmount | invalid, at upper bound | 100000 | invalid (rule is `LessThan`, boundary itself fails), corrected message |
| TargetAmount | valid, at lower bound | 0 | valid |
| TargetAmount | invalid, below lower bound | -1 | invalid |
| TargetAmount | valid, just above lower bound | 1 | valid |
| ProductCode | invalid | null | invalid (NotEmpty) |
| ProductCode | invalid | "" (empty) | invalid (NotEmpty) |
| ProductCode | invalid, over max length | 51 chars | invalid (MaximumLength(50)) |
| ProductCode | valid, at max length | 50 chars | valid |
| ProductCode | valid, typical value | e.g. "PRODUCT001" | valid |
| Combined | fully valid request | valid ProductCode + in-range TargetAmount | `ShouldNotHaveAnyValidationErrors()` |
