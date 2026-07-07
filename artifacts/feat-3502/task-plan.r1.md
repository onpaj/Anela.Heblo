# Implementation Plan: Fix TargetAmount validation message + add test coverage for SubmitStockTakingRequestValidator

## Goal

`SubmitStockTakingRequestValidator` (backend, FluentValidation) has a validation rule `LessThan(100000)` on `TargetAmount` whose error message incorrectly says `"Target amount must be less than 1,000"`. The rule's numeric bound (`100000`) is correct and must not change — only the message text is wrong. The validator also currently has 0% test coverage. This plan fixes the message string and adds a full unit test suite covering every validation rule in the class (`ProductCode`, `TargetAmount`).

## Scope

Single subsystem, single validator class, backend-only. No DI, no migrations, no new packages (`FluentValidation.TestHelper` is already referenced by the test project, confirmed by the existing sibling test files below). This is intentionally a single task per the pipeline's task-extraction rule.

## Verified source files (read directly from the worktree before writing this plan)

**`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequestValidator.cs`** (current, 21 lines):

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking;

public class SubmitStockTakingRequestValidator : AbstractValidator<SubmitStockTakingRequest>
{
    public SubmitStockTakingRequestValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .WithMessage("Product code is required")
            .MaximumLength(50)
            .WithMessage("Product code cannot exceed 50 characters");

        RuleFor(x => x.TargetAmount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Target amount must be greater than or equal to 0")
            .LessThan(100000)
            .WithMessage("Target amount must be less than 1,000");
    }
}
```

Line 19 is the bug: `.WithMessage("Target amount must be less than 1,000")` must become `.WithMessage("Target amount must be less than 100,000")`. The `.LessThan(100000)` call on line 18 is correct and must NOT change.

**`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequest.cs`** (DTO, unmodified by this plan, shown for reference only):

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking;

public class SubmitStockTakingRequest : IRequest<SubmitStockTakingResponse>
{
    [Required(ErrorMessage = "Product code is required")]
    [StringLength(50, ErrorMessage = "Product code cannot exceed 50 characters")]
    public string ProductCode { get; set; } = null!;

    [Required(ErrorMessage = "Target amount is required")]
    [Range(0, 999999.99, ErrorMessage = "Target amount must be between 0 and 999999.99")]
    public decimal TargetAmount { get; set; }

    public bool SoftStockTaking { get; set; } = true;
}
```

`TargetAmount` is `decimal`. Note the `[Range]` DataAnnotation on the DTO is not what's under test here — `SubmitStockTakingRequestValidator` is a **FluentValidation** validator invoked directly via `TestValidate()`, which does not evaluate DataAnnotations at all. Only the FluentValidation rules matter for these tests.

**Sibling test file used as the style template** — `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/UpdateProductCompositionOrderRequestValidatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Validators;

public class UpdateProductCompositionOrderRequestValidatorTests
{
    private readonly UpdateProductCompositionOrderRequestValidator _validator;

    public UpdateProductCompositionOrderRequestValidatorTests()
    {
        _validator = new UpdateProductCompositionOrderRequestValidator();
    }
    // ... uses _validator.TestValidate(request), result.ShouldHaveValidationErrorFor(x => x.Prop).WithErrorMessage("..."),
    // result.ShouldNotHaveValidationErrorFor(x => x.Prop), result.ShouldNotHaveAnyValidationErrors()
}
```

This confirms: namespace `Anela.Heblo.Tests.Features.Catalog.Validators`, `using FluentValidation.TestHelper;`, `using Xunit;`, constructor instantiates the validator directly (no DI/mocks needed since `SubmitStockTakingRequestValidator` has a parameterless constructor).

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequestValidator.cs` | Modify (1 line) | Fix the `TargetAmount` upper-bound error message text |
| `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/SubmitStockTakingRequestValidatorTests.cs` | Create | Full unit test coverage for `SubmitStockTakingRequestValidator` (both `ProductCode` and `TargetAmount` rules) |

The test file goes in the centralized `Features/Catalog/Validators/` test folder (matching `GetCatalogDetailRequestValidatorTests.cs` and `UpdateProductCompositionOrderRequestValidatorTests.cs`), NOT colocated with the validator source under `UseCases/SubmitStockTaking/`.

---

### task: fix-target-amount-message-and-add-validator-tests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequestValidator.cs` (line 19)
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/SubmitStockTakingRequestValidatorTests.cs`

Steps:

- [ ] **Step 1: Write the new test file with all test cases (will fail to compile/pass until Step 3's fix is applied for two specific cases)**

  Create `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/SubmitStockTakingRequestValidatorTests.cs` with this exact content:

  ```csharp
  using Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking;
  using FluentValidation.TestHelper;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.Catalog.Validators;

  public class SubmitStockTakingRequestValidatorTests
  {
      private readonly SubmitStockTakingRequestValidator _validator;

      public SubmitStockTakingRequestValidatorTests()
      {
          _validator = new SubmitStockTakingRequestValidator();
      }

      private static SubmitStockTakingRequest ValidRequest() => new()
      {
          ProductCode = "ABC123",
          TargetAmount = 500
      };

      [Theory]
      [InlineData(500)]
      [InlineData(99999)]
      [InlineData(0)]
      [InlineData(1)]
      public void TargetAmount_ValidValues_PassesValidation(decimal targetAmount)
      {
          var request = ValidRequest();
          request.TargetAmount = targetAmount;

          var result = _validator.TestValidate(request);

          result.ShouldNotHaveValidationErrorFor(x => x.TargetAmount);
      }

      [Theory]
      [InlineData(100001)]
      [InlineData(100000)]
      [InlineData(-1)]
      public void TargetAmount_InvalidValues_FailsValidation(decimal targetAmount)
      {
          var request = ValidRequest();
          request.TargetAmount = targetAmount;

          var result = _validator.TestValidate(request);

          result.ShouldHaveValidationErrorFor(x => x.TargetAmount);
      }

      [Fact]
      public void TargetAmount_ExceedsUpperBound_HasCorrectErrorMessage()
      {
          var request = ValidRequest();
          request.TargetAmount = 100001;

          var result = _validator.TestValidate(request);

          result.ShouldHaveValidationErrorFor(x => x.TargetAmount)
              .WithErrorMessage("Target amount must be less than 100,000");
      }

      [Fact]
      public void TargetAmount_AtUpperBoundExclusive_FailsValidation()
      {
          // 100000 itself must fail: the rule is LessThan(100000), i.e. exclusive upper bound.
          var request = ValidRequest();
          request.TargetAmount = 100000;

          var result = _validator.TestValidate(request);

          result.ShouldHaveValidationErrorFor(x => x.TargetAmount)
              .WithErrorMessage("Target amount must be less than 100,000");
      }

      [Fact]
      public void TargetAmount_JustBelowUpperBound_PassesValidation()
      {
          var request = ValidRequest();
          request.TargetAmount = 99999;

          var result = _validator.TestValidate(request);

          result.ShouldNotHaveValidationErrorFor(x => x.TargetAmount);
      }

      [Fact]
      public void TargetAmount_Negative_HasCorrectErrorMessage()
      {
          var request = ValidRequest();
          request.TargetAmount = -1;

          var result = _validator.TestValidate(request);

          result.ShouldHaveValidationErrorFor(x => x.TargetAmount)
              .WithErrorMessage("Target amount must be greater than or equal to 0");
      }

      [Fact]
      public void TargetAmount_Zero_PassesValidation()
      {
          // Lower bound is inclusive (GreaterThanOrEqualTo).
          var request = ValidRequest();
          request.TargetAmount = 0;

          var result = _validator.TestValidate(request);

          result.ShouldNotHaveValidationErrorFor(x => x.TargetAmount);
      }

      [Fact]
      public void TargetAmount_One_PassesValidation()
      {
          var request = ValidRequest();
          request.TargetAmount = 1;

          var result = _validator.TestValidate(request);

          result.ShouldNotHaveValidationErrorFor(x => x.TargetAmount);
      }

      [Fact]
      public void ProductCode_TypicalValue_PassesValidation()
      {
          var request = ValidRequest();
          request.ProductCode = "ABC123";

          var result = _validator.TestValidate(request);

          result.ShouldNotHaveValidationErrorFor(x => x.ProductCode);
      }

      [Fact]
      public void ProductCode_Exactly50Characters_PassesValidation()
      {
          var request = ValidRequest();
          request.ProductCode = new string('A', 50);

          var result = _validator.TestValidate(request);

          result.ShouldNotHaveValidationErrorFor(x => x.ProductCode);
      }

      [Theory]
      [InlineData(null)]
      [InlineData("")]
      public void ProductCode_NullOrEmpty_HasCorrectErrorMessage(string? productCode)
      {
          var request = ValidRequest();
          request.ProductCode = productCode!;

          var result = _validator.TestValidate(request);

          result.ShouldHaveValidationErrorFor(x => x.ProductCode)
              .WithErrorMessage("Product code is required");
      }

      [Fact]
      public void ProductCode_Exceeds50Characters_HasCorrectErrorMessage()
      {
          var request = ValidRequest();
          request.ProductCode = new string('A', 51);

          var result = _validator.TestValidate(request);

          result.ShouldHaveValidationErrorFor(x => x.ProductCode)
              .WithErrorMessage("Product code cannot exceed 50 characters");
      }

      [Fact]
      public void ValidRequest_PassesAllValidation()
      {
          var request = new SubmitStockTakingRequest
          {
              ProductCode = "ABC123",
              TargetAmount = 500
          };

          var result = _validator.TestValidate(request);

          Assert.True(result.IsValid);
          Assert.Empty(result.Errors);
      }
  }
  ```

- [ ] **Step 2: Run the new tests and confirm exactly two failures (the message-text assertions), everything else passes**

  From the repo root:

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitStockTakingRequestValidatorTests"
  ```

  Expected: `TargetAmount_ExceedsUpperBound_HasCorrectErrorMessage` and `TargetAmount_AtUpperBoundExclusive_FailsValidation` FAIL (actual message is still `"Target amount must be less than 1,000"`, expected `"Target amount must be less than 100,000"`). All other tests in the class PASS. Total: 2 failed, rest passed.

- [ ] **Step 3: Fix the misleading error message in the validator**

  In `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequestValidator.cs`, change line 19 from:

  ```csharp
              .WithMessage("Target amount must be less than 1,000");
  ```

  to:

  ```csharp
              .WithMessage("Target amount must be less than 100,000");
  ```

  Do not change line 18 (`.LessThan(100000)`) — the numeric bound is correct and out of scope for this fix. The full corrected `RuleFor(x => x.TargetAmount)` block should read:

  ```csharp
          RuleFor(x => x.TargetAmount)
              .GreaterThanOrEqualTo(0)
              .WithMessage("Target amount must be greater than or equal to 0")
              .LessThan(100000)
              .WithMessage("Target amount must be less than 100,000");
  ```

- [ ] **Step 4: Run the full test class again and confirm all tests pass**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitStockTakingRequestValidatorTests"
  ```

  Expected: all tests in `SubmitStockTakingRequestValidatorTests` pass, 0 failed.

- [ ] **Step 5: Run the full backend test suite to confirm no regressions elsewhere**

  ```bash
  cd backend
  dotnet build
  dotnet test
  ```

  Expected: build succeeds with no errors; full test run passes (no pre-existing test asserted the old, incorrect `"Target amount must be less than 1,000"` message — this validator had 0% prior coverage per the spec, so no other test should reference that string).

- [ ] **Step 6: Format the code**

  ```bash
  cd backend
  dotnet format
  ```

  Expected: no unformatted files reported, or auto-fixes applied cleanly (only whitespace/style, no semantic changes).

- [ ] **Step 7: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/SubmitStockTaking/SubmitStockTakingRequestValidator.cs
  git add backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/SubmitStockTakingRequestValidatorTests.cs
  git commit -m "Fix TargetAmount validation message and add SubmitStockTakingRequestValidator test coverage"
  ```
