namespace Anela.Heblo.Application.Domain.Catalog.Sales;

public record CatalogSaleRecord
{
    public DateTime Date { get; set; }

    public string ProductCode { get; set; }

    public string ProductName { get; set; }

    public double AmountTotal { get; set; }
    public double AmountB2B { get; set; }
    public double AmountB2C { get; set; }

    public decimal SumTotal { get; set; }
    public decimal SumB2B { get; set; }
    public decimal SumB2C { get; set; }
}