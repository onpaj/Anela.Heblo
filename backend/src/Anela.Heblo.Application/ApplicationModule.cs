using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Configuration;
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Bank;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Dashboard;
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.InvoiceClassification;
using Anela.Heblo.Application.Features.Purchase;
using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Application.Features.Journal;
using Anela.Heblo.Application.Features.Logistics;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;
using Anela.Heblo.Application.Features.Manufacture;
using Anela.Heblo.Application.Features.OrgChart;
using Anela.Heblo.Application.Features.UserManagement;
using Anela.Heblo.Xcc.Services.Dashboard;
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

        // Register health check integration wrapper
        services.AddHostedService<HydrationOrchestratorWrapper>();

        // Background refresh system is now handled by XCC module

        // Register all feature modules
        services.AddConfigurationModule();
        services.AddAnalyticsModule();
        services.AddBankModule();
        services.AddCatalogModule(configuration);
        services.AddDashboardModule();
        services.AddFileStorageModule(configuration);
        services.AddPurchaseModule();
        services.AddFinancialOverviewModule(configuration);
        services.AddJournalModule();
        services.AddManufactureModule(configuration);
        services.AddTransportModule();
        services.AddGiftPackageManufactureModule();
        services.AddUserManagement(configuration);
        services.AddOrgChartServices(configuration);
        services.AddInvoiceClassificationModule();
        // services.AddOrdersModule();
        // services.AddInvoicesModule();

        return services;
    }
}