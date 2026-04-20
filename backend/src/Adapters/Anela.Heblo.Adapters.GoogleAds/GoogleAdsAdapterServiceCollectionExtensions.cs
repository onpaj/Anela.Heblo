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
        services.AddScoped<IAccountBudgetFetcher, SdkAccountBudgetFetcher>();
        services.AddScoped<GoogleAdsTransactionSource>(sp =>
            new GoogleAdsTransactionSource(
                sp.GetRequiredService<IAccountBudgetFetcher>(),
                sp.GetRequiredService<ILogger<GoogleAdsTransactionSource>>()));
        services.AddScoped<MarketingInvoiceImportService>(sp => new MarketingInvoiceImportService(
            sp.GetRequiredService<GoogleAdsTransactionSource>(),
            sp.GetRequiredService<IImportedMarketingTransactionRepository>(),
            sp.GetRequiredService<ILogger<MarketingInvoiceImportService>>()));
        services.AddScoped<IRecurringJob, GoogleAdsInvoiceImportJob>();
        return services;
    }
}
