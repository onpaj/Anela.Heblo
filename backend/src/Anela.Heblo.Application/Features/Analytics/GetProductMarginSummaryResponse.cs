using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics;

public class GetProductMarginSummaryResponse : BaseResponse
{
    public List<MonthlyProductMarginDto> MonthlyData { get; set; } = new();
    public List<TopProductDto> TopProducts { get; set; } = new(); // For legend/color mapping
    public decimal TotalMargin { get; set; }
    public string TimeWindow { get; set; } = string.Empty;
    public ProductGroupingMode GroupingMode { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
}