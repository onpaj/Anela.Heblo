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
    /// mistyped Key Vault secret can never stop the app from booting; a half-configured set logs a
    /// startup warning naming the unusable settings. Nothing here calls Google.
    /// The developer token is not required (Google sunset developer tokens on 2026-09-09).
    /// </summary>
    public static IServiceCollection AddGoogleAdsMarketingAds(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(GoogleAdsSettings.ConfigurationKey);
        var settings = section.Get<GoogleAdsSettings>() ?? new GoogleAdsSettings();
        if (!IsConfigured(settings))
        {
            var misconfiguration = DescribeMisconfiguration(settings);
            if (misconfiguration is not null)
                Console.Error.WriteLine($"[GoogleAds] {misconfiguration}");
            return services;
        }

        services.Configure<GoogleAdsSettings>(section);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(GoogleAdsRestClient.HttpClientName, client => client.Timeout = ApiTimeout);
        services.AddHttpClient(GoogleAdsOAuthTokenProvider.HttpClientName, client => client.Timeout = OAuthTimeout);
        services.AddSingleton<IGoogleAdsAccessTokenProvider, GoogleAdsOAuthTokenProvider>();
        services.AddSingleton<IGoogleAdsApiClient, GoogleAdsRestClient>();
        services.AddScoped<IAdPlatformReadSource, GoogleAdsReadSource>();
        return services;
    }

    /// <summary>
    /// A warning for a half-configured environment (some values real, others blank, placeholder or
    /// malformed), which usually means a mistyped Key Vault secret. Names the settings, never their
    /// values. Null when nothing is configured at all, or everything is.
    /// </summary>
    internal static string? DescribeMisconfiguration(GoogleAdsSettings settings)
    {
        var credentials = new (string Name, string Value)[]
        {
            (nameof(GoogleAdsSettings.CustomerId), settings.CustomerId),
            (nameof(GoogleAdsSettings.OAuth2ClientId), settings.OAuth2ClientId),
            (nameof(GoogleAdsSettings.OAuth2ClientSecret), settings.OAuth2ClientSecret),
            (nameof(GoogleAdsSettings.OAuth2RefreshToken), settings.OAuth2RefreshToken),
        };
        if (!credentials.Any(c => AdSettingsGuard.IsConfigured(c.Value)) || IsConfigured(settings))
            return null;

        var unusable = credentials
            .Where(c => !AdSettingsGuard.IsConfigured(c.Value)
                        || (c.Name == nameof(GoogleAdsSettings.CustomerId) && !IsNumericCustomerId(c.Value)))
            .Select(c => c.Name)
            .ToList();
        var loginCustomerId = GoogleAdsIds.NormalizeCustomerId(settings.LoginCustomerId);
        if (loginCustomerId.Length > 0 && !GoogleAdsIds.IsNumericId(loginCustomerId))
            unusable.Add(nameof(GoogleAdsSettings.LoginCustomerId));

        return $"Google Ads is partly configured; the read source stays unregistered. Unusable settings: "
               + $"{string.Join(", ", unusable)}. Check the Key Vault secrets GoogleAds--<name>.";
    }

    private static bool IsNumericCustomerId(string customerId) =>
        GoogleAdsIds.IsNumericId(GoogleAdsIds.NormalizeCustomerId(customerId));

    internal static bool IsConfigured(GoogleAdsSettings settings)
    {
        var loginCustomerId = GoogleAdsIds.NormalizeCustomerId(settings.LoginCustomerId);
        return AdSettingsGuard.IsConfigured(
                   settings.CustomerId, settings.OAuth2ClientId, settings.OAuth2ClientSecret, settings.OAuth2RefreshToken)
               && IsNumericCustomerId(settings.CustomerId)
               && (loginCustomerId.Length == 0 || GoogleAdsIds.IsNumericId(loginCustomerId));
    }
}
