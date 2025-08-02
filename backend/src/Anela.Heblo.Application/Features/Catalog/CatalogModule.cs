using Anela.Heblo.Application.Features.Catalog.Fakes;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Catalog;

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

        // Configure catalog repository options
        services.Configure<CatalogRepositoryOptions>(options => { });

        // Register AutoMapper for catalog mappings
        services.AddAutoMapper(typeof(CatalogModule));

        return services;
    }
}