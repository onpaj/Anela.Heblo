namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

public class ShoptetApiSettings
{
    public static string ConfigurationKey => "Shoptet";

    public bool IsTestEnvironment { get; set; } = false;
    public string BaseUrl { get; set; } = "https://api.myshoptet.com";
    public string ApiToken { get; set; } = null!;
    public Dictionary<string, string> ShippingGuidMap { get; set; } = new();
    public string PaymentMethodGuid { get; set; } = null!;

    /// <summary>
    /// Shoptet warehouse ID used for stock movements. Discover via GET /api/stocks (returns defaultStockId).
    /// Most single-warehouse stores use id 1. Configure per environment in user secrets: Shoptet:StockId
    /// </summary>
    public int StockId { get; set; } = 1;
}
