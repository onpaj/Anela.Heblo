using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.FinancialOverview.Model;

public class GetFinancialOverviewResponse
{
    [Required]
    public List<MonthlyFinancialDataDto> Data { get; set; } = new();
    
    [Required]
    public FinancialSummaryDto Summary { get; set; } = new();
}

public class MonthlyFinancialDataDto
{
    [Required]
    public int Year { get; set; }
    
    [Required]
    public int Month { get; set; }
    
    [Required]
    public string MonthYearDisplay { get; set; } = string.Empty;
    
    [Required]
    public decimal Income { get; set; }
    
    [Required]
    public decimal Expenses { get; set; }
    
    [Required]
    public decimal FinancialBalance { get; set; }
    
    /// <summary>
    /// Stock value changes for this month (Phase 2 - optional)
    /// </summary>
    public StockChangeDto? StockChanges { get; set; }
    
    /// <summary>
    /// Total stock value change (sum of all warehouse types)
    /// </summary>
    public decimal? TotalStockValueChange { get; set; }
    
    /// <summary>
    /// Total balance including stock value changes (FinancialBalance + TotalStockValueChange)
    /// </summary>
    public decimal? TotalBalance { get; set; }
}

public class FinancialSummaryDto
{
    [Required]
    public decimal TotalIncome { get; set; }
    
    [Required]
    public decimal TotalExpenses { get; set; }
    
    [Required]
    public decimal TotalBalance { get; set; }
    
    [Required]
    public decimal AverageMonthlyIncome { get; set; }
    
    [Required]
    public decimal AverageMonthlyExpenses { get; set; }
    
    [Required]
    public decimal AverageMonthlyBalance { get; set; }
    
    /// <summary>
    /// Stock data summary (Phase 2 - optional)
    /// </summary>
    public StockSummaryDto? StockSummary { get; set; }
}

public class StockChangeDto
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

public class StockSummaryDto
{
    /// <summary>
    /// Total stock value change across all warehouses and months
    /// </summary>
    public decimal TotalStockValueChange { get; set; }
    
    /// <summary>
    /// Average monthly stock value change
    /// </summary>
    public decimal AverageMonthlyStockChange { get; set; }
    
    /// <summary>
    /// Total balance including stock changes (TotalBalance + TotalStockValueChange)
    /// </summary>
    public decimal TotalBalanceWithStock { get; set; }
    
    /// <summary>
    /// Average monthly total balance including stock changes
    /// </summary>
    public decimal AverageMonthlyTotalBalance { get; set; }
}