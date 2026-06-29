using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.GoogleAds;

public static class GoogleAdsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddGoogleAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<GoogleAdsSettings>()
            .BindConfiguration(GoogleAdsSettings.ConfigurationKey)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Marketing invoice adapter (billing/account budgets)
        services.AddScoped<IAccountBudgetFetcher, SdkAccountBudgetFetcher>();
        services.AddScoped<GoogleAdsTransactionSource>(sp =>
            new GoogleAdsTransactionSource(
                sp.GetRequiredService<IAccountBudgetFetcher>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GoogleAdsTransactionSource>>()));
        services.AddScoped<IMarketingTransactionSource>(sp =>
            sp.GetRequiredService<GoogleAdsTransactionSource>());
        services.AddScoped<IRecurringJob, GoogleAdsInvoiceImportJob>();

        // Campaign performance adapter
        services.AddScoped<IGoogleAdsClient, GoogleAdsClientWrapper>();

        return services;
    }
}
