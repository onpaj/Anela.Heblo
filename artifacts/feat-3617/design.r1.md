# Design: Unit Test Coverage for CreateManufactureDifficultyRequestValidator

## Component Design

### `CreateManufactureDifficultyRequestValidatorTests` (new test class)

**Location:** `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/CreateManufactureDifficultyRequestValidatorTests.cs`
**Namespace:** `Anela.Heblo.Tests.Features.Catalog.Validators`

**Responsibility:** Exercise every `RuleFor(...)` chain in `CreateManufactureDifficultyRequestValidator` in isolation, asserting both the pass/fail outcome and the exact error message per field, plus one whole-request happy-path assertion. Purely additive — the validator and DTO under test are not modified.

**Structure:**

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty;
using Anela.Heblo.Application.Features.Catalog.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Validators;

public class CreateManufactureDifficultyRequestValidatorTests
{
    private readonly CreateManufactureDifficultyRequestValidator _validator = new();

    private static CreateManufactureDifficultyRequest ValidRequest() => new()
    {
        ProductCode = "PROD001",
        DifficultyValue = 1,
        ValidFrom = null,
        ValidTo = null
    };

    // --- ProductCode (FR-2) ---
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ProductCode_NullOrEmpty_HasCorrectErrorMessage(string? productCode) { /* ... */ }

    [Fact]
    public void ProductCode_TypicalValue_PassesValidation() { /* ... */ }

    [Fact]
    public void ProductCode_Exactly50Characters_PassesValidation() { /* ... */ }

    [Fact]
    public void ProductCode_Exactly51Characters_HasCorrectErrorMessage() { /* ... */ }

    // --- DifficultyValue (FR-3) ---
    [Fact]
    public void DifficultyValue_Negative_HasCorrectErrorMessage() { /* ... */ }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DifficultyValue_NonNegative_PassesValidation(int value) { /* ... */ }

    // --- ValidFrom / ValidTo cross-field (FR-4) ---
    [Fact]
    public void ValidFromValidTo_FromBeforeTo_PassesValidation() { /* ... */ }

    [Fact]
    public void ValidFromValidTo_Equal_HasCorrectErrorMessageOnBothFields() { /* ... */ }

    [Fact]
    public void ValidFromValidTo_FromAfterTo_HasCorrectErrorMessageOnBothFields() { /* ... */ }

    [Fact]
    public void ValidFromValidTo_OnlyFromSet_PassesValidation() { /* ... */ }

    [Fact]
    public void ValidFromValidTo_OnlyToSet_PassesValidation() { /* ... */ }

    [Fact]
    public void ValidFromValidTo_BothNull_PassesValidation() { /* ... */ }

    // --- Whole request (FR-5) ---
    [Fact]
    public void ValidRequest_PassesAllValidation() { /* ... */ }
}
```

**Helper contract:**
- `ValidRequest()` — private static factory returning a baseline valid `CreateManufactureDifficultyRequest` (`ProductCode = "PROD001"`, `DifficultyValue = 1`, `ValidFrom`/`ValidTo` both `null`). Individual test methods clone/mutate the object returned by this call (each call returns a fresh instance — no shared mutable state across tests).
- `_validator` — single `CreateManufactureDifficultyRequestValidator` instance held in a `readonly` field, constructed once per test-class instantiation (xUnit creates a new class instance per test case, so no cross-test state leakage).

**Interaction pattern (per test):**
1. Build request via `ValidRequest()`, mutate the field(s) under test.
2. Call `_validator.TestValidate(request)` → `TestValidationResult<CreateManufactureDifficultyRequest>`.
3. Assert via `ShouldHaveValidationErrorFor(x => x.Field).WithErrorMessage("...")` / `ShouldNotHaveValidationErrorFor(x => x.Field)`, or for FR-5 via `Assert.True(result.IsValid)` and `Assert.Empty(result.Errors)`.

**Date literals:** Use fixed `DateTime` literals (e.g. `new DateTime(2026, 1, 1)`, `new DateTime(2026, 1, 2)`) for all `ValidFrom`/`ValidTo` cases — no `DateTime.Now`/relative dates, to keep the suite deterministic.

**Out of scope for this component:** no mocks, no DI, no database, no HTTP pipeline, no data-annotation (`[Required]`/`[Range]`) testing — those are model-binding concerns handled elsewhere.

## Data Schemas

No schema changes. Tests construct plain in-memory instances of the existing, unmodified DTO:

```csharp
public class CreateManufactureDifficultyRequest : IRequest<CreateManufactureDifficultyResponse>
{
    public string ProductCode { get; set; } = null!;
    public int DifficultyValue { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}
```

**Validation rules under test** (from `CreateManufactureDifficultyRequestValidator`, unmodified):

| Field | Rule | Error message |
|---|---|---|
| `ProductCode` | `NotEmpty()` | `"Product code is required"` |
| `ProductCode` | `MaximumLength(50)` | `"Product code cannot exceed 50 characters"` |
| `DifficultyValue` | `GreaterThanOrEqualTo(0)` | `"Difficulty value must be non-negative"` |
| `ValidFrom` | `LessThan(x => x.ValidTo)`, guarded by `.When(x => x.ValidFrom.HasValue && x.ValidTo.HasValue)` | `"ValidFrom must be earlier than ValidTo"` |
| `ValidTo` | `GreaterThan(x => x.ValidFrom)`, guarded by `.When(x => x.ValidFrom.HasValue && x.ValidTo.HasValue)` | `"ValidTo must be later than ValidFrom"` |

No request/response wire shape changes, no database schema changes, no event payloads — this task produces no new or altered API surface.
