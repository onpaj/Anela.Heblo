using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Anela.Heblo.Persistence.Marketing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance;

public static class MarketingPerformanceModule
{
    public static IServiceCollection AddMarketingPerformanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MarketingPerformanceOptions>()
            .Bind(configuration.GetSection(MarketingPerformanceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MarketingPerformanceOptions>, MarketingPerformanceOptionsValidator>();

        services.AddScoped<IMarketingPerformanceRepository, MarketingPerformanceRepository>();
        services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();
        // No-op fallback; the Flexi adapter will override with the real received-invoice
        // implementation when it is registered (last registration wins).
        services.AddScoped<IMonthlyAdCostSource, NoOpMonthlyAdCostSource>();
        services.AddSingleton<MarketingPerformanceRunGuard>();
        services.AddScoped<IMarketingPerformanceRefreshService, MarketingPerformanceRefreshService>();
        services.AddScoped<MarketingPerformanceRecomputeJob>();
        services.AddScoped<IMarketingPerformanceRecomputeEnqueuer, HangfireMarketingPerformanceRecomputeEnqueuer>();
        // MediatR handlers and IRecurringJob implementations are discovered by assembly scan.
        return services;
    }
}
