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
