using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Common.TimePeriods;
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.Configuration;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.GridLayouts;
using Anela.Heblo.Application.Features.MarketingInvoices;
using Anela.Heblo.Application.Features.BackgroundJobs;
using Anela.Heblo.Application.Features.Bank;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Inventory;
using Anela.Heblo.Application.Features.Dashboard;
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.InvoiceClassification;
using Anela.Heblo.Application.Features.Invoices;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.CatalogDocuments;
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.Article;
using Anela.Heblo.Application.Features.Leaflet;
using Anela.Heblo.Application.Features.MeetingTasks;
using Anela.Heblo.Application.Features.Purchase;
using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Application.Features.Journal;
using Anela.Heblo.Application.Features.Marketing;
using Anela.Heblo.Application.Features.Logistics;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;
using Anela.Heblo.Application.Features.Manufacture;
using Anela.Heblo.Application.Features.OrgChart;
using Anela.Heblo.Application.Features.PackingMaterials;
using Anela.Heblo.Application.Features.CarrierCooling;
using Anela.Heblo.Application.Features.GiftSettings;
using Anela.Heblo.Application.Features.WeatherForecast;
using Anela.Heblo.Application.Features.DataQuality;
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Application.Features.Smartsupp;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.Packaging;
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
        // Register shared RAG infrastructure
        services.AddSharedRagModule();

        // Register MediatR
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationModule).Assembly));

        // Register AutoMapper
        services.AddAutoMapper(cfg => { }, typeof(ApplicationModule).Assembly);

        // Register shared time period services
        services.AddScoped<ITimePeriodResolver, TimePeriodResolver>();

        // Background refresh system, hydration, and service readiness tracking are handled by XCC module

        // Register all feature modules
        services.AddConfigurationModule();
        services.AddAnalyticsModule();
        services.AddBackgroundJobsModule();
        services.AddBankModule(configuration);
        services.AddCatalogModule(configuration);
        services.AddDashboardModule();
        services.AddFileStorageModule(configuration);
        services.AddPurchaseModule();
        services.AddFinancialOverviewModule(configuration);
        services.AddJournalModule();
        services.AddMarketingModule(configuration);
        services.AddManufactureModule(configuration);
        services.AddTransportModule();
        services.AddGiftPackageManufactureModule();
        services.AddUserManagement(configuration);
        services.AddOrgChartServices(configuration);
        services.AddInvoiceClassificationModule();
        services.AddPackingMaterialsModule();
        services.AddInvoicesModule();
        services.AddKnowledgeBaseModule(configuration);
        services.AddCatalogDocumentsModule(configuration);
        services.AddLeafletModule(configuration);
        services.AddArticleModule(configuration);
        services.AddExpeditionListModule(configuration);
        services.AddExpeditionListArchiveModule();
        services.AddShoptetOrdersModule(configuration);
        services.AddShipmentLabelsModule(configuration);
        services.AddPackagingModule();
        services.AddGridLayoutsModule();
        services.AddMarketingInvoicesModule();
        services.AddCarrierCoolingModule();
        services.AddGiftSettingsModule();
        services.AddWeatherForecastModule();
        services.AddDataQualityModule();
        services.AddPhotobankModule(configuration);
        services.AddMeetingTasksModule(configuration);
        services.AddSmartsuppModule(configuration);
        services.AddInventoryModule();
        // services.AddOrdersModule();

        services.AddFeatureFlagsModule(configuration);

        return services;
    }
}