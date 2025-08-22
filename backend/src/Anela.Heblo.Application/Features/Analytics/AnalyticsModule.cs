using Anela.Heblo.Application.Features.Analytics.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Analytics;

public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        // Register analytics-specific services
        services.AddTransient<IProductMarginAnalysisService, ProductMarginAnalysisService>();

        return services;
    }
}