using MediatR;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;

public class GetMarginReportRequest : IRequest<GetMarginReportResponse>
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? ProductFilter { get; set; }
    public string? CategoryFilter { get; set; }
    public bool IncludeDetailedBreakdown { get; set; } = false;
    public int MaxProducts { get; set; } = 50;
}