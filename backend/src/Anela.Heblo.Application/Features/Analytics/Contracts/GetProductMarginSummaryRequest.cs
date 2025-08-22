using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class GetProductMarginSummaryRequest : IRequest<GetProductMarginSummaryResponse>
{
    public string TimeWindow { get; set; } = "current-year"; // current-year, current-and-previous-year, last-6-months, last-12-months, last-24-months
    public int TopProductCount { get; set; } = 15; // Configurable, default 15
    public ProductGroupingMode GroupingMode { get; set; } = ProductGroupingMode.Products; // Products, ProductFamily, ProductType
}