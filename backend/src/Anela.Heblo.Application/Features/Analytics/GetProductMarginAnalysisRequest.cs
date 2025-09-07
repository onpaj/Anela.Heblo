using MediatR;

namespace Anela.Heblo.Application.Features.Analytics;

public class GetProductMarginAnalysisRequest : IRequest<GetProductMarginAnalysisResponse>
{
    public string ProductId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IncludeBreakdown { get; set; } = true;
}