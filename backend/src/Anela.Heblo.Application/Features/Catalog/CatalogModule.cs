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

        // Register catalog repository - use mock for Automation environment
        var environmentName = environment?.EnvironmentName ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        if (environmentName == "Automation")
        {
            services.AddTransient<ICatalogRepository, MockCatalogRepository>();
        }
        else
        {
            services.AddTransient<ICatalogRepository, CatalogRepository>();
        }
        // Register any catalog-specific services here if needed

        services.AddTransient<ITransportBoxRepository, EmptyTransportBoxRepository>();
        services.AddTransient<IStockTakingRepository, EmptyStockTakingRepository>();

        // Register background service for periodic refresh operations
        // Skip background services in automation environment to avoid external service dependencies
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