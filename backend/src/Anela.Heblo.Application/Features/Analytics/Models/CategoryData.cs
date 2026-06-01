namespace Anela.Heblo.Application.Features.Analytics.Models;

/// <summary>
/// Data structure for accumulating category-level margin information
/// </summary>
public class CategoryData
{
    public decimal TotalMargin { get; set; }
    public decimal TotalRevenue { get; set; }
    public int ProductCount { get; set; }
    public int TotalUnitsSold { get; set; }
}