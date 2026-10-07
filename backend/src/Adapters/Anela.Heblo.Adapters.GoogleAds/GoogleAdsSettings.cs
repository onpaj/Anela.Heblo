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

    /// <summary>
    /// Manager (MCC) customer id sent as the <c>login-customer-id</c> header when Heblo's Google user
    /// reaches the account through a manager. Empty = direct access, header omitted. Used by the
    /// MarketingAds read source and executor only; the billing fetcher keeps its own behaviour.
    /// </summary>
    public string LoginCustomerId { get; set; } = string.Empty;

    /// <summary>Google Ads REST API major version for the MarketingAds read source and executor.</summary>
    public string ApiVersion { get; set; } = "v25";

    /// <summary>
    /// Email of the Google user whose refresh token Heblo uses. change_event rows that this user made
    /// through the API are attributed to Heblo rather than to a person.
    /// </summary>
    public string HebloUserEmail { get; set; } = string.Empty;
}
