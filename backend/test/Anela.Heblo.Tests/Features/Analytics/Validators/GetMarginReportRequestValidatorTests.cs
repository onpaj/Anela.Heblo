using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;
using Anela.Heblo.Application.Features.Analytics.Validators;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics.Validators;

public class GetMarginReportRequestValidatorTests
{
    private readonly GetMarginReportRequestValidator _validator;

    public GetMarginReportRequestValidatorTests()
    {
        _validator = new GetMarginReportRequestValidator();
    }

    #region StartDate vs EndDate Tests

    [Fact]
    public void StartDate_LessThanEndDate_ShouldNotHaveValidationError()
    {
        // Arrange
        var startDate = new DateTime(2024, 01, 01);
        var endDate = new DateTime(2024, 01, 31);
        var request = new GetMarginReportRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            MaxProducts = 50
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.StartDate);
    }

    [Fact]
    public void StartDate_GreaterThanEndDate_ShouldHaveInvalidDateRangeError()
    {
        // Arrange
        var startDate = new DateTime(2024, 12, 31);
        var endDate = new DateTime(2024, 01, 01);
        var request = new GetMarginReportRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            MaxProducts = 50
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.StartDate);
    }

    [Fact]
    public void StartDate_GreaterThanEndDate_ShouldHaveCorrectErrorCode()
    {
        // Arrange
        var startDate = new DateTime(2024, 12, 31);
        var endDate = new DateTime(2024, 01, 01);
        var request = new GetMarginReportRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            MaxProducts = 50
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.Single(f => f.PropertyName == nameof(GetMarginReportRequest.StartDate));
        failure.ErrorCode.Should().Be(((int)ErrorCodes.InvalidDateRange).ToString());
    }

    [Fact]
    public void StartDate_GreaterThanEndDate_ShouldHaveCorrectParams()
    {
        // Arrange
        var startDate = new DateTime(2024, 12, 31);
        var endDate = new DateTime(2024, 01, 01);
        var request = new GetMarginReportRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            MaxProducts = 50
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var error = result.Errors.Single(f => f.PropertyName == nameof(GetMarginReportRequest.StartDate));
        error.CustomState.Should().NotBeNull();
        var paramDict = error.CustomState as Dictionary<string, string>;
        paramDict.Should().NotBeNull();
        paramDict!.Should().ContainKey("startDate");
        paramDict!.Should().ContainKey("endDate");
        paramDict!["startDate"].Should().Be(startDate.ToString("yyyy-MM-dd"));
        paramDict!["endDate"].Should().Be(endDate.ToString("yyyy-MM-dd"));
    }

    #endregion

    #region Report Period Length Tests

    [Fact]
    public void PeriodTooLong_ShouldHaveInvalidReportPeriodError()
    {
        // Arrange - more than 730 days
        var startDate = new DateTime(2020, 01, 01);
        var endDate = new DateTime(2024, 01, 01);
        var request = new GetMarginReportRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            MaxProducts = 50
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void PeriodTooLong_ShouldHaveCorrectErrorCode()
    {
        // Arrange - more than 730 days
        var startDate = new DateTime(2020, 01, 01);
        var endDate = new DateTime(2024, 01, 01);
        var request = new GetMarginReportRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            MaxProducts = 50
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var periodTooLongError = result.Errors.FirstOrDefault(e => e.ErrorCode == ((int)ErrorCodes.InvalidReportPeriod).ToString());
        periodTooLongError.Should().NotBeNull();
        periodTooLongError!.ErrorCode.Should().Be(((int)ErrorCodes.InvalidReportPeriod).ToString());
    }

    [Fact]
    public void PeriodTooShort_ShouldHaveInvalidReportPeriodError()
    {
        // Arrange - same start and end date (0 days)
        var sameDate = new DateTime(2024, 06, 15);
        var request = new GetMarginReportRequest
        {
            StartDate = sameDate,
            EndDate = sameDate,
            MaxProducts = 50
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void PeriodAtBoundary_MaxDays_ShouldNotHaveValidationError()
    {
        // Arrange - exactly 730 days
        var startDate = new DateTime(2024, 01, 01);
        var endDate = startDate.AddDays(730);
        var request = new GetMarginReportRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            MaxProducts = 50
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void PeriodAtBoundary_MinDays_ShouldNotHaveValidationError()
    {
        // Arrange - exactly 1 day
        var startDate = new DateTime(2024, 01, 01);
        var endDate = startDate.AddDays(1);
        var request = new GetMarginReportRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            MaxProducts = 50
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x);
    }

    #endregion

    #region Valid Request Tests

    [Fact]
    public void ValidRequest_ShouldNotHaveAnyValidationErrors()
    {
        // Arrange
        var startDate = new DateTime(2024, 01, 01);
        var endDate = new DateTime(2024, 02, 01);
        var request = new GetMarginReportRequest
        {
            StartDate = startDate,
            EndDate = endDate,
            MaxProducts = 50,
            ProductFilter = null,
            CategoryFilter = null,
            IncludeDetailedBreakdown = false
        };

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion
}
