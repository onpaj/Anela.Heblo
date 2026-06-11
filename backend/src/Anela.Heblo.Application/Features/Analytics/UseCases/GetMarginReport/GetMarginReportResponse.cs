using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;

public class GetMarginReportResponse : BaseResponse
{
    public DateTime ReportPeriodStart { get; set; }
    public DateTime ReportPeriodEnd { get; set; }
    public decimal TotalMargin { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal AverageMarginPercentage { get; set; }
    public int TotalProductsAnalyzed { get; set; }
    public int TotalUnitsSold { get; set; }
    public List<ProductMarginSummaryDto> ProductSummaries { get; set; } = new();
    public List<CategoryMarginSummaryDto> CategorySummaries { get; set; } = new();
}