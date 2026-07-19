using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;
using Anela.Heblo.Application.Features.Manufacture.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class CalculateBatchByIngredientRequestValidatorTests
{
    private readonly CalculateBatchByIngredientRequestValidator _validator;

    public CalculateBatchByIngredientRequestValidatorTests()
    {
        _validator = new CalculateBatchByIngredientRequestValidator();
    }

    [Fact]
    public void Validate_ValidRequest_PassesValidation()
    {
        // Arrange
        var request = new CalculateBatchByIngredientRequest
        {
            ProductCode = "PROD001",
            IngredientCode = "ING001",
            DesiredIngredientAmount = 100
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Validate_DesiredIngredientAmount_BelowOrEqualZero_FailsValidation(double amount)
    {
        // Arrange
        var request = new CalculateBatchByIngredientRequest
        {
            ProductCode = "PROD001",
            IngredientCode = "ING001",
            DesiredIngredientAmount = amount
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.DesiredIngredientAmount);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(100)]
    [InlineData(999999.99)]
    public void Validate_DesiredIngredientAmount_ValidPositiveValue_PassesValidation(double amount)
    {
        // Arrange
        var request = new CalculateBatchByIngredientRequest
        {
            ProductCode = "PROD001",
            IngredientCode = "ING001",
            DesiredIngredientAmount = amount
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.DesiredIngredientAmount);
    }

    [Theory]
    [InlineData(1000000)]
    [InlineData(999999.991)]
    public void Validate_DesiredIngredientAmount_AboveUpperBound_FailsValidation(double amount)
    {
        // Arrange
        var request = new CalculateBatchByIngredientRequest
        {
            ProductCode = "PROD001",
            IngredientCode = "ING001",
            DesiredIngredientAmount = amount
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.DesiredIngredientAmount);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_ProductCode_Empty_FailsValidation(string? productCode)
    {
        // Arrange
        var request = new CalculateBatchByIngredientRequest
        {
            ProductCode = productCode,
            IngredientCode = "ING001",
            DesiredIngredientAmount = 100
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ProductCode);
    }

    [Fact]
    public void Validate_ProductCode_MaxLength_Boundary()
    {
        // Arrange
        var validRequest = new CalculateBatchByIngredientRequest
        {
            ProductCode = new string('A', 50),
            IngredientCode = "ING001",
            DesiredIngredientAmount = 100
        };
        var invalidRequest = new CalculateBatchByIngredientRequest
        {
            ProductCode = new string('A', 51),
            IngredientCode = "ING001",
            DesiredIngredientAmount = 100
        };

        // Act
        var validResult = _validator.TestValidate(validRequest);
        var invalidResult = _validator.TestValidate(invalidRequest);

        // Assert
        validResult.ShouldNotHaveValidationErrorFor(x => x.ProductCode);
        invalidResult.ShouldHaveValidationErrorFor(x => x.ProductCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_IngredientCode_Empty_FailsValidation(string? ingredientCode)
    {
        // Arrange
        var request = new CalculateBatchByIngredientRequest
        {
            ProductCode = "PROD001",
            IngredientCode = ingredientCode,
            DesiredIngredientAmount = 100
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.IngredientCode);
    }

    [Fact]
    public void Validate_IngredientCode_MaxLength_Boundary()
    {
        // Arrange
        var validRequest = new CalculateBatchByIngredientRequest
        {
            ProductCode = "PROD001",
            IngredientCode = new string('A', 50),
            DesiredIngredientAmount = 100
        };
        var invalidRequest = new CalculateBatchByIngredientRequest
        {
            ProductCode = "PROD001",
            IngredientCode = new string('A', 51),
            DesiredIngredientAmount = 100
        };

        // Act
        var validResult = _validator.TestValidate(validRequest);
        var invalidResult = _validator.TestValidate(invalidRequest);

        // Assert
        validResult.ShouldNotHaveValidationErrorFor(x => x.IngredientCode);
        invalidResult.ShouldHaveValidationErrorFor(x => x.IngredientCode);
    }
}
