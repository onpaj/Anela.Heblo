using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;

public class GetProductMarginAnalysisResponse : BaseResponse
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal TotalMargin { get; set; }
    public decimal MarginPercentage { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public int TotalUnitsSold { get; set; }
    public DateTime AnalysisPeriodStart { get; set; }
    public DateTime AnalysisPeriodEnd { get; set; }
    public List<MonthlyMarginBreakdownDto> MonthlyBreakdown { get; set; } = new();
}