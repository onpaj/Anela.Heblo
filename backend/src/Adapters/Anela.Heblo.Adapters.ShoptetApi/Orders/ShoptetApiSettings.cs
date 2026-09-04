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

    /// <summary>
    /// Shoptet price list to sync retail prices with. Must be configured explicitly —
    /// GET /api/pricelists returns no `default` flag, so the retail list cannot be discovered
    /// automatically; a null value makes the price sync throw rather than guess.
    /// Configure as Shoptet:DefaultPriceListId. On the Anela store: id 1 is "Hlavní ceník"
    /// (retail, the source of truth for the price sync); 32 is Bezobal; 38 and 39 are the two
    /// wholesale lists (Velkoobchodní ceník / Velkoobchodní 35%) and must never be used here.
    /// </summary>
    public int? DefaultPriceListId { get; set; }
}
