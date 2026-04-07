using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;

namespace Anela.Heblo.Adapters.ShoptetApi;

public static class ShoptetApiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddShoptetApiAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        services.AddOptions<ShoptetApiSettings>()
            .Bind(configuration.GetSection(ShoptetApiSettings.ConfigurationKey));

        services.AddHttpClient<IEshopOrderClient, ShoptetOrderClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
        });

        services.AddTransient<IPickingListSource, ShoptetApiExpeditionListSource>();

        return services;
    }
}
