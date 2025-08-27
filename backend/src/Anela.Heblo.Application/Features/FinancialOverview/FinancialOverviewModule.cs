using Anela.Heblo.Domain.Features.FinancialOverview;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public static class FinancialOverviewModule
{
    public static IServiceCollection AddFinancialOverviewModule(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration, IHostEnvironment? environment = null)
    {
        // Ensure memory cache is available for financial analysis caching
        services.AddMemoryCache();

        // Register IStockValueService using factory pattern to avoid ServiceProvider antipattern
        // Uses environment-specific implementations:
        // - Test/Automation environments: PlaceholderStockValueService (no external dependencies)
        // - Production/Development: StockValueService (real ERP integration)
        services.AddScoped<IStockValueService>(provider =>
        {
            var env = provider.GetRequiredService<IHostEnvironment>();

            if (env.EnvironmentName == "Test" || env.EnvironmentName == "Automation")
            {
                // PlaceholderStockValueService provides deterministic empty data for consistent testing
                // This avoids external ERP dependencies during automated testing
                var logger = provider.GetRequiredService<ILogger<PlaceholderStockValueService>>();
                return new PlaceholderStockValueService(logger);
            }
            else
            {
                // StockValueService provides real stock data integration with ERP systems
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

        // Configure financial analysis options from configuration
        services.Configure<FinancialAnalysisOptions>(options =>
        {
            configuration.GetSection(FinancialAnalysisOptions.ConfigKey).Bind(options);
        });

        return services;
    }
}