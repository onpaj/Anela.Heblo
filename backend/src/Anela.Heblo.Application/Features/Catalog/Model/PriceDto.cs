namespace Anela.Heblo.Application.Features.Catalog.Model;

public class PriceDto
{
    public decimal? CurrentSellingPrice { get; set; }
    public decimal? CurrentPurchasePrice { get; set; }
    public decimal? SellingPriceWithVat { get; set; }
    public decimal? PurchasePriceWithVat { get; set; }
    public EshopPriceDto? EshopPrice { get; set; }
    public ErpPriceDto? ErpPrice { get; set; }
}

public class EshopPriceDto
{
    public decimal PriceWithVat { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal PriceWithoutVat { get; set; }
}

public class ErpPriceDto
{
    public decimal PriceWithoutVat { get; set; }
    public decimal PriceWithVat { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal PurchasePriceWithVat { get; set; }
}