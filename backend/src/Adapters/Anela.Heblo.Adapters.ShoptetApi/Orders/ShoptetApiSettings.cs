using Anela.Heblo.Domain.Features.Invoices;

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

    /// <summary>
    /// Maps shipping GUID strings → ShippingMethod enum for invoice import.
    /// Configure per environment in user secrets: Shoptet:InvoiceShippingGuidMap:{guid}
    /// </summary>
    public Dictionary<string, ShippingMethod> InvoiceShippingGuidMap { get; set; } = new();

    /// <summary>
    /// Fallback item weight in grams when the catalog has no GrossWeight or NetWeight for a product.
    /// Defaults to 0: a product with no known weight contributes nothing to the shipment weight,
    /// rather than inflating it (e.g. 50 pcs × 500 g = 25 kg on order 126014878).
    /// </summary>
    public int DefaultItemWeightGrams { get; set; } = 0;
}
