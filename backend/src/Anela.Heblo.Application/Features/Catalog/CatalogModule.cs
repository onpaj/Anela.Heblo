using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Common.Behaviors;
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
        services.AddTransient<IMaterialCostRepository, CatalogMaterialCostRepository>();
        services.AddTransient<IManufactureCostRepository, ManufactureCostRepository>();
        services.AddTransient<ISalesCostRepository, SalesCostRepository>();
        services.AddTransient<IOverheadCostRepository, OverheadCostRepository>();

        // Register catalog-specific services
        services.AddSingleton<IManufactureCostCalculationService, ManufactureCostCalculationService>();
        services.AddTransient<ISalesCostCalculationService, SalesCostCalculationService>();
        services.AddTransient<IMarginCalculationService, MarginCalculationService>();
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

        // Register background service for periodic refresh operations
        // Tests can configure hosted services separately
        services.AddHostedService<CatalogRefreshBackgroundService>();

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

        return services;
    }
}