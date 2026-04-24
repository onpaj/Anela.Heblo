namespace Anela.Heblo.Adapters.MetaAds;

public class MetaAdsSettings
{
    public const string ConfigurationKey = "MetaAds";

    /// <summary>Ad account ID in the form "act_123456789".</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>System User token from Meta Business Manager. Store in secrets.json / Key Vault.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Graph API version, e.g. "v21.0".</summary>
    public string ApiVersion { get; set; } = "v21.0";
}
