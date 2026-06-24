using Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class CreatePurchaseOrderRequestValidatorTests
{
    private readonly CreatePurchaseOrderRequestValidator _validator = new();

    private static string TodayStr => DateTime.UtcNow.ToString("yyyy-MM-dd");
    private static string FutureStr(int days) => DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-dd");
    private static string PastStr(int days) => DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");

    private static CreatePurchaseOrderRequest ValidRequest() => new()
    {
        SupplierId = 1,
        OrderDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        Lines = null
    };

    // --------------- SupplierId ---------------

    [Theory]
    [InlineData(1L)]
    [InlineData(100L)]
    [InlineData(long.MaxValue)]
    public void SupplierId_ValidValues_PassesValidation(long supplierId)
    {
        var request = ValidRequest();
        request.SupplierId = supplierId;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.SupplierId);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void SupplierId_InvalidValues_FailsValidation(long supplierId)
    {
        var request = ValidRequest();
        request.SupplierId = supplierId;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.SupplierId)
            .WithErrorMessage("Supplier is required");
    }

    // --------------- OrderDate ---------------

    [Fact]
    public void OrderDate_Empty_FailsValidation()
    {
        var request = ValidRequest();
        request.OrderDate = "";

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.OrderDate);
    }

    [Fact]
    public void OrderDate_InvalidString_FailsValidation()
    {
        var request = ValidRequest();
        request.OrderDate = "not-a-date";

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.OrderDate)
            .WithErrorMessage("Order date must be a valid date");
    }

    [Fact]
    public void OrderDate_PastDate_PassesValidation()
    {
        var request = ValidRequest();
        request.OrderDate = PastStr(30);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.OrderDate);
    }

    [Fact]
    public void OrderDate_Today_PassesValidation()
    {
        var request = ValidRequest();
        request.OrderDate = TodayStr;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.OrderDate);
    }

    [Fact]
    public void OrderDate_Exactly30DaysInFuture_PassesValidation()
    {
        var request = ValidRequest();
        request.OrderDate = FutureStr(30);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.OrderDate);
    }

    [Fact]
    public void OrderDate_31DaysInFuture_FailsValidation()
    {
        var request = ValidRequest();
        request.OrderDate = FutureStr(31);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.OrderDate)
            .WithErrorMessage("Order date cannot be more than 30 days in the future");
    }

    // --------------- ExpectedDeliveryDate ---------------

    [Fact]
    public void ExpectedDeliveryDate_Null_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = null;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    [Fact]
    public void ExpectedDeliveryDate_Empty_PassesValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = "";

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    [Fact]
    public void ExpectedDeliveryDate_InvalidString_FailsValidation()
    {
        var request = ValidRequest();
        request.ExpectedDeliveryDate = "not-a-date";

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ExpectedDeliveryDate)
            .WithErrorMessage("Expected delivery date must be a valid date");
    }

    [Fact]
    public void ExpectedDeliveryDate_SameAsOrderDate_PassesValidation()
    {
        var request = ValidRequest();
        request.OrderDate = TodayStr;
        request.ExpectedDeliveryDate = TodayStr;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    [Fact]
    public void ExpectedDeliveryDate_AfterOrderDate_PassesValidation()
    {
        var request = ValidRequest();
        request.OrderDate = TodayStr;
        request.ExpectedDeliveryDate = FutureStr(7);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate);
    }

    [Fact]
    public void ExpectedDeliveryDate_BeforeOrderDate_FailsValidation()
    {
        var request = ValidRequest();
        request.OrderDate = TodayStr;
        request.ExpectedDeliveryDate = PastStr(1);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ExpectedDeliveryDate)
            .WithErrorMessage("Expected delivery date must be on or after the order date");
    }

    // --------------- Notes ---------------

    [Fact]
    public void Notes_Null_PassesValidation()
    {
        var request = ValidRequest();
        request.Notes = null;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void Notes_Exactly1000Characters_PassesValidation()
    {
        var request = ValidRequest();
        request.Notes = new string('A', 1000);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void Notes_1001Characters_FailsValidation()
    {
        var request = ValidRequest();
        request.Notes = new string('A', 1001);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Notes)
            .WithErrorMessage("Notes cannot exceed 1000 characters");
    }

    // --------------- OrderNumber ---------------

    [Fact]
    public void OrderNumber_Null_PassesValidation()
    {
        var request = ValidRequest();
        request.OrderNumber = null;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.OrderNumber);
    }

    [Fact]
    public void OrderNumber_Exactly50Characters_PassesValidation()
    {
        var request = ValidRequest();
        request.OrderNumber = new string('X', 50);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.OrderNumber);
    }

    [Fact]
    public void OrderNumber_51Characters_FailsValidation()
    {
        var request = ValidRequest();
        request.OrderNumber = new string('X', 51);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.OrderNumber)
            .WithErrorMessage("Order number cannot exceed 50 characters");
    }

    // --------------- Lines count ---------------

    [Fact]
    public void Lines_Null_PassesValidation()
    {
        var request = ValidRequest();
        request.Lines = null;

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void Lines_Empty_PassesValidation()
    {
        var request = ValidRequest();
        request.Lines = new List<CreatePurchaseOrderLineRequest>();

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void Lines_Exactly100Items_PassesValidation()
    {
        var request = ValidRequest();
        request.Lines = Enumerable.Range(0, 100)
            .Select(_ => new CreatePurchaseOrderLineRequest
            {
                MaterialId = "MAT-001",
                Quantity = 1m,
                UnitPrice = 0m
            })
            .ToList();

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void Lines_101Items_FailsValidation()
    {
        var request = ValidRequest();
        request.Lines = Enumerable.Range(0, 101)
            .Select(_ => new CreatePurchaseOrderLineRequest
            {
                MaterialId = "MAT-001",
                Quantity = 1m,
                UnitPrice = 0m
            })
            .ToList();

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Lines)
            .WithErrorMessage("A purchase order cannot have more than 100 line items");
    }

    // --------------- Full valid request ---------------

    [Fact]
    public void ValidRequest_PassesAllValidation()
    {
        var request = new CreatePurchaseOrderRequest
        {
            SupplierId = 42,
            OrderDate = TodayStr,
            ExpectedDeliveryDate = FutureStr(7),
            Notes = "Test order",
            OrderNumber = "PO-2026-001",
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new()
                {
                    MaterialId = "MAT-001",
                    Name = "Test Material",
                    Quantity = 10m,
                    UnitPrice = 5.50m,
                    Notes = null
                }
            }
        };

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveAnyValidationErrors();
    }
}

public class CreatePurchaseOrderLineRequestValidatorTests
{
    private readonly CreatePurchaseOrderLineRequestValidator _validator = new();

    private static CreatePurchaseOrderLineRequest ValidLine() => new()
    {
        MaterialId = "MAT-001",
        Quantity = 1m,
        UnitPrice = 0m
    };

    // --------------- MaterialId ---------------

    [Fact]
    public void MaterialId_Empty_FailsValidation()
    {
        var line = ValidLine();
        line.MaterialId = "";

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.MaterialId)
            .WithErrorMessage("Material ID is required");
    }

    [Fact]
    public void MaterialId_Valid_PassesValidation()
    {
        var line = ValidLine();
        line.MaterialId = "MAT-001";

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.MaterialId);
    }

    [Fact]
    public void MaterialId_Exactly50Characters_PassesValidation()
    {
        var line = ValidLine();
        line.MaterialId = new string('M', 50);

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.MaterialId);
    }

    [Fact]
    public void MaterialId_51Characters_FailsValidation()
    {
        var line = ValidLine();
        line.MaterialId = new string('M', 51);

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.MaterialId)
            .WithErrorMessage("Material ID cannot exceed 50 characters");
    }

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
    public void Quantity_SmallPositive_PassesValidation()
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
        line.Quantity = 1000000m;

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.Quantity)
            .WithErrorMessage("Quantity cannot exceed 999999.99");
    }

    // --------------- UnitPrice ---------------

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
    public void UnitPrice_Zero_PassesValidation()
    {
        var line = ValidLine();
        line.UnitPrice = 0m;

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.UnitPrice);
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
        line.UnitPrice = 1000000m;

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.UnitPrice)
            .WithErrorMessage("Unit price cannot exceed 999999.99");
    }

    // --------------- Notes ---------------

    [Fact]
    public void Notes_Null_PassesValidation()
    {
        var line = ValidLine();
        line.Notes = null;

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void Notes_Exactly500Characters_PassesValidation()
    {
        var line = ValidLine();
        line.Notes = new string('N', 500);

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void Notes_501Characters_FailsValidation()
    {
        var line = ValidLine();
        line.Notes = new string('N', 501);

        var result = _validator.TestValidate(line);

        result.ShouldHaveValidationErrorFor(x => x.Notes)
            .WithErrorMessage("Notes cannot exceed 500 characters");
    }

    // --------------- Full valid line ---------------

    [Fact]
    public void ValidLine_PassesAllValidation()
    {
        var line = new CreatePurchaseOrderLineRequest
        {
            MaterialId = "MAT-001",
            Name = "Test Material",
            Quantity = 5m,
            UnitPrice = 10.99m,
            Notes = "Some notes"
        };

        var result = _validator.TestValidate(line);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
