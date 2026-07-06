### task: lines-collection-and-wiring-tests

**Goal:** Extend `UpdatePurchaseOrderRequestValidatorTests` (class 1, created by the `scaffold-and-date-boundary-tests` task) with the `Lines` collection tests — `NotNull`/`NotEmpty` (FR-6), the 100-item cap (FR-5) — plus one `RuleForEach` child-validator wiring-confirmation test (part of FR-7, parent-level only; the full `Quantity`/`UnitPrice` boundary coverage is added by the `line-item-standalone-validator-tests` task).

**Precondition:** `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs` already exists with the `UpdatePurchaseOrderRequestValidatorTests` class ending in an `ExpectedDeliveryDate_Null_PassesValidation` test method immediately followed by the class's closing brace (produced by the prior task). If the file does not yet exist or does not match, create/restore it first using the exact content shown in the `scaffold-and-date-boundary-tests` task's Step 1 before proceeding.

**Files:**
- Edit: `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs`

- [ ] **Step 1: Read the file**

Read `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs` to confirm its current end-of-class content matches the precondition above.

- [ ] **Step 2: Insert the Lines tests before the class's closing brace**

Using the Edit tool, replace this exact text:

```csharp
    [Fact]
    public void ExpectedDeliveryDate_Null_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = null;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }
}
```

with:

```csharp
    [Fact]
    public void ExpectedDeliveryDate_Null_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = null;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    // --------------- Lines: null / empty / cap ---------------

    [Fact]
    public void Lines_Null_FailsValidation()
    {
        var request = ValidRequest();
        request.Lines = null!;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Lines)
            .WithErrorMessage("Order lines are required");
    }

    [Fact]
    public void Lines_Empty_FailsValidation()
    {
        var request = ValidRequest();
        request.Lines = new List<UpdatePurchaseOrderLineRequest>();

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Lines)
            .WithErrorMessage("At least one order line is required");
    }

    [Fact]
    public void Lines_Exactly100Items_PassesValidation()
    {
        var request = ValidRequest();
        request.Lines = Enumerable.Range(1, 100)
            .Select(i => new UpdatePurchaseOrderLineRequest
            {
                MaterialId = $"MAT-{i}",
                Quantity = 1m,
                UnitPrice = 1m
            })
            .ToList();

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void Lines_101Items_FailsValidation()
    {
        var request = ValidRequest();
        request.Lines = Enumerable.Range(1, 101)
            .Select(i => new UpdatePurchaseOrderLineRequest
            {
                MaterialId = $"MAT-{i}",
                Quantity = 1m,
                UnitPrice = 1m
            })
            .ToList();

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Lines)
            .WithErrorMessage("A purchase order cannot have more than 100 line items");
    }

    // --------------- Lines[0]: RuleForEach wiring confirmation ---------------

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

- [ ] **Step 3: Build the test project**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Run the new tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderRequestValidatorTests"
```
Expected: all 13 tests in `UpdatePurchaseOrderRequestValidatorTests` pass (the 8 from the previous task plus `Lines_Null_FailsValidation`, `Lines_Empty_FailsValidation`, `Lines_Exactly100Items_PassesValidation`, `Lines_101Items_FailsValidation`, `Lines_ChildValidatorWiring_InvalidLineQuantity_FailsValidationOnParent`), 0 failed.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs
git commit -m "Add Lines collection and RuleForEach wiring tests for UpdatePurchaseOrderRequestValidator"
```

---

