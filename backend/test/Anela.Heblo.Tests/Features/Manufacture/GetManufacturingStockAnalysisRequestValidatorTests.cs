using Anela.Heblo.Application.Features.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;
using Anela.Heblo.Application.Features.Manufacture.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class GetManufacturingStockAnalysisRequestValidatorTests
{
    private readonly GetManufacturingStockAnalysisRequestValidator _validator;

    public GetManufacturingStockAnalysisRequestValidatorTests()
    {
        _validator = new GetManufacturingStockAnalysisRequestValidator();
    }

    #region PageSize Tests

    [Fact]
    public void PageSize_WhenValid_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { PageSize = 20 };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void PageSize_WhenMinimumValue_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { PageSize = ManufactureConstants.MIN_PAGE_SIZE };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void PageSize_WhenMaximumValue_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { PageSize = ManufactureConstants.MAX_PAGE_SIZE };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void PageSize_WhenBelowMinimum_ShouldHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { PageSize = ManufactureConstants.MIN_PAGE_SIZE - 1 };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.PageSize)
            .WithErrorMessage($"PageSize must be at least {ManufactureConstants.MIN_PAGE_SIZE}");
    }

    [Fact]
    public void PageSize_WhenAboveMaximum_ShouldHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { PageSize = ManufactureConstants.MAX_PAGE_SIZE + 1 };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.PageSize)
            .WithErrorMessage($"PageSize cannot exceed {ManufactureConstants.MAX_PAGE_SIZE}");
    }

    #endregion

    #region PageNumber Tests

    [Fact]
    public void PageNumber_WhenValid_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { PageNumber = 5 };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.PageNumber);
    }

    [Fact]
    public void PageNumber_WhenMinimumValue_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { PageNumber = ManufactureConstants.MIN_PAGE_NUMBER };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.PageNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void PageNumber_WhenBelowMinimum_ShouldHaveValidationError(int pageNumber)
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { PageNumber = pageNumber };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.PageNumber)
            .WithErrorMessage($"PageNumber must be at least {ManufactureConstants.MIN_PAGE_NUMBER}");
    }

    #endregion

    #region CustomPeriod Tests

    [Fact]
    public void CustomPeriod_WhenNotSelected_ShouldNotValidateCustomDates()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            TimePeriod = TimePeriodFilter.PreviousQuarter,
            CustomFromDate = null,
            CustomToDate = null
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.CustomFromDate);
        result.ShouldNotHaveValidationErrorFor(x => x.CustomToDate);
    }

    [Fact]
    public void CustomPeriod_WhenSelectedWithValidDates_ShouldNotHaveValidationError()
    {
        // Arrange
        var fromDate = new DateTime(2023, 1, 1);
        var toDate = new DateTime(2023, 12, 31);
        var request = new GetManufacturingStockAnalysisRequest
        {
            TimePeriod = TimePeriodFilter.CustomPeriod,
            CustomFromDate = fromDate,
            CustomToDate = toDate
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.CustomFromDate);
        result.ShouldNotHaveValidationErrorFor(x => x.CustomToDate);
        result.ShouldNotHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void CustomPeriod_WhenSelectedWithoutFromDate_ShouldHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            TimePeriod = TimePeriodFilter.CustomPeriod,
            CustomFromDate = null,
            CustomToDate = new DateTime(2023, 12, 31)
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.CustomFromDate)
            .WithErrorMessage("CustomFromDate is required when using CustomPeriod");
    }

    [Fact]
    public void CustomPeriod_WhenSelectedWithoutToDate_ShouldHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            TimePeriod = TimePeriodFilter.CustomPeriod,
            CustomFromDate = new DateTime(2023, 1, 1),
            CustomToDate = null
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.CustomToDate)
            .WithErrorMessage("CustomToDate is required when using CustomPeriod");
    }

    [Fact]
    public void CustomPeriod_WhenFromDateAfterToDate_ShouldHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            TimePeriod = TimePeriodFilter.CustomPeriod,
            CustomFromDate = new DateTime(2023, 12, 31),
            CustomToDate = new DateTime(2023, 1, 1)
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorMessage("CustomFromDate must be before or equal to CustomToDate");
    }

    [Fact]
    public void CustomPeriod_WhenFromDateEqualsToDate_ShouldNotHaveValidationError()
    {
        // Arrange
        var sameDate = new DateTime(2023, 6, 15);
        var request = new GetManufacturingStockAnalysisRequest
        {
            TimePeriod = TimePeriodFilter.CustomPeriod,
            CustomFromDate = sameDate,
            CustomToDate = sameDate
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x);
    }

    #endregion

    #region SearchTerm Tests

    [Fact]
    public void SearchTerm_WhenNull_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { SearchTerm = null };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.SearchTerm);
    }

    [Fact]
    public void SearchTerm_WhenEmpty_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { SearchTerm = string.Empty };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.SearchTerm);
    }

    [Fact]
    public void SearchTerm_WhenValidLength_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { SearchTerm = "Valid search term" };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.SearchTerm);
    }

    [Fact]
    public void SearchTerm_WhenTooLong_ShouldHaveValidationError()
    {
        // Arrange
        var longSearchTerm = new string('a', 101); // 101 characters
        var request = new GetManufacturingStockAnalysisRequest { SearchTerm = longSearchTerm };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.SearchTerm)
            .WithErrorMessage("SearchTerm cannot exceed 100 characters");
    }

    #endregion

    #region ProductFamily Tests

    [Fact]
    public void ProductFamily_WhenNull_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { ProductFamily = null };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.ProductFamily);
    }

    [Fact]
    public void ProductFamily_WhenValidLength_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest { ProductFamily = "ValidFamily" };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.ProductFamily);
    }

    [Fact]
    public void ProductFamily_WhenTooLong_ShouldHaveValidationError()
    {
        // Arrange
        var longProductFamily = new string('a', 51); // 51 characters
        var request = new GetManufacturingStockAnalysisRequest { ProductFamily = longProductFamily };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ProductFamily)
            .WithErrorMessage("ProductFamily cannot exceed 50 characters");
    }

    #endregion
}