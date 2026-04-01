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

        return services;
    }
}
