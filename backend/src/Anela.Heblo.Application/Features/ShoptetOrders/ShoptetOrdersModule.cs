using Anela.Heblo.Application.Features.Packaging.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ShoptetOrders;

public static class ShoptetOrdersModule
{
    public static IServiceCollection AddShoptetOrdersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ShoptetOrdersSettings>(
            configuration.GetSection(ShoptetOrdersSettings.ConfigurationKey));

        // Cross-module contract: ShoptetOrders implements Packaging's IPackedOrderStatusUpdater via
        // adapter. DI registration is owned by the provider (ShoptetOrders), not the consumer
        // (Packaging). Lifetime mirrors IEshopOrderClient's own Transient registration
        // (ShoptetApiAdapterServiceCollectionExtensions).
        services.AddTransient<IPackedOrderStatusUpdater, ShoptetOrdersPackedOrderStatusUpdaterAdapter>();

        return services;
    }
}
