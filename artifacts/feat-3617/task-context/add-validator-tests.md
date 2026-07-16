### task: add-validator-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/CreateManufactureDifficultyRequestValidatorTests.cs`
- No other files are created or modified.

This single task builds the test file incrementally (one FR block per step) so each step is independently verifiable, then runs the full class once at the end.

- [ ] **Step 1: Create the file with skeleton, `using`s, constructor, and `ValidRequest()` helper**

  Create `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/CreateManufactureDifficultyRequestValidatorTests.cs` with this content:

  ```csharp
  using Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty;
  using Anela.Heblo.Application.Features.Catalog.Validators;
  using FluentValidation.TestHelper;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.Catalog.Validators;

  public class CreateManufactureDifficultyRequestValidatorTests
  {
      private readonly CreateManufactureDifficultyRequestValidator _validator;

      public CreateManufactureDifficultyRequestValidatorTests()
      {
          _validator = new CreateManufactureDifficultyRequestValidator();
      }

      private static CreateManufactureDifficultyRequest ValidRequest() => new()
      {
          ProductCode = "PROD001",
          DifficultyValue = 1,
          ValidFrom = null,
          ValidTo = null
      };
  }
  ```

- [ ] **Step 2: Build to verify the skeleton compiles**

  ```bash
  dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
  ```

  Expected: `Build succeeded.` with 0 errors. An empty test class with no `[Fact]`/`[Theory]` methods compiles fine — this step only confirms the `using`s, namespace, and helper resolve correctly (i.e. `FluentValidation.TestHelper` and the two `Anela.Heblo.Application...` namespaces are reachable from this project, and `CreateManufactureDifficultyRequestValidator`/`CreateManufactureDifficultyRequest` are the correct types).

- [ ] **Step 3: Add `ProductCode` tests (FR-2) inside the class, after the `ValidRequest()` helper**

  ```csharp
      // --- ProductCode (FR-2) ---

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
      public void ProductCode_TypicalValue_PassesValidation()
      {
          var request = ValidRequest();
          request.ProductCode = "PROD001";

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

      [Fact]
      public void ProductCode_Exactly51Characters_HasCorrectErrorMessage()
      {
          var request = ValidRequest();
          request.ProductCode = new string('A', 51);

          var result = _validator.TestValidate(request);

          result.ShouldHaveValidationErrorFor(x => x.ProductCode)
              .WithErrorMessage("Product code cannot exceed 50 characters");
      }
  ```

- [ ] **Step 4: Run the `ProductCode` tests and verify they pass**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CreateManufactureDifficultyRequestValidatorTests.ProductCode"
  ```

  Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0` (2 cases from the `[Theory]` + 3 `[Fact]`s = 5 total test cases). If `ProductCode_NullOrEmpty_HasCorrectErrorMessage` fails on the `WithErrorMessage` assertion, double-check the exact string against the validator source (`"Product code is required"`) — do not change the validator to match a wrong assumption.

- [ ] **Step 5: Add `DifficultyValue` tests (FR-3), after the `ProductCode` block**

  ```csharp
      // --- DifficultyValue (FR-3) ---

      [Fact]
      public void DifficultyValue_Negative_HasCorrectErrorMessage()
      {
          var request = ValidRequest();
          request.DifficultyValue = -1;

          var result = _validator.TestValidate(request);

          result.ShouldHaveValidationErrorFor(x => x.DifficultyValue)
              .WithErrorMessage("Difficulty value must be non-negative");
      }

      [Theory]
      [InlineData(0)]
      [InlineData(1)]
      public void DifficultyValue_NonNegative_PassesValidation(int value)
      {
          var request = ValidRequest();
          request.DifficultyValue = value;

          var result = _validator.TestValidate(request);

          result.ShouldNotHaveValidationErrorFor(x => x.DifficultyValue);
      }
  ```

- [ ] **Step 6: Run the `DifficultyValue` tests and verify they pass**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CreateManufactureDifficultyRequestValidatorTests.DifficultyValue"
  ```

  Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0` (1 `[Fact]` + 2 cases from the `[Theory]` = 3 total).

- [ ] **Step 7: Add `ValidFrom`/`ValidTo` cross-field tests (FR-4), after the `DifficultyValue` block**

  Use fixed `DateTime` literals throughout (never `DateTime.Now`) to keep the suite deterministic:

  ```csharp
      // --- ValidFrom / ValidTo cross-field (FR-4) ---

      [Fact]
      public void ValidFromValidTo_FromBeforeTo_PassesValidation()
      {
          var request = ValidRequest();
          request.ValidFrom = new DateTime(2026, 1, 1);
          request.ValidTo = new DateTime(2026, 1, 2);

          var result = _validator.TestValidate(request);

          result.ShouldNotHaveValidationErrorFor(x => x.ValidFrom);
          result.ShouldNotHaveValidationErrorFor(x => x.ValidTo);
      }

      [Fact]
      public void ValidFromValidTo_Equal_HasCorrectErrorMessageOnBothFields()
      {
          var request = ValidRequest();
          var same = new DateTime(2026, 1, 1);
          request.ValidFrom = same;
          request.ValidTo = same;

          var result = _validator.TestValidate(request);

          result.ShouldHaveValidationErrorFor(x => x.ValidFrom)
              .WithErrorMessage("ValidFrom must be earlier than ValidTo");
          result.ShouldHaveValidationErrorFor(x => x.ValidTo)
              .WithErrorMessage("ValidTo must be later than ValidFrom");
      }

      [Fact]
      public void ValidFromValidTo_FromAfterTo_HasCorrectErrorMessageOnBothFields()
      {
          var request = ValidRequest();
          request.ValidFrom = new DateTime(2026, 1, 2);
          request.ValidTo = new DateTime(2026, 1, 1);

          var result = _validator.TestValidate(request);

          result.ShouldHaveValidationErrorFor(x => x.ValidFrom)
              .WithErrorMessage("ValidFrom must be earlier than ValidTo");
          result.ShouldHaveValidationErrorFor(x => x.ValidTo)
              .WithErrorMessage("ValidTo must be later than ValidFrom");
      }

      [Fact]
      public void ValidFromValidTo_OnlyFromSet_PassesValidation()
      {
          var request = ValidRequest();
          request.ValidFrom = new DateTime(2026, 1, 1);
          request.ValidTo = null;

          var result = _validator.TestValidate(request);

          result.ShouldNotHaveValidationErrorFor(x => x.ValidFrom);
          result.ShouldNotHaveValidationErrorFor(x => x.ValidTo);
      }

      [Fact]
      public void ValidFromValidTo_OnlyToSet_PassesValidation()
      {
          var request = ValidRequest();
          request.ValidFrom = null;
          request.ValidTo = new DateTime(2026, 1, 1);

          var result = _validator.TestValidate(request);

          result.ShouldNotHaveValidationErrorFor(x => x.ValidFrom);
          result.ShouldNotHaveValidationErrorFor(x => x.ValidTo);
      }

      [Fact]
      public void ValidFromValidTo_BothNull_PassesValidation()
      {
          var request = ValidRequest();
          request.ValidFrom = null;
          request.ValidTo = null;

          var result = _validator.TestValidate(request);

          result.ShouldNotHaveValidationErrorFor(x => x.ValidFrom);
          result.ShouldNotHaveValidationErrorFor(x => x.ValidTo);
      }
  ```

- [ ] **Step 8: Run the cross-field date tests and verify they pass**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CreateManufactureDifficultyRequestValidatorTests.ValidFromValidTo"
  ```

  Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0`. This step is the most likely place for a mismatch between spec assumptions and actual behavior to surface (e.g. if `ShouldNotHaveValidationErrorFor` unexpectedly fails for the equal-dates case) — if any assertion fails, re-read the validator's `.When(...)` guard and rule chain rather than adjusting the validator; the spec and arch-review both confirm this exact behavior is intentional and already correct.

- [ ] **Step 9: Add the whole-request happy-path test (FR-5), after the `ValidFromValidTo` block**

  ```csharp
      // --- Whole request (FR-5) ---

      [Fact]
      public void ValidRequest_PassesAllValidation()
      {
          var request = new CreateManufactureDifficultyRequest
          {
              ProductCode = "PROD001",
              DifficultyValue = 1,
              ValidFrom = new DateTime(2026, 1, 1),
              ValidTo = new DateTime(2026, 1, 2)
          };

          var result = _validator.TestValidate(request);

          Assert.True(result.IsValid);
          Assert.Empty(result.Errors);
      }
  ```

  Close the class with `}` after this method if not already present.

- [ ] **Step 10: Run the full test class and verify all 14 test methods (18 test cases) pass**

  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CreateManufactureDifficultyRequestValidatorTests"
  ```

  Expected: `Passed! - Failed: 0, Passed: 18, Skipped: 0`.

  Case count reconciliation: `ProductCode_NullOrEmpty_HasCorrectErrorMessage` (2 cases) + `ProductCode_TypicalValue_PassesValidation` (1) + `ProductCode_Exactly50Characters_PassesValidation` (1) + `ProductCode_Exactly51Characters_HasCorrectErrorMessage` (1) + `DifficultyValue_Negative_HasCorrectErrorMessage` (1) + `DifficultyValue_NonNegative_PassesValidation` (2 cases) + `ValidFromValidTo_FromBeforeTo_PassesValidation` (1) + `ValidFromValidTo_Equal_HasCorrectErrorMessageOnBothFields` (1) + `ValidFromValidTo_FromAfterTo_HasCorrectErrorMessageOnBothFields` (1) + `ValidFromValidTo_OnlyFromSet_PassesValidation` (1) + `ValidFromValidTo_OnlyToSet_PassesValidation` (1) + `ValidFromValidTo_BothNull_PassesValidation` (1) + `ValidRequest_PassesAllValidation` (1) = 18 test cases across 14 test methods.

- [ ] **Step 11: Run `dotnet format` and the full backend build to confirm no regressions**

  ```bash
  dotnet format backend/Anela.Heblo.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/CreateManufactureDifficultyRequestValidatorTests.cs
  dotnet build Anela.Heblo.sln
  ```

  If `dotnet format` reports formatting differences, run it without `--verify-no-changes` to apply them, then re-run Step 10 to confirm tests still pass:

  ```bash
  dotnet format backend/Anela.Heblo.sln --include backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/CreateManufactureDifficultyRequestValidatorTests.cs
  ```

  Expected: `Build succeeded.` with 0 errors, 0 warnings introduced by the new file.

- [ ] **Step 12: Commit**

  ```bash
  git add backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/CreateManufactureDifficultyRequestValidatorTests.cs
  git commit -m "test(catalog): add unit tests for CreateManufactureDifficultyRequestValidator

Covers ProductCode NotEmpty/MaximumLength(50), DifficultyValue
GreaterThanOrEqualTo(0), and the ValidFrom/ValidTo cross-field date
range invariant (including the single-sided-null pass-through
behavior, documented here as intentional). No production code
changes; closes the 0% coverage gap flagged by the weekly
coverage-gap routine (CI run #28968007617)."
  ```

  Expected: commit created with exactly one file (`CreateManufactureDifficultyRequestValidatorTests.cs`) in the diff.

---

## Self-Review

**Spec coverage check** (FR-1 through FR-5, NFR-1 through NFR-3):
- FR-1 (file placement, namespace, xUnit + `FluentValidation.TestHelper`, constructor-instantiated validator, `ValidRequest()` helper) → satisfied by Step 1 (skeleton) and the overall file structure.
- FR-2 (`ProductCode` null/empty/typical/50/51 chars) → Step 3/4, all 5 acceptance criteria present as test cases (whitespace-only case explicitly noted as out-of-scope-but-optional in the spec; omitted here per spec's own "not required" language — no gap).
- FR-3 (`DifficultyValue` -1/0/1) → Step 5/6, all 3 acceptance criteria present.
- FR-4 (six date-range scenarios: `<`, `==`, `>`, only-from, only-to, both-null) → Step 7/8, all 6 acceptance criteria present as discrete `[Fact]`s per the arch-review's Decision 2.
- FR-5 (whole-request happy path, `IsValid`/`Errors` assertions) → Step 9, matches `SubmitStockTakingRequestValidatorTests.ValidRequest_PassesAllValidation` style exactly.
- NFR-1 (performance) → no action needed, satisfied by construction (pure in-memory FluentValidation calls).
- NFR-2 (security) → no action needed, no new attack surface.
- NFR-3 (naming convention, `[Theory]` vs `[Fact]` split) → all method names follow `MethodOrField_Scenario_ExpectedOutcome`; `[Theory]` used only for `ProductCode` null/empty and `DifficultyValue` 0/1 (single-field boundary cases), `[Fact]` used for all cross-field date scenarios, matching NFR-3 exactly.

**Placeholder scan:** No "TBD"/"TODO"/"implement later" strings anywhere in this plan. Every step shows complete, real C# code or an exact, runnable `dotnet` command with an expected output string. No step says "add appropriate tests" without showing the actual test code.

**Type/name consistency check:** `CreateManufactureDifficultyRequestValidator`, `CreateManufactureDifficultyRequest`, `ProductCode` (string), `DifficultyValue` (int), `ValidFrom`/`ValidTo` (`DateTime?`) are used identically across every step and match the verbatim source read directly from `CreateManufactureDifficultyRequestValidator.cs` and `CreateManufactureDifficultyRequest.cs` quoted in the Context section above. Error message strings (`"Product code is required"`, `"Product code cannot exceed 50 characters"`, `"Difficulty value must be non-negative"`, `"ValidFrom must be earlier than ValidTo"`, `"ValidTo must be later than ValidFrom"`) are copied verbatim from the validator source, not re-typed from memory in a way that could drift.

**Out-of-scope guard:** No step touches `CreateManufactureDifficultyRequestValidator.cs`, `CreateManufactureDifficultyRequest.cs`, `CreateManufactureDifficultyResponse.cs`, `CreateManufactureDifficultyHandler.cs`, or `Anela.Heblo.Tests.csproj` — consistent with the spec's explicit "Out of Scope" and "Dependencies" sections.
