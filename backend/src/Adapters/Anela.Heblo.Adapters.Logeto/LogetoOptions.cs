namespace Anela.Heblo.Adapters.Logeto;

public class LogetoOptions
{
    public const string ConfigKey = "Logeto";

    /// <summary>Account subdomain: https://{AccountName}.logeto.com. Empty = adapter unconfigured.</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>API key sent in the AccessKey header. Comes from Key Vault (Logeto--AccessKey).</summary>
    public string AccessKey { get; set; } = string.Empty;

    public int RetryCount { get; set; } = 3;
    public int RequestTimeoutSeconds { get; set; } = 30;
}
