using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Analytics.DashboardTiles;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;
using Anela.Heblo.Application.Features.Analytics.Validators;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Analytics;

/// <summary>
/// Enhanced analytics module with refactored services and validation
/// Registers new services for better separation of concerns and testability
/// </summary>
public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        // Register refactored services for clean separation of concerns
        // Note: IMarginCalculationService is registered by CatalogModule and injected here
        services.AddScoped<IProductFilterService, ProductFilterService>();
        services.AddScoped<IReportBuilderService, ReportBuilderService>();

        // Register validators for FluentValidation
        services.AddScoped<IValidator<GetMarginReportRequest>, GetMarginReportRequestValidator>();
        services.AddScoped<IValidator<GetProductMarginAnalysisRequest>, GetProductMarginAnalysisRequestValidator>();

        // Register MediatR validation pipeline behavior for Analytics requests
        services.AddScoped<IPipelineBehavior<GetMarginReportRequest, GetMarginReportResponse>,
            ValidationResultBehavior<GetMarginReportRequest, GetMarginReportResponse>>();
        services.AddScoped<IPipelineBehavior<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>,
            ValidationResultBehavior<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>>();

        services.AddScoped<IMarginCalculator, MarginCalculator>();
        services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();

        // Register dashboard tiles
        services.RegisterTile<InvoiceImportStatisticsTile>();

        return services;
    }
}