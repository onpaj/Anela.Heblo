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
