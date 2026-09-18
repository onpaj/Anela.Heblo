using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

public class MarketingPerformanceRefreshService : IMarketingPerformanceRefreshService
{
    private readonly IMarketingPerformanceRepository _repository;
    private readonly IMonthlyRevenueSource _revenueSource;
    private readonly IMonthlyAdCostSource _costSource;
    private readonly MarketingPerformanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarketingPerformanceRefreshService> _logger;

    public MarketingPerformanceRefreshService(
        IMarketingPerformanceRepository repository,
        IMonthlyRevenueSource revenueSource,
        IMonthlyAdCostSource costSource,
        IOptions<MarketingPerformanceOptions> options,
        TimeProvider timeProvider,
        ILogger<MarketingPerformanceRefreshService> logger)
    {
        _repository = repository;
        _revenueSource = revenueSource;
        _costSource = costSource;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<RefreshRunResult> RefreshWindowAsync(CancellationToken cancellationToken)
    {
        var current = YearMonth.From(_timeProvider.GetLocalNow().DateTime);
        var windowStart = current.AddMonths(-(_options.RecomputeWindowMonths - 1));

        var outcomes = new List<MonthRefreshOutcome>();
        foreach (var month in YearMonth.Range(windowStart, current))
        {
            outcomes.Add(await RefreshMonthAsync(month, cancellationToken));
        }

        var locked = await _repository.LockMonthsBeforeAsync(windowStart, cancellationToken);
        if (locked > 0)
        {
            _logger.LogInformation("Marketing performance: locked {Count} month(s) before {Cutoff}", locked, windowStart);
        }

        return new RefreshRunResult { Months = outcomes, LockedMonths = locked };
    }

    public async Task<RefreshRunResult> RecomputeRangeAsync(YearMonth from, YearMonth to, CancellationToken cancellationToken)
    {
        var outcomes = new List<MonthRefreshOutcome>();
        foreach (var month in YearMonth.Range(from, to))
        {
            outcomes.Add(await RefreshMonthAsync(month, cancellationToken));
        }
        return new RefreshRunResult { Months = outcomes, LockedMonths = 0 };
    }

    private async Task<MonthRefreshOutcome> RefreshMonthAsync(YearMonth month, CancellationToken cancellationToken)
    {
        var row = await _repository.GetForUpdateAsync(month, cancellationToken);
        var isNew = row is null;
        row ??= new MarketingPerformanceMonth { Year = month.Year, Month = month.Month };

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var errors = new List<string>();
        var revenueOk = false;
        var costsOk = false;
        var unmatched = 0;

        try
        {
            var revenue = await _revenueSource.GetAsync(month, cancellationToken);
            row.RetailOrderCount = revenue.RetailOrderCount;
            row.RetailRevenueWithVat = revenue.RetailRevenueWithVat;
            row.WholesaleOrderCount = revenue.WholesaleOrderCount;
            row.WholesaleRevenueWithVat = revenue.WholesaleRevenueWithVat;
            row.SkippedEurInvoiceCount = revenue.SkippedEurInvoiceCount;
            row.RevenueComputedAt = now;
            revenueOk = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Marketing performance: revenue step failed for {Month}", month);
            errors.Add($"Revenue: {ex.Message}");
        }

        try
        {
            var channels = _options.ToDefinitions();
            var vatIds = channels.SelectMany(c => c.VatIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var invoices = await _costSource.GetAsync(month, vatIds, cancellationToken);
            var bucketing = ChannelCostBucketer.Bucket(invoices, channels);
            unmatched = bucketing.UnmatchedVatIds.Count;
            if (unmatched > 0)
            {
                _logger.LogWarning("Marketing performance {Month}: {Count} invoice supplier DIČ(s) matched no channel: {VatIds}",
                    month, unmatched, string.Join(", ", bucketing.UnmatchedVatIds));
            }

            row.ChannelCosts.Clear();
            foreach (var bucket in bucketing.Buckets)
            {
                // Never set Id on children added to a tracked parent — EF would issue UPDATE instead of INSERT.
                row.ChannelCosts.Add(new MarketingPerformanceChannelCost
                {
                    ChannelCode = bucket.ChannelCode,
                    CostWithoutVat = bucket.CostWithoutVat,
                    InvoiceCount = bucket.InvoiceCount,
                });
            }
            row.CostsComputedAt = now;
            costsOk = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Marketing performance: cost step failed for {Month}", month);
            errors.Add($"Costs: {ex.Message}");
        }

        row.LastError = errors.Count == 0 ? null : string.Join(" | ", errors);

        if (isNew)
        {
            await _repository.AddAsync(row, cancellationToken);
        }
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Marketing performance {Month}: revenue={RevenueOk} costs={CostsOk}", month, revenueOk, costsOk);
        return new MonthRefreshOutcome { Month = month, RevenueOk = revenueOk, CostsOk = costsOk, Error = row.LastError, UnmatchedVatIdCount = unmatched };
    }
}
