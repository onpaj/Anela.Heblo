using Anela.Heblo.Application.Features.Analytics.Domain;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Analytics;

/// <summary>
/// 🔒 PERFORMANCE FIX: Enhanced analytics module with streaming architecture
/// Registers new calculators and repository for memory-efficient processing
/// </summary>
public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        // 🔒 PERFORMANCE FIX: Register new streaming repository
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

        // 🔒 PERFORMANCE FIX: Register extracted calculators (single responsibility)
        services.AddScoped<MarginCalculator>();
        services.AddScoped<MonthlyBreakdownGenerator>();

        // Keep existing service for time window parsing
        services.AddTransient<IProductMarginAnalysisService, ProductMarginAnalysisService>();

        return services;
    }
}