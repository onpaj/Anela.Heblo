namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class CatalogSalesRecordDto
{
    public DateTime Date { get; set; }
    public double AmountTotal { get; set; }
    public double AmountB2B { get; set; }
    public double AmountB2C { get; set; }
    public decimal SumTotal { get; set; }
    public decimal SumB2B { get; set; }
    public decimal SumB2C { get; set; }
}

public class CatalogPurchaseRecordDto
{
    public DateTime Date { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public double Amount { get; set; }
    public decimal PricePerPiece { get; set; }
    public decimal PriceTotal { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
}

public class CatalogConsumedRecordDto
{
    public DateTime Date { get; set; }
    public double Amount { get; set; }
    public string ProductName { get; set; } = string.Empty;
}

public class CatalogHistoricalDataDto
{
    public List<CatalogSalesRecordDto> SalesHistory { get; set; } = new();
    public List<CatalogPurchaseRecordDto> PurchaseHistory { get; set; } = new();
    public List<CatalogConsumedRecordDto> ConsumedHistory { get; set; } = new();
}