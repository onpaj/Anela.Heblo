using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.MetaAds;

public static class MetaAdsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddMetaAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MetaAdsSettings>()
            .Bind(configuration.GetSection(MetaAdsSettings.ConfigKey));

        services.AddTransient<MetaTokenRefreshHandler>();

        services.AddHttpClient<IMetaAdsClient, MetaAdsClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<MetaAdsSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
        })
        .AddHttpMessageHandler<MetaTokenRefreshHandler>();

        return services;
    }
}
