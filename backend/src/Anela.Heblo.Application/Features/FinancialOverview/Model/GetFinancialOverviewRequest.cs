using MediatR;

namespace Anela.Heblo.Application.Features.FinancialOverview.Model;

public class GetFinancialOverviewRequest : IRequest<GetFinancialOverviewResponse>
{
    public int? Months { get; set; } = 6;

    /// <summary>
    /// Include stock value changes in the response (Phase 2 functionality)
    /// </summary>
    public bool IncludeStockData { get; set; } = true;
}