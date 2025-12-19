using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Catalog.DashboardTiles;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Repositories;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetManufactureDifficultySettings;
using Anela.Heblo.Application.Features.Catalog.UseCases.RecalculateProductWeight;
using Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking;
using Anela.Heblo.Application.Features.Catalog.UseCases.UpdateManufactureDifficulty;
using Anela.Heblo.Application.Features.Catalog.Validators;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence.Catalog.ManufactureDifficulty;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        // Register default implementations - tests can override these
        services.AddTransient<ICatalogRepository, CatalogRepository>();
        services.AddTransient<IManufactureDifficultyRepository, ManufactureDifficultyRepository>();

        // Register cost repositories
        //services.AddTransient<IMaterialCostRepository, CatalogMaterialCostRepository>();
        services.AddTransient<IMaterialCostRepository, PurchasePriceOnlyMaterialCostRepository>(); // Use purchase priceonly, till stock price is correct
        services.AddTransient<IManufactureCostRepository, ManufactureCostRepository>();

        // Register catalog-specific services
        services.AddScoped<IManufactureCostCalculationService, ManufactureCostCalculationService>();
        services.AddScoped<ISalesCostCalculationService, SalesCostCalculationService>();
        services.AddScoped<IOverheadCostCalculationService, OverheadCostCalculationService>();
        services.AddScoped<IMarginCalculationService, MarginCalculationService>();
        services.AddSingleton<ICatalogResilienceService, CatalogResilienceService>();
        services.AddSingleton<ICatalogMergeScheduler, CatalogMergeScheduler>();
        services.AddTransient<SafeMarginCalculator>();
        services.AddTransient<IProductWeightRecalculationService, ProductWeightRecalculationService>();

        // Configure feature flags from configuration
        services.Configure<CatalogFeatureFlags>(options =>
        {
            // Default values - can be overridden by configuration
            options.IsTransportBoxTrackingEnabled = false;
            options.IsStockTakingEnabled = false;
            options.IsBackgroundRefreshEnabled = true;
        });

        // Background refresh services are now handled by centralized BackgroundRefreshSchedulerService
        // Old CatalogRefreshBackgroundService is replaced by individual refresh tasks

        // Configure catalog repository options from configuration
        services.Configure<DataSourceOptions>(options =>
        {
            configuration.GetSection(DataSourceOptions.ConfigKey).Bind(options);
        });

        // Configure catalog cache optimization options
        services.Configure<CatalogCacheOptions>(options =>
        {
            configuration.GetSection(CatalogCacheOptions.SectionName).Bind(options);
        });

        // Register AutoMapper for catalog mappings
        services.AddAutoMapper(typeof(CatalogModule));

        // Register FluentValidation validators for catalog requests
        services.AddScoped<IValidator<GetCatalogDetailRequest>, GetCatalogDetailRequestValidator>();
        services.AddScoped<IValidator<CreateManufactureDifficultyRequest>, CreateManufactureDifficultyRequestValidator>();
        services.AddScoped<IValidator<UpdateManufactureDifficultyRequest>, UpdateManufactureDifficultyRequestValidator>();
        services.AddScoped<IValidator<GetManufactureDifficultySettingsRequest>, GetManufactureDifficultyHistoryRequestValidator>();
        services.AddScoped<IValidator<SubmitStockTakingRequest>, SubmitStockTakingRequestValidator>();
        services.AddScoped<IValidator<RecalculateProductWeightRequest>, RecalculateProductWeightRequestValidator>();

        // Register MediatR validation behavior only for catalog requests
        services.AddScoped<IPipelineBehavior<GetCatalogDetailRequest, GetCatalogDetailResponse>, ValidationBehavior<GetCatalogDetailRequest, GetCatalogDetailResponse>>();
        services.AddScoped<IPipelineBehavior<CreateManufactureDifficultyRequest, CreateManufactureDifficultyResponse>, ValidationBehavior<CreateManufactureDifficultyRequest, CreateManufactureDifficultyResponse>>();
        services.AddScoped<IPipelineBehavior<UpdateManufactureDifficultyRequest, UpdateManufactureDifficultyResponse>, ValidationBehavior<UpdateManufactureDifficultyRequest, UpdateManufactureDifficultyResponse>>();
        services.AddScoped<IPipelineBehavior<GetManufactureDifficultySettingsRequest, GetManufactureDifficultySettingsResponse>, ValidationBehavior<GetManufactureDifficultySettingsRequest, GetManufactureDifficultySettingsResponse>>();
        services.AddScoped<IPipelineBehavior<SubmitStockTakingRequest, SubmitStockTakingResponse>, ValidationBehavior<SubmitStockTakingRequest, SubmitStockTakingResponse>>();
        services.AddScoped<IPipelineBehavior<RecalculateProductWeightRequest, RecalculateProductWeightResponse>, ValidationBehavior<RecalculateProductWeightRequest, RecalculateProductWeightResponse>>();

        RegisterBackgroundRefreshTasks(services);

        // Register dashboard tiles
        services.RegisterTile<ProductInventoryCountTile>();
        services.RegisterTile<MaterialInventoryCountTile>();
        services.RegisterTile<ProductInventorySummaryTile>();
        services.RegisterTile<MaterialWithExpirationInventorySummaryTile>();
        services.RegisterTile<MaterialWithoutExpirationInventorySummaryTile>();
        services.RegisterTile<LowStockAlertTile>();

        return services;
    }

    private static void RegisterBackgroundRefreshTasks(IServiceCollection services)
    {
        // Catalog repository refresh tasks
        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshTransportData),
            (r, ct) => r.RefreshTransportData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshReserveData),
            (r, ct) => r.RefreshReserveData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshOrderedData),
            (r, ct) => r.RefreshOrderedData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshPlannedData),
            (r, ct) => r.RefreshPlannedData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshSalesData),
            (r, ct) => r.RefreshSalesData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshAttributesData),
            (r, ct) => r.RefreshAttributesData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshErpStockData),
            (r, ct) => r.RefreshErpStockData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshEshopStockData),
            (r, ct) => r.RefreshEshopStockData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshPurchaseHistoryData),
            (r, ct) => r.RefreshPurchaseHistoryData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshManufactureHistoryData),
            (r, ct) => r.RefreshManufactureHistoryData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshConsumedHistoryData),
            (r, ct) => r.RefreshConsumedHistoryData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshStockTakingData),
            (r, ct) => r.RefreshStockTakingData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshLotsData),
            (r, ct) => r.RefreshLotsData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshEshopPricesData),
            (r, ct) => r.RefreshEshopPricesData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshErpPricesData),
            (r, ct) => r.RefreshErpPricesData(ct)
        );

        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshManufactureDifficultySettingsData),
            (r, ct) => r.RefreshManufactureDifficultySettingsData(null, ct)
        );

        services.RegisterRefreshTask<ISalesCostCalculationService>(
            nameof(ISalesCostCalculationService.Reload),
            async (serviceProvider, ct) =>
            {
                var catalogRepository = serviceProvider.GetRequiredService<ICatalogRepository>();
                var costService = serviceProvider.GetRequiredService<ISalesCostCalculationService>();

                await catalogRepository.WaitForCurrentMergeAsync(ct);
                await costService.Reload();
            }
        );

        services.RegisterRefreshTask<IOverheadCostCalculationService>(
            nameof(IOverheadCostCalculationService.Reload),
            async (serviceProvider, ct) =>
            {
                var catalogRepository = serviceProvider.GetRequiredService<ICatalogRepository>();
                var costService = serviceProvider.GetRequiredService<IOverheadCostCalculationService>();

                await catalogRepository.WaitForCurrentMergeAsync(ct);
                await costService.Reload();
            }
        );

        // Manufacture cost calculation task (requires special logic)
        services.RegisterRefreshTask<IManufactureCostCalculationService>(
            nameof(IManufactureCostCalculationService.Reload),
            async (serviceProvider, ct) =>
            {
                var catalogRepository = serviceProvider.GetRequiredService<ICatalogRepository>();
                var manufactureCostService = serviceProvider.GetRequiredService<IManufactureCostCalculationService>();

                await catalogRepository.WaitForCurrentMergeAsync(ct);
                var catalogData = await catalogRepository.GetAllAsync(ct);
                await manufactureCostService.Reload(catalogData.ToList());
            }
        );

        // Margin calculation task
        services.RegisterRefreshTask(
            nameof(ICatalogRepository),
            "RefreshMarginData",
            async (sp, ct) =>
            {
                var catalogRepository = sp.GetRequiredService<ICatalogRepository>();
                var marginService = sp.GetRequiredService<IMarginCalculationService>();

                await catalogRepository.WaitForCurrentMergeAsync(ct);
                var products = await catalogRepository.GetAllAsync(ct);
                var twoYearsAgo = DateOnly.FromDateTime(DateTime.Now.AddYears(-2));
                var minDate = new DateOnly(2025, 1, 1); // No M2 margins available for older data
                var dateFrom = twoYearsAgo > minDate ? twoYearsAgo : minDate;
                var dateTo = DateOnly.FromDateTime(DateTime.Now).AddMonths(-1); // Current month is not accurate

                foreach (var product in products)
                {
                    product.Margins = await marginService.GetMarginAsync(product, products, dateFrom, dateTo, ct);
                }
            });
    }
}