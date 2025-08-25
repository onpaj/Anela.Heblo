using Anela.Heblo.Domain.Features.FinancialOverview;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public static class FinancialOverviewModule
{
    public static IServiceCollection AddFinancialOverviewModule(this IServiceCollection services, IHostEnvironment? environment = null)
    {
        // Ensure memory cache is available for financial analysis caching
        services.AddMemoryCache();

        // Register IStockValueService using factory pattern to avoid ServiceProvider antipattern
        services.AddScoped<IStockValueService>(provider =>
        {
            var env = provider.GetRequiredService<IHostEnvironment>();
            
            if (env.IsEnvironment("Test") || env.IsEnvironment("Automation"))
            {
                // Use placeholder implementation for test environments
                var logger = provider.GetRequiredService<ILogger<PlaceholderStockValueService>>();
                return new PlaceholderStockValueService(logger);
            }
            else
            {
                // Use real implementation for production and development environments
                var stockClient = provider.GetRequiredService<IErpStockClient>();
                var priceClient = provider.GetRequiredService<IProductPriceErpClient>();
                var logger = provider.GetRequiredService<ILogger<StockValueService>>();
                return new StockValueService(stockClient, priceClient, logger);
            }
        });

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