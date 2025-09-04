using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Configuration;
using Anela.Heblo.Application.Features.Audit;
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Purchase;
using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Application.Features.Journal;
using Anela.Heblo.Application.Features.Manufacture;
using Anela.Heblo.Application.Features.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Application;

/// <summary>
/// Main application module registration
/// </summary>
public static class ApplicationModule
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment? environment = null)
    {
        // Register MediatR
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationModule).Assembly));

        // Register AutoMapper
        services.AddAutoMapper(typeof(ApplicationModule).Assembly);

        // Register common services
        services.AddSingleton<IBackgroundServiceReadinessTracker, BackgroundServiceReadinessTracker>();

        // Register all feature modules
        services.AddConfigurationModule();
        services.AddAuditModule();
        services.AddAnalyticsModule();
        services.AddCatalogModule(configuration);
        services.AddPurchaseModule();
        services.AddFinancialOverviewModule(configuration);
        services.AddJournalModule();
        services.AddManufactureModule(configuration);
        services.AddTransportModule();
        // services.AddOrdersModule();
        // services.AddInvoicesModule();

        return services;
    }
}