using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.MetaAds;

public static class MetaAdsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddMetaAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MetaAdsSettings>(configuration.GetSection(MetaAdsSettings.ConfigurationKey));
        services.AddHttpClient<MetaAdsTransactionSource>();
        services.AddScoped<IMarketingTransactionSource>(sp =>
            sp.GetRequiredService<MetaAdsTransactionSource>());
        services.AddScoped<IRecurringJob, MetaAdsInvoiceImportJob>();
        return services;
    }
}
