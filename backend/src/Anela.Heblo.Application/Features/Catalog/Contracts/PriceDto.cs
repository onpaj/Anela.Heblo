namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class PriceDto
{
    public decimal? CurrentSellingPrice { get; set; }
    public decimal? CurrentPurchasePrice { get; set; }
    public decimal? SellingPriceWithVat { get; set; }
    public decimal? PurchasePriceWithVat { get; set; }
    /// <summary>Warehouse valuation per unit of the pieces currently in stock; null when nothing is in stock.</summary>
    public decimal? StockPrice { get; set; }
    public EshopPriceDto? EshopPrice { get; set; }
    public ErpPriceDto? ErpPrice { get; set; }
}

public class EshopPriceDto
{
    /// <summary>The price customers pay today — the action price while an action runs.</summary>
    public decimal PriceWithVat { get; set; }
    public decimal PurchasePrice { get; set; }
    /// <summary>Without-VAT form of <see cref="PriceWithVat"/>.</summary>
    public decimal PriceWithoutVat { get; set; }
    /// <summary>The regular list price, regardless of any action.</summary>
    public decimal? RegularPriceWithVat { get; set; }
    /// <summary>The action price set in the e-shop, even when the action is not running today.</summary>
    public decimal? ActionPriceWithVat { get; set; }
    public DateOnly? ActionFrom { get; set; }
    public DateOnly? ActionUntil { get; set; }
    public bool IsInAction { get; set; }
}

public class ErpPriceDto
{
    public decimal PriceWithoutVat { get; set; }
    public decimal PriceWithVat { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal PurchasePriceWithVat { get; set; }
}