using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Contracts;

public class MarketingYearSeriesDto
{
    [Required] public int Year { get; set; }
    /// <summary>Always 12 cells, January to December; months without data have HasData = false.</summary>
    [Required] public List<MonthlyMarketingPerformanceDto> Months { get; set; } = new();
    [Required] public int YtdOrders { get; set; }
    [Required] public decimal YtdRevenueWithoutVat { get; set; }
    [Required] public decimal YtdTotalCost { get; set; }
    public decimal? YtdPno { get; set; }
}
