using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.ShoptetOrders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi;

public static class ShoptetApiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddShoptetApiAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ShoptetApiSettings>()
            .Bind(configuration.GetSection(ShoptetApiSettings.ConfigurationKey));

        services.AddHttpClient<IShoptetOrderClient, ShoptetOrderClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        });

        return services;
    }
}
