using MediatR;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public class GetFinancialOverviewRequest : IRequest<GetFinancialOverviewResponse>
{
    public int? Months { get; set; } = 6;

    /// <summary>
    /// Include stock value changes in the response (Phase 2 functionality)
    /// </summary>
    public bool IncludeStockData { get; set; } = true;

    /// <summary>
    /// Departments to exclude from income/expense calculations.
    /// When null or empty, the cached path is used (all departments included).
    /// When populated, a real-time calculation is performed with in-memory department filtering.
    /// </summary>
    public List<string>? ExcludedDepartments { get; set; }

    /// <summary>
    /// When true, includes the current (incomplete) month in the data.
    /// Bypasses cache and uses real-time calculation.
    /// </summary>
    public bool IncludeCurrentMonth { get; set; } = false;
}
