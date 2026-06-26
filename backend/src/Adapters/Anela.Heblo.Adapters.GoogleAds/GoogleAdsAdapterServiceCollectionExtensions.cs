using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.GoogleAds;

public static class GoogleAdsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddGoogleAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GoogleAdsSettings>(configuration.GetSection(GoogleAdsSettings.ConfigurationKey));
        services.AddSingleton<IAccountBudgetFetcher, SdkAccountBudgetFetcher>();
        services.AddScoped<GoogleAdsTransactionSource>(sp =>
            new GoogleAdsTransactionSource(
                sp.GetRequiredService<IAccountBudgetFetcher>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GoogleAdsTransactionSource>>()));
        services.AddScoped<IMarketingTransactionSource>(sp =>
            sp.GetRequiredService<GoogleAdsTransactionSource>());
        services.AddScoped<IRecurringJob, GoogleAdsInvoiceImportJob>();
        return services;
    }
}
