using System.Net.Http.Headers;
using Anela.Heblo.Adapters.ShoptetApi.ShoptetPay;
using Anela.Heblo.Domain.Features.Bank;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi;

public static class ShoptetApiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddShoptetApiAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ShoptetPaySettings>()
            .Bind(configuration.GetSection(ShoptetPaySettings.ConfigurationKey))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ShoptetPayBankClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetPaySettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiToken);
        });

        services.AddTransient<IBankClient>(sp => sp.GetRequiredService<ShoptetPayBankClient>());

        return services;
    }
}
