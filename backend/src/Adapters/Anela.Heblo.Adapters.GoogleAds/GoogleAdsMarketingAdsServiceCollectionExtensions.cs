using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Adapters.GoogleAds;

public static class GoogleAdsMarketingAdsServiceCollectionExtensions
{
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(100);
    private static readonly TimeSpan OAuthTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers the Google Ads platform for the MarketingAds backbone, but only when the account and
    /// OAuth credentials are really configured: AdSettingsGuard rejects blanks, "-- stored in … --" and
    /// "XXX" placeholders. Otherwise nothing is registered and nothing throws, so a missing or
    /// mistyped Key Vault secret can never stop the app from booting. Nothing here calls Google.
    /// The developer token is not required (Google sunset developer tokens on 2026-09-09).
    /// </summary>
    public static IServiceCollection AddGoogleAdsMarketingAds(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(GoogleAdsSettings.ConfigurationKey);
        var settings = section.Get<GoogleAdsSettings>() ?? new GoogleAdsSettings();
        if (!IsConfigured(settings))
            return services;

        services.Configure<GoogleAdsSettings>(section);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(GoogleAdsRestClient.HttpClientName, client => client.Timeout = ApiTimeout);
        services.AddHttpClient(GoogleAdsOAuthTokenProvider.HttpClientName, client => client.Timeout = OAuthTimeout);
        services.AddSingleton<IGoogleAdsAccessTokenProvider, GoogleAdsOAuthTokenProvider>();
        services.AddSingleton<IGoogleAdsApiClient, GoogleAdsRestClient>();
        services.AddScoped<IAdPlatformReadSource, GoogleAdsReadSource>();
        return services;
    }

    internal static bool IsConfigured(GoogleAdsSettings settings)
    {
        var loginCustomerId = GoogleAdsIds.NormalizeCustomerId(settings.LoginCustomerId);
        return AdSettingsGuard.IsConfigured(
                   settings.CustomerId, settings.OAuth2ClientId, settings.OAuth2ClientSecret, settings.OAuth2RefreshToken)
               && GoogleAdsIds.IsNumericId(GoogleAdsIds.NormalizeCustomerId(settings.CustomerId))
               && (loginCustomerId.Length == 0 || GoogleAdsIds.IsNumericId(loginCustomerId));
    }
}
