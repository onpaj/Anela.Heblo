using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddScoped<IRecurringJob, GoogleAdsInvoiceImportJob>();
        return services;
    }
}
