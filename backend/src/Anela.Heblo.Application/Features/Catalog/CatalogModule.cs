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
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Application.Features.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment? environment = null)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        // Register catalog repository - use mock only for Test environment (testing)
        // Real repository for Development, Test, and Production environments
        var environmentName = environment?.EnvironmentName ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        if (environmentName == "Test")
        {
            services.AddTransient<ICatalogRepository, MockCatalogRepository>();
        }
        else
        {
            services.AddTransient<ICatalogRepository, CatalogRepository>();
        }
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
            options.IsBackgroundRefreshEnabled = environmentName != "Test";
        });

        // Register repositories based on feature flags
        // For now, using empty implementations until features are fully implemented
        // TODO: Replace with real implementations when features are ready
        services.AddTransient<ITransportBoxRepository, EmptyTransportBoxRepository>();
        services.AddTransient<IStockTakingRepository, EmptyStockTakingRepository>();

        // Register background service for periodic refresh operations
        // Use feature flags to control when background services are enabled
        if (environmentName != "Test") // Keep existing behavior for compatibility
        {
            services.AddHostedService<CatalogRefreshBackgroundService>();
        }

        // Configure catalog repository options from configuration
        services.Configure<CatalogRepositoryOptions>(options =>
        {
            configuration.GetSection(CatalogRepositoryOptions.ConfigKey).Bind(options);
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