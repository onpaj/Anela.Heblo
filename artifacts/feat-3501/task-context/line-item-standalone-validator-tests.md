### task: line-item-standalone-validator-tests

**Goal:** Add the second public test class, `UpdatePurchaseOrderLineRequestValidatorTests`, to the same file, testing `UpdatePurchaseOrderLineRequestValidator` standalone against a bare `UpdatePurchaseOrderLineRequest` — full `Quantity` (FR-7) and `UnitPrice` (FR-8) boundary coverage, plus a full-valid-line smoke test.

**Precondition:** `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs` already exists and ends with the `Lines_ChildValidatorWiring_InvalidLineQuantity_FailsValidationOnParent` test method immediately followed by the closing brace of `UpdatePurchaseOrderRequestValidatorTests` (produced by the `lines-collection-and-wiring-tests` task), and that closing brace is the last line in the file. If the file does not match this shape, first apply the `scaffold-and-date-boundary-tests` and `lines-collection-and-wiring-tests` tasks' steps in order before proceeding.

**Files:**
- Edit: `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs`

- [ ] **Step 1: Read the file**

Read `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs` to confirm the current end-of-file content matches the precondition above.

- [ ] **Step 2: Append the second test class**

Using the Edit tool, replace this exact text (the last method and closing brace of class 1):

```csharp
    [Fact]
    public void Lines_ChildValidatorWiring_InvalidLineQuantity_FailsValidationOnParent()
    {
        var request = ValidRequest();
        request.Lines[0].Quantity = 0m;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor("Lines[0].Quantity")
            .WithErrorMessage("Quantity must be greater than 0");
    }
}
```

with:

```csharp
    [Fact]
    public void Lines_ChildValidatorWiring_InvalidLineQuantity_FailsValidationOnParent()
    {
        var request = ValidRequest();
        request.Lines[0].Quantity = 0m;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor("Lines[0].Quantity")
            .WithErrorMessage("Quantity must be greater than 0");
    }
}

public class UpdatePurchaseOrderLineRequestValidatorTests
{
    private readonly UpdatePurchaseOrderLineRequestValidator _validator = new();

    private static UpdatePurchaseOrderLineRequest ValidLine() => new()
    {
        MaterialId = "MAT-001",
        Quantity = 1m,
        UnitPrice = 1m
    };

    // --------------- Quantity ---------------

    [Fact]
    public void Quantity_Zero_FailsValidation()
    {
        var line = ValidLine();
        line.Quantity = 0m;

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.Quantity)
            .WithErrorMessage("Quantity must be greater than 0");
    }

    [Fact]
    public void Quantity_Negative_FailsValidation()
    {
        var line = ValidLine();
        line.Quantity = -1m;

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.Quantity)
            .WithErrorMessage("Quantity must be greater than 0");
    }

    [Fact]
    public void Quantity_SmallestValidIncrement_PassesValidation()
    {
        var line = ValidLine();
        line.Quantity = 0.01m;

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.Quantity);
    }

    [Fact]
    public void Quantity_AtMaximum_PassesValidation()
    {
        var line = ValidLine();
        line.Quantity = 999999.99m;

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.Quantity);
    }

    [Fact]
    public void Quantity_ExceedsMaximum_FailsValidation()
    {
        var line = ValidLine();
        line.Quantity = 1000000.00m;

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.Quantity)
            .WithErrorMessage("Quantity cannot exceed 999999.99");
    }

    // --------------- UnitPrice ---------------

    [Fact]
    public void UnitPrice_Zero_PassesValidation()
    {
        var line = ValidLine();
        line.UnitPrice = 0m;

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.UnitPrice);
    }

    [Fact]
    public void UnitPrice_Negative_FailsValidation()
    {
        var line = ValidLine();
        line.UnitPrice = -0.01m;

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.UnitPrice)
            .WithErrorMessage("Unit price cannot be negative");
    }

    [Fact]
    public void UnitPrice_AtMaximum_PassesValidation()
    {
        var line = ValidLine();
        line.UnitPrice = 999999.99m;

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.UnitPrice);
    }

    [Fact]
    public void UnitPrice_ExceedsMaximum_FailsValidation()
    {
        var line = ValidLine();
        line.UnitPrice = 1000000.00m;

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.UnitPrice)
            .WithErrorMessage("Unit price cannot exceed 999999.99");
    }

    // --------------- Full valid line ---------------

    [Fact]
    public void ValidLine_PassesAllValidation()
    {
        var line = ValidLine();

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
```

- [ ] **Step 3: Build the test project**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Run the full new file's test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderRequestValidatorTests|FullyQualifiedName~UpdatePurchaseOrderLineRequestValidatorTests"
```
Expected: all 23 tests pass — 13 in `UpdatePurchaseOrderRequestValidatorTests` (from the previous two tasks) plus 10 in `UpdatePurchaseOrderLineRequestValidatorTests` (`Quantity_Zero_FailsValidation`, `Quantity_Negative_FailsValidation`, `Quantity_SmallestValidIncrement_PassesValidation`, `Quantity_AtMaximum_PassesValidation`, `Quantity_ExceedsMaximum_FailsValidation`, `UnitPrice_Zero_PassesValidation`, `UnitPrice_Negative_FailsValidation`, `UnitPrice_AtMaximum_PassesValidation`, `UnitPrice_ExceedsMaximum_FailsValidation`, `ValidLine_PassesAllValidation`), 0 failed.

- [ ] **Step 5: Run the full backend test suite to confirm no regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: all tests pass (no pre-existing failures introduced by this change).

- [ ] **Step 6: Format check**

```bash
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs
```
If this reports formatting issues, run without `--verify-no-changes` to apply fixes, then re-run Step 5.

- [ ] **Step 7: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs
git commit -m "Add standalone Quantity/UnitPrice boundary tests for UpdatePurchaseOrderLineRequestValidator"
```
