using Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;
using FluentAssertions;
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

    private static UpdateProductCompositionOrderRequest ValidRequest() => new()
    {
        ProductCode = "PROD-001",
        Order = new List<IngredientOrderItem>
        {
            new() { IngredientProductCode = "MAT-A", SortOrder = 1 },
            new() { IngredientProductCode = "MAT-B", SortOrder = 2 },
        }
    };

    [Fact]
    public void ValidRequest_UniqueIngredientCodes_PassesAllValidation()
    {
        var result = _validator.TestValidate(ValidRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Order_NullValue_FailsValidation()
    {
        var request = new UpdateProductCompositionOrderRequest { ProductCode = "PROD-001", Order = null! };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Order)
            .WithErrorMessage("Order list is required");
    }

    [Fact]
    public void Order_EmptyList_PassesValidation()
    {
        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PROD-001",
            Order = new List<IngredientOrderItem>()
        };

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.Order);
    }

    [Fact]
    public void Order_DuplicateIngredientCodes_FailsValidation()
    {
        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PROD-001",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1 },
                new() { IngredientProductCode = "MAT-A", SortOrder = 2 },
            }
        };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Order)
            .WithErrorMessage("Ingredient product codes must be unique within the order list");
    }

    [Fact]
    public void Order_MultipleDuplicateCodes_FailsValidation()
    {
        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PROD-001",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = 1 },
                new() { IngredientProductCode = "MAT-B", SortOrder = 2 },
                new() { IngredientProductCode = "MAT-A", SortOrder = 3 },
                new() { IngredientProductCode = "MAT-B", SortOrder = 4 },
            }
        };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Order);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IngredientProductCode_EmptyOrWhitespace_FailsValidation(string code)
    {
        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PROD-001",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = code, SortOrder = 1 },
            }
        };

        var result = _validator.TestValidate(request);

        result.Errors.Should().Contain(e =>
            e.PropertyName == "Order[0].IngredientProductCode" &&
            e.ErrorMessage == "Ingredient product code is required");
    }

    [Fact]
    public void IngredientProductCode_ExceedsMaxLength_FailsValidation()
    {
        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PROD-001",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = new string('X', 51), SortOrder = 1 },
            }
        };

        var result = _validator.TestValidate(request);

        result.Errors.Should().Contain(e =>
            e.PropertyName == "Order[0].IngredientProductCode" &&
            e.ErrorMessage == "Ingredient product code cannot exceed 50 characters");
    }

    [Fact]
    public void IngredientProductCode_ExactlyMaxLength_PassesValidation()
    {
        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PROD-001",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = new string('X', 50), SortOrder = 1 },
            }
        };

        var result = _validator.TestValidate(request);

        result.Errors.Should().NotContain(e => e.PropertyName == "Order[0].IngredientProductCode");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void SortOrder_ZeroOrNegative_FailsValidation(int sortOrder)
    {
        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PROD-001",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = sortOrder },
            }
        };

        var result = _validator.TestValidate(request);

        result.Errors.Should().Contain(e =>
            e.PropertyName == "Order[0].SortOrder" &&
            e.ErrorMessage == "Sort order must be greater than 0");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(100)]
    public void SortOrder_PositiveValue_PassesValidation(int sortOrder)
    {
        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PROD-001",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "MAT-A", SortOrder = sortOrder },
            }
        };

        var result = _validator.TestValidate(request);

        result.Errors.Should().NotContain(e => e.PropertyName == "Order[0].SortOrder");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ProductCode_EmptyOrNull_FailsValidation(string? productCode)
    {
        var request = ValidRequest();
        request.ProductCode = productCode!;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ProductCode)
            .WithErrorMessage("Product code is required");
    }

    [Fact]
    public void ProductCode_ExceedsMaxLength_FailsValidation()
    {
        var request = ValidRequest();
        request.ProductCode = new string('P', 51);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ProductCode)
            .WithErrorMessage("Product code cannot exceed 50 characters");
    }

    [Fact]
    public void MultipleInvalidItems_AllItemErrorsReported()
    {
        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PROD-001",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "", SortOrder = 0 },
                new() { IngredientProductCode = "MAT-B", SortOrder = 1 },
            }
        };

        var result = _validator.TestValidate(request);

        result.Errors.Should().Contain(e => e.PropertyName == "Order[0].IngredientProductCode");
        result.Errors.Should().Contain(e => e.PropertyName == "Order[0].SortOrder");
        result.Errors.Should().NotContain(e => e.PropertyName == "Order[1].IngredientProductCode");
        result.Errors.Should().NotContain(e => e.PropertyName == "Order[1].SortOrder");
    }
}
