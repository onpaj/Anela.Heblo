using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

/// <summary>
/// No-op implementation of IMonthlyAdCostSource used until the Flexi adapter (which supplies
/// received-invoice data) is registered. Logs a warning and returns an empty list so the
/// application starts cleanly; ad costs read as zero until the real source is wired in.
/// </summary>
public sealed class NoOpMonthlyAdCostSource : IMonthlyAdCostSource
{
    private readonly ILogger<NoOpMonthlyAdCostSource> _logger;

    public NoOpMonthlyAdCostSource(ILogger<NoOpMonthlyAdCostSource> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<AdCostInvoice>> GetAsync(YearMonth month, IReadOnlyCollection<string> vatIds, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Ad cost source disabled (Flexi adapter not registered) — ad costs for {Month} will read as zero",
            month);
        return Task.FromResult<IReadOnlyList<AdCostInvoice>>(Array.Empty<AdCostInvoice>());
    }
}
