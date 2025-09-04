using Anela.Heblo.Application.Features.FinancialOverview.Services;
using Anela.Heblo.Domain.Features.FinancialOverview;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public static class FinancialOverviewModule
{
    public static IServiceCollection AddFinancialOverviewModule(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        // Ensure memory cache is available for financial analysis caching
        services.AddMemoryCache();

        // Register default implementation - tests can override this
        services.AddScoped<IStockValueService>(provider =>
        {
            var stockClient = provider.GetRequiredService<IErpStockClient>();
            var priceClient = provider.GetRequiredService<IProductPriceErpClient>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StockValueService>>();
            return new StockValueService(stockClient, priceClient, logger);
        });

        // Register financial analysis service as scoped (uses IMemoryCache for caching)
        services.AddScoped<IFinancialAnalysisService, FinancialAnalysisService>();

        // Register background service for financial data caching
        // Tests can configure hosted services separately
        services.AddHostedService<FinancialAnalysisBackgroundService>();

        // Configure financial analysis options from configuration
        services.Configure<FinancialAnalysisOptions>(options =>
        {
            configuration.GetSection(FinancialAnalysisOptions.ConfigKey).Bind(options);
        });

        return services;
    }
}