using Anela.Heblo.Application.Features.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOutput;
using Anela.Heblo.Application.Features.Manufacture.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class GetManufactureOutputRequestValidatorTests
{
    private readonly GetManufactureOutputRequestValidator _validator;

    public GetManufactureOutputRequestValidatorTests()
    {
        _validator = new GetManufactureOutputRequestValidator();
    }

    [Fact]
    public void MonthsBack_WhenValid_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufactureOutputRequest { MonthsBack = 12 };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.MonthsBack);
    }

    [Fact]
    public void MonthsBack_WhenMinimumValue_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufactureOutputRequest { MonthsBack = ManufactureConstants.MIN_MONTHS_BACK };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.MonthsBack);
    }

    [Fact]
    public void MonthsBack_WhenMaximumValue_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufactureOutputRequest { MonthsBack = ManufactureConstants.MAX_MONTHS_BACK };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.MonthsBack);
    }

    [Fact]
    public void MonthsBack_WhenBelowMinimum_ShouldHaveValidationError()
    {
        // Arrange
        var request = new GetManufactureOutputRequest { MonthsBack = ManufactureConstants.MIN_MONTHS_BACK - 1 };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.MonthsBack)
            .WithErrorMessage($"MonthsBack must be at least {ManufactureConstants.MIN_MONTHS_BACK}");
    }

    [Fact]
    public void MonthsBack_WhenAboveMaximum_ShouldHaveValidationError()
    {
        // Arrange
        var request = new GetManufactureOutputRequest { MonthsBack = ManufactureConstants.MAX_MONTHS_BACK + 1 };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.MonthsBack)
            .WithErrorMessage($"MonthsBack cannot exceed {ManufactureConstants.MAX_MONTHS_BACK}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void MonthsBack_WhenZeroOrNegative_ShouldHaveValidationError(int monthsBack)
    {
        // Arrange
        var request = new GetManufactureOutputRequest { MonthsBack = monthsBack };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.MonthsBack);
    }

    [Theory]
    [InlineData(61)]
    [InlineData(100)]
    [InlineData(999)]
    public void MonthsBack_WhenExceedsMaximum_ShouldHaveValidationError(int monthsBack)
    {
        // Arrange
        var request = new GetManufactureOutputRequest { MonthsBack = monthsBack };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.MonthsBack);
    }
}