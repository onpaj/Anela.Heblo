using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.FinancialOverview.Model;

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