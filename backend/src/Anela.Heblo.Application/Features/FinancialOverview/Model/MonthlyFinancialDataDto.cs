using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.FinancialOverview.Model;

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