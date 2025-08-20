using Anela.Heblo.Domain.Features.FinancialOverview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public static class FinancialOverviewModule
{
    public static IServiceCollection AddFinancialOverviewModule(this IServiceCollection services, IHostEnvironment? environment = null)
    {
        // Ensure memory cache is available for financial analysis caching
        services.AddMemoryCache();

        // Register real implementation that uses IErpStockClient and IProductPriceErpClient
        services.AddScoped<IStockValueService, StockValueService>();

        // Register financial analysis service as scoped (uses IMemoryCache for caching)
        services.AddScoped<IFinancialAnalysisService, FinancialAnalysisService>();

        // Register background service for financial data caching
        var environmentName = environment?.EnvironmentName ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        // Skip background services in automation environment to avoid external service dependencies
        if (environmentName != "Automation")
        {
            services.AddHostedService<FinancialAnalysisBackgroundService>();
        }

        // Configure financial analysis options
        services.Configure<FinancialAnalysisOptions>(options => { });

        return services;
    }
}