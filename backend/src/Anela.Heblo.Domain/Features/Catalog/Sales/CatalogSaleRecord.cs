namespace Anela.Heblo.Domain.Features.Catalog.Sales;

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

    /// <summary>
    /// Set when this record was derived from a bundle sale rather than an invoice line.
    /// Such records carry quantity only — all Sum* values are zero, so the bundle's own record
    /// keeps the full revenue. Null on records that came straight from the ERP.
    /// </summary>
    public string? SourceBundleCode { get; set; }
}