namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class CategoryMarginSummaryDto
{
    public string Category { get; set; } = string.Empty;
    public decimal TotalMargin { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal AverageMarginPercentage { get; set; }
    public int ProductCount { get; set; }
    public int TotalUnitsSold { get; set; }
}
