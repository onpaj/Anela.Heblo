using Anela.Heblo.Adapters.GoogleAds;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal static class TestSettings
{
    public const string CustomerId = "1234567890";
    public const string OAuthClientId = "client-id.apps.googleusercontent.com";
    public const string OAuthClientSecret = "client-secret-value";
    public const string RefreshToken = "refresh-token-value";
    public const string HebloUserEmail = "heblo-ads@anela.cz";
    public static readonly DateTimeOffset Now = new(2026, 10, 7, 6, 0, 0, TimeSpan.Zero);

    public static GoogleAdsSettings Create(Action<GoogleAdsSettings>? configure = null)
    {
        var settings = new GoogleAdsSettings
        {
            CustomerId = "123-456-7890",
            OAuth2ClientId = OAuthClientId,
            OAuth2ClientSecret = OAuthClientSecret,
            OAuth2RefreshToken = RefreshToken,
            ApiVersion = "v25",
            HebloUserEmail = HebloUserEmail,
        };
        configure?.Invoke(settings);
        return settings;
    }
}
