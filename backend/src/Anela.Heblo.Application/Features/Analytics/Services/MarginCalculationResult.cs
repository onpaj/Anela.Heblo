using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

/// <summary>
/// Result object for margin calculations
/// </summary>
public class MarginCalculationResult
{
    public required Dictionary<string, decimal> GroupTotals { get; init; }
    public required Dictionary<string, List<AnalyticsProduct>> GroupProducts { get; init; }
    public required decimal TotalMargin { get; init; }
}
