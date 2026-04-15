namespace Anela.Heblo.Adapters.GoogleAds;

public class GoogleAdsSettings
{
    public const string ConfigurationKey = "GoogleAds";

    /// <summary>Google Ads customer ID (no dashes), e.g. "1234567890".</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>Developer token from Google Ads API Center. Store in secrets.json / Key Vault.</summary>
    public string DeveloperToken { get; set; } = string.Empty;

    /// <summary>OAuth2 client ID from GCP project. Store in secrets.json / Key Vault.</summary>
    public string OAuth2ClientId { get; set; } = string.Empty;

    /// <summary>OAuth2 client secret from GCP project. Store in secrets.json / Key Vault.</summary>
    public string OAuth2ClientSecret { get; set; } = string.Empty;

    /// <summary>OAuth2 refresh token for the Google Ads account. Store in secrets.json / Key Vault.</summary>
    public string OAuth2RefreshToken { get; set; } = string.Empty;
}
