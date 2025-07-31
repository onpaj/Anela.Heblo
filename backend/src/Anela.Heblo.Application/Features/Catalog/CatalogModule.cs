using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.Domain.Catalog.Stock;
using Anela.Heblo.Application.Domain.Logistics.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.features.catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        services.AddTransient<ICatalogRepository, CatalogRepository>();
        // Register any catalog-specific services here if needed

        services.AddTransient<ITransportBoxRepository, EmptyTransportBoxRepository>();
        services.AddTransient<IStockTakingRepository, EmptyStockTakingRepository>();

        // Register background service for periodic refresh operations
        services.AddHostedService<CatalogRefreshBackgroundService>();

        // Register AutoMapper for catalog mappings
        services.AddAutoMapper(typeof(CatalogModule));

        return services;
    }
}