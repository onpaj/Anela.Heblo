using MediatR;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public class GetFinancialComparisonRequest : IRequest<GetFinancialComparisonResponse>
{
    /// <summary>Number of years to compare. Clamped to 2..3 by the service.</summary>
    public int? Years { get; set; } = 3;

    public bool IncludeStockData { get; set; } = true;

    public List<string>? ExcludedDepartments { get; set; }

    /// <summary>When true, includes the partial cutoff month (cut at today - lag days) for every year.</summary>
    public bool IncludePartialMonth { get; set; } = true;
}
