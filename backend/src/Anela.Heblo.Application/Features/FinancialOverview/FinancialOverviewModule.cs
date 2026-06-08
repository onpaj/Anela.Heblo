using Anela.Heblo.Application.Features.FinancialOverview.Services;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Anela.Heblo.Domain.Features.FinancialOverview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public static class FinancialOverviewModule
{
    public static IServiceCollection AddFinancialOverviewModule(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        // Ensure memory cache is available for financial analysis caching
        services.AddMemoryCache();

        services.AddScoped<IStockValueService, StockValueService>();

        // Register financial analysis service as scoped (uses IMemoryCache for caching)
        services.AddScoped<IFinancialAnalysisService, FinancialAnalysisService>();

        // Background refresh services are now handled by centralized BackgroundRefreshSchedulerService
        // Old FinancialAnalysisBackgroundService is replaced by refresh task
        RegisterBackgroundRefreshTasks(services);

        // Configure financial analysis options from configuration
        services.Configure<FinancialAnalysisOptions>(options =>
        {
            configuration.GetSection(FinancialAnalysisOptions.ConfigKey).Bind(options);
        });

        return services;
    }

    private static void RegisterBackgroundRefreshTasks(IServiceCollection services)
    {
        services.RegisterRefreshTask<IFinancialAnalysisService>(
            nameof(IFinancialAnalysisService.RefreshFinancialDataAsync),
            (s, ct) => s.RefreshFinancialDataAsync(null, null, ct)
        );
    }
}