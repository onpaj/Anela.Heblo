using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;
using Anela.Heblo.Application.Features.Analytics.Validators;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics.Pipeline;

/// <summary>
/// Integration tests for Analytics validation pipeline behavior.
/// Verifies that the ValidationResultBehavior is wired correctly in the MediatR pipeline
/// and that validators are executed before handlers are called.
/// </summary>
public class AnalyticsValidationPipelineTests
{
    /// <summary>
    /// Builds a real MediatR mediator with validation pipeline behavior configured.
    /// Used to test the complete request/response flow including validation.
    /// </summary>
    private IMediator BuildMediator(
        Mock<IAnalyticsRepository>? repoMock = null,
        Mock<IProductFilterService>? filterMock = null,
        Mock<IReportBuilderService>? builderMock = null)
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GetMarginReportHandler).Assembly));

        // Validators
        services.AddScoped<IValidator<GetMarginReportRequest>, GetMarginReportRequestValidator>();
        services.AddScoped<IValidator<GetProductMarginAnalysisRequest>, GetProductMarginAnalysisRequestValidator>();

        // Pipeline behaviors
        services.AddScoped<IPipelineBehavior<GetMarginReportRequest, GetMarginReportResponse>,
            ValidationResultBehavior<GetMarginReportRequest, GetMarginReportResponse>>();
        services.AddScoped<IPipelineBehavior<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>,
            ValidationResultBehavior<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>>();

        // Handler dependencies (mocked - handlers won't be called for invalid requests)
        services.AddScoped(_ => (repoMock ?? new Mock<IAnalyticsRepository>()).Object);
        services.AddScoped(_ => (filterMock ?? new Mock<IProductFilterService>()).Object);
        services.AddScoped(_ => (builderMock ?? new Mock<IReportBuilderService>()).Object);
        services.AddScoped<IMarginCalculator, MarginCalculator>();

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task GetMarginReport_InvalidDateRange_ReturnsInvalidDateRangeErrorCode()
    {
        // Arrange
        var mediator = BuildMediator();
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2024, 12, 31),
            EndDate = new DateTime(2024, 01, 01),
            MaxProducts = 50
        };

        // Act
        var result = await mediator.Send(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
        result.Params.Should().ContainKey("startDate");
        result.Params.Should().ContainKey("endDate");
    }

    [Fact]
    public async Task GetMarginReport_PeriodTooLong_ReturnsInvalidReportPeriodErrorCode()
    {
        // Arrange
        var mediator = BuildMediator();
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2020, 01, 01),
            EndDate = new DateTime(2024, 01, 01),
            MaxProducts = 50
        };

        // Act
        var result = await mediator.Send(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidReportPeriod);
        result.Params.Should().ContainKey("period");
    }

    [Fact]
    public async Task GetProductMarginAnalysis_EmptyProductId_ReturnsRequiredFieldMissingErrorCode()
    {
        // Arrange
        var mediator = BuildMediator();
        var request = new GetProductMarginAnalysisRequest
        {
            ProductId = "",
            StartDate = new DateTime(2024, 01, 01),
            EndDate = new DateTime(2024, 12, 31)
        };

        // Act
        var result = await mediator.Send(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        result.Params.Should().ContainKey("field");
        result.Params["field"].Should().Be("ProductId");
    }

    [Fact]
    public async Task GetProductMarginAnalysis_InvalidDateRange_ReturnsInvalidDateRangeErrorCode()
    {
        // Arrange
        var mediator = BuildMediator();
        var request = new GetProductMarginAnalysisRequest
        {
            ProductId = "PROD001",
            StartDate = new DateTime(2024, 12, 31),
            EndDate = new DateTime(2024, 01, 01)
        };

        // Act
        var result = await mediator.Send(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
    }
}
