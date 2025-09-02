using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Catalog.Fakes;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Catalog.Validators;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence.Repository;
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

        // Register catalog-specific services
        services.AddTransient<IManufactureCostCalculationService, ManufactureCostCalculationService>();
        services.AddSingleton<ICatalogResilienceService, CatalogResilienceService>();
        services.AddTransient<SafeMarginCalculator>();

        // Configure feature flags from configuration
        services.Configure<CatalogFeatureFlags>(options =>
        {
            // Default values - can be overridden by configuration
            options.IsTransportBoxTrackingEnabled = false;
            options.IsStockTakingEnabled = false;
            options.IsBackgroundRefreshEnabled = true;
        });

        // Register repositories based on feature flags
        // For now, using empty implementations until features are fully implemented
        // TODO: Replace with real implementations when features are ready
        services.AddTransient<ITransportBoxRepository, EmptyTransportBoxRepository>();
        services.AddTransient<IStockTakingRepository, EmptyStockTakingRepository>();

        // Register background service for periodic refresh operations
        // Tests can configure hosted services separately
        services.AddHostedService<CatalogRefreshBackgroundService>();

        // Configure catalog repository options from configuration
        services.Configure<DataSourceOptions>(options =>
        {
            configuration.GetSection(DataSourceOptions.ConfigKey).Bind(options);
        });

        // Register AutoMapper for catalog mappings
        services.AddAutoMapper(typeof(CatalogModule));

        // Register FluentValidation validators for catalog requests
        services.AddScoped<IValidator<GetCatalogDetailRequest>, GetCatalogDetailRequestValidator>();

        // Register MediatR validation behavior only for catalog requests
        services.AddScoped<IPipelineBehavior<GetCatalogDetailRequest, GetCatalogDetailResponse>, ValidationBehavior<GetCatalogDetailRequest, GetCatalogDetailResponse>>();

        return services;
    }
}