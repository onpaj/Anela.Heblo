using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Features.Analytics.Validators;
using Anela.Heblo.Domain.Features.Analytics;
using FluentValidation;
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

        // Register repository
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

        // Register refactored services for clean separation of concerns
        // Note: IMarginCalculationService is registered by CatalogModule and injected here
        services.AddScoped<Analytics.Services.IMarginCalculationService, Analytics.Services.MarginCalculationService>();
        services.AddScoped<IProductFilterService, ProductFilterService>();
        services.AddScoped<IReportBuilderService, ReportBuilderService>();

        // Register validators for FluentValidation
        services.AddScoped<IValidator<GetMarginReportRequest>, GetMarginReportRequestValidator>();
        services.AddScoped<IValidator<GetProductMarginAnalysisRequest>, GetProductMarginAnalysisRequestValidator>();

        // Legacy services (keeping for backward compatibility)
        services.AddScoped<MarginCalculator>();
        services.AddScoped<MonthlyBreakdownGenerator>();
        services.AddTransient<IProductMarginAnalysisService, ProductMarginAnalysisService>();

        return services;
    }
}