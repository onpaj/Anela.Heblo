using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.MetaAds;

public static class MetaAdsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddMetaAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MetaAdsSettings>(configuration.GetSection(MetaAdsSettings.ConfigurationKey));
        services.AddHttpClient<MetaAdsTransactionSource>();
        services.AddScoped<IRecurringJob, MetaAdsInvoiceImportJob>();
        return services;
    }
}
