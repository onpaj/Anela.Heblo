using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;
using Anela.Heblo.Application.Features.Analytics.Validators;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics.Validators;

public class GetProductMarginAnalysisRequestValidatorTests
{
    private readonly GetProductMarginAnalysisRequestValidator _validator;

    public GetProductMarginAnalysisRequestValidatorTests()
    {
        _validator = new GetProductMarginAnalysisRequestValidator();
    }

    private static GetProductMarginAnalysisRequest CreateValidRequest() =>
        new()
        {
            ProductId = "PROD001",
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
            IncludeBreakdown = false
        };

    #region ProductId Tests

    [Fact]
    public void ProductId_Empty_ShouldHaveRequiredFieldMissingError()
    {
        // Arrange
        var request = CreateValidRequest();
        request.ProductId = string.Empty;

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ProductId)
            .WithErrorMessage(AnalyticsConstants.ValidationMessages.PRODUCT_ID_REQUIRED);
    }

    [Fact]
    public void ProductId_Empty_ShouldHaveCorrectErrorCode()
    {
        // Arrange
        var request = CreateValidRequest();
        request.ProductId = string.Empty;

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.Single(f => f.PropertyName == nameof(GetProductMarginAnalysisRequest.ProductId));
        failure.ErrorCode.Should().Be(((int)ErrorCodes.RequiredFieldMissing).ToString());
    }

    [Fact]
    public void ProductId_Empty_ShouldHaveCorrectParams()
    {
        // Arrange
        var request = CreateValidRequest();
        request.ProductId = string.Empty;

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.Single(f => f.PropertyName == nameof(GetProductMarginAnalysisRequest.ProductId));
        var customState = failure.CustomState as Dictionary<string, string>;
        customState.Should().NotBeNull();
        customState.Should().ContainKey("field").WithValue("ProductId");
    }

    [Fact]
    public void ProductId_Valid_ShouldNotHaveValidationError()
    {
        // Arrange
        var request = CreateValidRequest();

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.ProductId);
    }

    #endregion

    #region Date Range Tests

    [Fact]
    public void StartDate_GreaterThanEndDate_ShouldHaveInvalidDateRangeError()
    {
        // Arrange
        var request = CreateValidRequest();
        request.StartDate = new DateTime(2024, 12, 31);
        request.EndDate = new DateTime(2024, 1, 1);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.StartDate)
            .WithErrorMessage(AnalyticsConstants.ValidationMessages.INVALID_DATE_RANGE);
    }

    [Fact]
    public void StartDate_GreaterThanEndDate_ShouldHaveCorrectErrorCode()
    {
        // Arrange
        var request = CreateValidRequest();
        request.StartDate = new DateTime(2024, 12, 31);
        request.EndDate = new DateTime(2024, 1, 1);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.Single(f => f.PropertyName == nameof(GetProductMarginAnalysisRequest.StartDate));
        failure.ErrorCode.Should().Be(((int)ErrorCodes.InvalidDateRange).ToString());
    }

    [Fact]
    public void StartDate_GreaterThanEndDate_ShouldHaveCorrectParams()
    {
        // Arrange
        var request = CreateValidRequest();
        request.StartDate = new DateTime(2024, 12, 31);
        request.EndDate = new DateTime(2024, 1, 1);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.Single(f => f.PropertyName == nameof(GetProductMarginAnalysisRequest.StartDate));
        var customState = failure.CustomState as Dictionary<string, string>;
        customState.Should().NotBeNull();
        customState.Should().ContainKey("startDate");
        customState.Should().ContainKey("endDate");
    }

    #endregion

    #region Period Length Tests

    [Fact]
    public void PeriodTooLong_ShouldHaveInvalidReportPeriodError()
    {
        // Arrange
        var request = CreateValidRequest();
        request.StartDate = new DateTime(2020, 1, 1);
        request.EndDate = new DateTime(2024, 12, 31);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.Errors.Should().Contain(f =>
            f.ErrorMessage.Contains(AnalyticsConstants.ValidationMessages.PERIOD_TOO_LONG.Replace("{0}", AnalyticsConstants.MAX_REPORT_PERIOD_DAYS.ToString())));
    }

    [Fact]
    public void PeriodTooLong_ShouldHaveCorrectErrorCode()
    {
        // Arrange
        var request = CreateValidRequest();
        request.StartDate = new DateTime(2020, 1, 1);
        request.EndDate = new DateTime(2024, 12, 31);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.Single(f => f.ErrorMessage.Contains("exceed"));
        failure.ErrorCode.Should().Be(((int)ErrorCodes.InvalidReportPeriod).ToString());
    }

    [Fact]
    public void PeriodTooShort_ShouldHaveInvalidReportPeriodError()
    {
        // Arrange
        var request = CreateValidRequest();
        request.StartDate = new DateTime(2024, 1, 1);
        request.EndDate = new DateTime(2024, 1, 1);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.Errors.Should().Contain(f =>
            f.ErrorMessage.Contains(AnalyticsConstants.ValidationMessages.PERIOD_TOO_SHORT.Replace("{0}", AnalyticsConstants.MIN_REPORT_PERIOD_DAYS.ToString())));
    }

    #endregion

    #region Valid Request Tests

    [Fact]
    public void ValidRequest_ShouldNotHaveAnyValidationErrors()
    {
        // Arrange
        var request = CreateValidRequest();

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    #endregion
}
