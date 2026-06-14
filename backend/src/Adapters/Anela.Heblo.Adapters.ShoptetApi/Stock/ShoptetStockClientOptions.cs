namespace Anela.Heblo.Adapters.ShoptetApi.Stock;

public class ShoptetStockClientOptions
{
    public const string SettingsKey = "StockClient";

    public string Url { get; set; } = "http://";

    public int TimeoutSeconds { get; set; } = 8;

    public int MaxRetryAttempts { get; set; } = 3;

    public int RetryBaseDelaySeconds { get; set; } = 1;
}
