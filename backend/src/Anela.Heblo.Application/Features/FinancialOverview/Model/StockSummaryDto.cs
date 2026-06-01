namespace Anela.Heblo.Application.Features.FinancialOverview.Model;

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