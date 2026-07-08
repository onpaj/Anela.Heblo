using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.FinancialOverview.Model;

public class YearComparisonSeriesDto
{
    [Required]
    public int Year { get; set; }

    /// <summary>Cells for this year, ascending by month, only months that have data.</summary>
    [Required]
    public List<MonthlyFinancialDataDto> Months { get; set; } = new();

    [Required]
    public decimal YtdIncome { get; set; }

    [Required]
    public decimal YtdExpenses { get; set; }

    [Required]
    public decimal YtdFinancialBalance { get; set; }

    public decimal? YtdStockValueChange { get; set; }

    public decimal? YtdTotalBalance { get; set; }
}
