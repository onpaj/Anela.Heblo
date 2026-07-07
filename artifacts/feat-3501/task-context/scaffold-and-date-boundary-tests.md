### task: scaffold-and-date-boundary-tests

**Goal:** Create the new test file with class 1's scaffold (validator instance, `ValidRequest()` factory, date helpers) plus every `ExpectedDeliveryDate` test (FR-1 baseline, FR-2 future bound, FR-3 past bound with the clock-skew fix above, FR-4 null passthrough).

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs`

- [ ] **Step 1: Create the test file with the scaffold and date-boundary tests**

Use the Write tool to create `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs` with exactly this content:

```csharp
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class UpdatePurchaseOrderRequestValidatorTests
{
    private readonly UpdatePurchaseOrderRequestValidator _validator = new();

    private static DateTime FutureYears(int years) => DateTime.UtcNow.AddYears(years);
    private static DateTime PastYears(int years) => DateTime.UtcNow.AddYears(-years);

    private static UpdatePurchaseOrderRequest ValidRequest() => new()
    {
        Id = 1,
        SupplierId = 1,
        ExpectedDeliveryDate = null,
        Lines = new List<UpdatePurchaseOrderLineRequest>
        {
            new() { MaterialId = "MAT-001", Quantity = 1m, UnitPrice = 1m }
        }
    };

    // --------------- Baseline ---------------

    [Fact]
    public void ValidRequest_PassesAllValidation()
    {
        var request = ValidRequest();

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }

    // --------------- ExpectedDeliveryDate: future bound ---------------

    [Fact]
    public void ExpectedDeliveryDate_ExactlyTwoYearsInFuture_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = FutureYears(2);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    [Fact]
    public void ExpectedDeliveryDate_TwoYearsAndOneDayInFuture_FailsValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = FutureYears(2).AddDays(1);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ExpectedDeliveryDate)
            .WithErrorMessage("Expected delivery date must be reasonable (not more than 2 years in the future)");
    }

    [Fact]
    public void ExpectedDeliveryDate_OneDayInsideFutureBound_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = FutureYears(2).AddDays(-1);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    // --------------- ExpectedDeliveryDate: past bound ---------------

    [Fact]
    public void ExpectedDeliveryDate_AtTenYearPastBoundary_PassesValidation()
    {
        // NOTE: The validator computes its own `DateTime.UtcNow` internally, and that
        // read always happens strictly after the line below executes. A zero-offset
        // "DateTime.UtcNow.AddYears(-10)" value would therefore always be a hair
        // *earlier* than the validator's own lower bound (which is anchored to a
        // later "now") and would incorrectly fail on every run. A tiny forward buffer
        // neutralizes that read-order skew while still targeting the -10 year edge.
        var request = ValidRequest();
        request.ExpectedDeliveryDate = PastYears(10).AddSeconds(1);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    [Fact]
    public void ExpectedDeliveryDate_TenYearsAndOneDayInPast_FailsValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = PastYears(10).AddDays(-1);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ExpectedDeliveryDate)
            .WithErrorMessage("Expected delivery date must be reasonable (not more than 2 years in the future)");
    }

    [Fact]
    public void ExpectedDeliveryDate_OneDayInsidePastBound_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = PastYears(10).AddDays(1);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    // --------------- ExpectedDeliveryDate: null passthrough ---------------

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

- [ ] **Step 2: Build the test project**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Run the new tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdatePurchaseOrderRequestValidatorTests"
```
Expected: all 8 tests pass (`ValidRequest_PassesAllValidation`, `ExpectedDeliveryDate_ExactlyTwoYearsInFuture_PassesValidation`, `ExpectedDeliveryDate_TwoYearsAndOneDayInFuture_FailsValidation`, `ExpectedDeliveryDate_OneDayInsideFutureBound_PassesValidation`, `ExpectedDeliveryDate_AtTenYearPastBoundary_PassesValidation`, `ExpectedDeliveryDate_TenYearsAndOneDayInPast_FailsValidation`, `ExpectedDeliveryDate_OneDayInsidePastBound_PassesValidation`, `ExpectedDeliveryDate_Null_PassesValidation`), 0 failed.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs
git commit -m "Add ExpectedDeliveryDate boundary tests for UpdatePurchaseOrderRequestValidator"
```

---

