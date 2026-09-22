namespace Anela.Heblo.Adapters.GoogleAnalytics;

/// <summary>
/// Credentials for the GA4 Data API. Both values come from Key Vault
/// (<c>GoogleAnalytics--PropertyId</c>, <c>GoogleAnalytics--CredentialsJson</c>) — never from
/// App Service settings. When either is blank the whole GA4 stack stays unregistered.
/// </summary>
public class Ga4Options
{
    public const string ConfigurationKey = "GoogleAnalytics";

    /// <summary>Numeric GA4 property id, e.g. "392098710". The API path is <c>properties/{id}</c>.</summary>
    public string PropertyId { get; set; } = string.Empty;

    /// <summary>The service account key, as the raw JSON document.</summary>
    public string CredentialsJson { get; set; } = string.Empty;

    /// <summary>
    /// The credential has to look like the JSON document it is, not merely be non-blank. A
    /// human-readable placeholder in appsettings would otherwise pass a whitespace-only check and
    /// register the whole GA4 stack against a credential that cannot possibly parse.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PropertyId) &&
        CredentialsJson.AsSpan().TrimStart().StartsWith("{");
}
