namespace Anela.Heblo.Domain.Features.FinancialOverview;

public class MonthlyStockChange
{
    public int Year { get; set; }
    public int Month { get; set; }
    public StockChangeByType StockChanges { get; set; } = new();
    
    /// <summary>
    /// Total stock value change (sum of all warehouse types)
    /// </summary>
    public decimal TotalStockValueChange => 
        StockChanges.Materials + StockChanges.SemiProducts + StockChanges.Products;

    public string MonthYearDisplay => $"{Month:D2}/{Year}";
}

public class StockChangeByType
{
    /// <summary>
    /// Stock value change for Materials warehouse (MATERIAL - ID 5)
    /// </summary>
    public decimal Materials { get; set; }
    
    /// <summary>
    /// Stock value change for Semi-products warehouse (POLOTOVARY - ID 20)
    /// </summary>
    public decimal SemiProducts { get; set; }
    
    /// <summary>
    /// Stock value change for Products/Goods warehouse (ZBOZI - ID 4)
    /// </summary>
    public decimal Products { get; set; }
}