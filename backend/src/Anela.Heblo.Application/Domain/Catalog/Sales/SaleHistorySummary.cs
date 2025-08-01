namespace Anela.Heblo.Application.Domain.Catalog.Sales;

public class SaleHistorySummary
{
    public Dictionary<string, MonthlySalesSummary> MonthlyData { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class MonthlySalesSummary
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal TotalB2B { get; set; }
    public decimal TotalB2C { get; set; }
    public double AmountB2B { get; set; }
    public double AmountB2C { get; set; }
    public int TransactionCount { get; set; }

    public string MonthKey => $"{Year:D4}-{Month:D2}";
    public decimal TotalRevenue => TotalB2B + TotalB2C;
    public double TotalAmount => AmountB2B + AmountB2C;
}

