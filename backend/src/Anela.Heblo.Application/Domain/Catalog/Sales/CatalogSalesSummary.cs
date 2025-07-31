namespace Anela.Heblo.Application.Domain.Catalog.Sales;

public class CatalogSalesSummary
{
    public decimal B2B { get; set; }
    public decimal B2C { get; set; }
    public double AmountB2B { get; set; }
    public double AmountB2C { get; set; }
    public double DailyB2B { get; set; }
    public double DailyB2C { get; set; }

    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public double DailySales => (AmountB2B + AmountB2C) / (DateTo - DateFrom).TotalDays;
}