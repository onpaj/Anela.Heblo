using Anela.Heblo.Domain.Features.Analytics;
using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginSummary;

public class GetProductMarginSummaryRequest : IRequest<GetProductMarginSummaryResponse>
{
    public string TimeWindow { get; set; } = "current-year"; // current-year, current-and-previous-year, last-6-months, last-12-months, last-24-months
    public int TopProductCount { get; set; } = 15; // Configurable, default 15
    public ProductGroupingMode GroupingMode { get; set; } = ProductGroupingMode.Products; // Products, ProductFamily, ProductType

    // Margin level for display (determines which margin values to show)
    public string MarginLevel { get; set; } = "M3"; // M0, M1, M2, M3 (default M3 - net profitability)

    // Sorting parameters
    public string? SortBy { get; set; } // Column to sort by (m0percentage, m1amount, totalmargin, etc.)
    public bool SortDescending { get; set; } = true; // Default descending for margin sorting
}