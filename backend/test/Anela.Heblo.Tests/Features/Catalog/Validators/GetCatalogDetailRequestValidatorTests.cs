using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Application.Features.Catalog.Validators;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Validators;

public class GetCatalogDetailRequestValidatorTests
{
    private readonly GetCatalogDetailRequestValidator _validator;

    public GetCatalogDetailRequestValidatorTests()
    {
        _validator = new GetCatalogDetailRequestValidator();
    }

    [Theory]
    [InlineData("PROD-001")]
    [InlineData("ABC123")]
    [InlineData("X")]
    [InlineData("12345678901234567890123456789012345678901234567890")] // 50 characters - valid
    public void ProductCode_ValidValues_PassesValidation(string productCode)
    {
        // Arrange
        var request = new GetCatalogDetailRequest { ProductCode = productCode, MonthsBack = 12 };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.ProductCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890123456789012345678901")] // 51 characters - too long
    public void ProductCode_InvalidValues_FailsValidation(string productCode)
    {
        // Arrange
        var request = new GetCatalogDetailRequest { ProductCode = productCode, MonthsBack = 12 };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ProductCode);
    }

    [Fact]
    public void ProductCode_Empty_HasCorrectErrorMessage()
    {
        // Arrange
        var request = new GetCatalogDetailRequest { ProductCode = "", MonthsBack = 12 };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ProductCode)
            .WithErrorMessage("Product code is required");
    }

    [Fact]
    public void ProductCode_TooLong_HasCorrectErrorMessage()
    {
        // Arrange
        var request = new GetCatalogDetailRequest 
        { 
            ProductCode = "123456789012345678901234567890123456789012345678901", // 51 characters
            MonthsBack = 12 
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ProductCode)
            .WithErrorMessage("Product code cannot exceed 50 characters");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(36)]
    [InlineData(CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD)] // 999
    public void MonthsBack_ValidValues_PassesValidation(int monthsBack)
    {
        // Arrange
        var request = new GetCatalogDetailRequest { ProductCode = "PROD-001", MonthsBack = monthsBack };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.MonthsBack);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    [InlineData(-100)]
    [InlineData(1000)] // Greater than ALL_HISTORY_MONTHS_THRESHOLD
    [InlineData(1001)]
    [InlineData(int.MaxValue)]
    public void MonthsBack_InvalidValues_FailsValidation(int monthsBack)
    {
        // Arrange
        var request = new GetCatalogDetailRequest { ProductCode = "PROD-001", MonthsBack = monthsBack };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MonthsBack);
    }

    [Fact]
    public void MonthsBack_Negative_HasCorrectErrorMessage()
    {
        // Arrange
        var request = new GetCatalogDetailRequest { ProductCode = "PROD-001", MonthsBack = -5 };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MonthsBack)
            .WithErrorMessage("MonthsBack cannot be negative");
    }

    [Fact]
    public void MonthsBack_ExceedsThreshold_HasCorrectErrorMessage()
    {
        // Arrange
        var request = new GetCatalogDetailRequest { ProductCode = "PROD-001", MonthsBack = 1000 };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MonthsBack)
            .WithErrorMessage($"MonthsBack cannot exceed {CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD}");
    }

    [Fact]
    public void ValidRequest_PassesAllValidation()
    {
        // Arrange
        var request = new GetCatalogDetailRequest 
        { 
            ProductCode = "PROD-001", 
            MonthsBack = 12 
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void InvalidRequest_FailsMultipleValidations()
    {
        // Arrange
        var request = new GetCatalogDetailRequest 
        { 
            ProductCode = "", // Invalid: empty
            MonthsBack = -5   // Invalid: negative
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ProductCode);
        result.ShouldHaveValidationErrorFor(x => x.MonthsBack);
        result.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void EdgeCase_ProductCodeExactly50Characters_PassesValidation()
    {
        // Arrange
        var fiftyCharString = new string('A', 50);
        var request = new GetCatalogDetailRequest { ProductCode = fiftyCharString, MonthsBack = 12 };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.ProductCode);
    }

    [Fact]
    public void EdgeCase_MonthsBackAtThreshold_PassesValidation()
    {
        // Arrange
        var request = new GetCatalogDetailRequest 
        { 
            ProductCode = "PROD-001", 
            MonthsBack = CatalogConstants.ALL_HISTORY_MONTHS_THRESHOLD 
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.MonthsBack);
    }

    [Fact]
    public void EdgeCase_MonthsBackZero_PassesValidation()
    {
        // Arrange
        var request = new GetCatalogDetailRequest { ProductCode = "PROD-001", MonthsBack = 0 };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.MonthsBack);
    }
}