using Anela.Heblo.Application.Features.Catalog.Fakes;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Application.Features.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IHostEnvironment? environment = null)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        // Register catalog repository - use mock only for Automation environment (testing)
        // Real repository for Development, Test, and Production environments
        var environmentName = environment?.EnvironmentName ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        if (environmentName == "Automation")
        {
            services.AddTransient<ICatalogRepository, MockCatalogRepository>();
        }
        else
        {
            services.AddTransient<ICatalogRepository, CatalogRepository>();
        }
        // Register catalog-specific services
        services.AddTransient<IManufactureCostCalculationService, ManufactureCostCalculationService>();

        services.AddTransient<ITransportBoxRepository, EmptyTransportBoxRepository>();
        services.AddTransient<IStockTakingRepository, EmptyStockTakingRepository>();

        // Register background service for periodic refresh operations
        // Skip background services only in Automation environment to avoid external service dependencies during testing
        if (environmentName != "Automation")
        {
            services.AddHostedService<CatalogRefreshBackgroundService>();
        }

        // Configure catalog repository options
        services.Configure<CatalogRepositoryOptions>(options => { });

        // Register AutoMapper for catalog mappings
        services.AddAutoMapper(typeof(CatalogModule));

        return services;
    }
}