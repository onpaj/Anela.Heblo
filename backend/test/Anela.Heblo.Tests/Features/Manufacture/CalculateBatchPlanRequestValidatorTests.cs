using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using Anela.Heblo.Application.Features.Manufacture.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class CalculateBatchPlanRequestValidatorTests
{
    private readonly CalculateBatchPlanRequestValidator _validator;

    public CalculateBatchPlanRequestValidatorTests()
    {
        _validator = new CalculateBatchPlanRequestValidator();
    }

    [Fact]
    public void Validate_ValidRequest_PassesValidation()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.5
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_InvalidSemiproductCode_FailsValidation(string? semiproductCode)
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = semiproductCode,
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.SemiproductCode);
    }

    [Fact]
    public void Validate_MMQMultiplierMode_WithoutMultiplier_FailsValidation()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = null
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MmqMultiplier);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_MMQMultiplierMode_WithInvalidMultiplier_FailsValidation(double multiplier)
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = multiplier
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MmqMultiplier);
    }

    [Fact]
    public void Validate_TotalWeightMode_WithoutWeight_FailsValidation()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.TotalWeight,
            TotalWeightToUse = null
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TotalWeightToUse);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_TotalWeightMode_WithInvalidWeight_FailsValidation(double weight)
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.TotalWeight,
            TotalWeightToUse = weight
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TotalWeightToUse);
    }

    [Fact]
    public void Validate_TargetCoverageMode_WithoutCoverage_FailsValidation()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.TargetDaysCoverage,
            TargetDaysCoverage = null
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TargetDaysCoverage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_TargetCoverageMode_WithInvalidCoverage_FailsValidation(double coverage)
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.TargetDaysCoverage,
            TargetDaysCoverage = coverage
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TargetDaysCoverage);
    }

    [Fact]
    public void Validate_InvalidDateRange_FailsValidation()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0,
            FromDate = DateTime.Now.AddDays(1),
            ToDate = DateTime.Now
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.FromDate);
    }

    [Fact]
    public void Validate_ProductConstraint_WithoutProductCode_FailsValidation()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0,
            ProductConstraints = new List<ProductSizeConstraint>
            {
                new ProductSizeConstraint { ProductCode = "", IsFixed = true, FixedQuantity = 100 }
            }
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor("ProductConstraints[0].ProductCode");
    }

    [Fact]
    public void Validate_FixedProductConstraint_WithoutQuantity_FailsValidation()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0,
            ProductConstraints = new List<ProductSizeConstraint>
            {
                new ProductSizeConstraint { ProductCode = "PROD001", IsFixed = true, FixedQuantity = null }
            }
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor("ProductConstraints[0].FixedQuantity");
    }

    [Fact]
    public void Validate_FixedProductConstraint_WithNegativeQuantity_FailsValidation()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0,
            ProductConstraints = new List<ProductSizeConstraint>
            {
                new ProductSizeConstraint { ProductCode = "PROD001", IsFixed = true, FixedQuantity = -5 }
            }
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor("ProductConstraints[0].FixedQuantity");
    }
}