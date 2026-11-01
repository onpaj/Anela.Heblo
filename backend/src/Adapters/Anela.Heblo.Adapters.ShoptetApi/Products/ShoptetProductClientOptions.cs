namespace Anela.Heblo.Adapters.ShoptetApi.Products;

public class ShoptetProductClientOptions
{
    public const string SettingsKey = "ShoptetProductClient";

    /// <summary>Minimum delay between the starts of two consecutive requests (Shoptet rate limit).</summary>
    public int MinRequestIntervalMs { get; set; } = 250;

    public int MaxRetryAttempts { get; set; } = 4;

    public int RetryBaseDelaySeconds { get; set; } = 2;

    public int TimeoutSeconds { get; set; } = 15;
}
