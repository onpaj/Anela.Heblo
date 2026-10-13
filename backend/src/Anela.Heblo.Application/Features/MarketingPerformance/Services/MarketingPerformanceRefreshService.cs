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
            errors.Add($"Revenue: step failed ({ex.GetType().Name}); see server logs");
        }

        try
        {
            var channels = _options.ToDefinitions();
            var vatIds = channels.SelectMany(c => c.VatIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var invoices = await _costSource.GetAsync(month, vatIds, cancellationToken);
            var bucketing = ChannelCostBucketer.Bucket(invoices, channels);
            unmatched = bucketing.UnmatchedVatIds.Count;
            if (bucketing.SkippedCancelled > 0)
            {
                _logger.LogInformation("Marketing performance {Month}: skipped {Count} storno invoice(s)",
                    month, bucketing.SkippedCancelled);
            }
            if (unmatched > 0)
            {
                // Count only: a Czech dic can be a natural person's birth-number-derived
                // identifier, which must not reach logs/telemetry unredacted.
                _logger.LogWarning("Marketing performance {Month}: {Count} invoice supplier DIČ(s) matched no channel",
                    month, unmatched);
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
            if (_costSource.IsConfigured)
            {
                row.CostsComputedAt = now;
                costsOk = true;
            }
            else
            {
                // The no-op source returns an empty invoice list, which is indistinguishable from a real
                // "no ad spend this month" result. Without this guard the month would be stamped as
                // successfully computed and render 0 Kč cost / 0 % PNO with no warning badge, because the
                // badge keys off exactly LastError / the two *ComputedAt timestamps. costsOk stays false and
                // CostsComputedAt stays unset so the unwired state is visible through that same affordance.
                errors.Add("Costs: ad-cost source is not configured (Flexi adapter not wired) — costs read as zero");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Marketing performance: cost step failed for {Month}", month);
            errors.Add($"Costs: step failed ({ex.GetType().Name}); see server logs");
        }

        row.LastError = errors.Count == 0 ? null : string.Join(" | ", errors);

        if (isNew)
        {
            await _repository.AddAsync(row, cancellationToken);
        }

        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // SaveChangesAsync deliberately sits outside the two per-step try/catch blocks above: a persistence
            // failure here (e.g. DbUpdateException) must not escape RefreshMonthAsync. The callers loop over
            // months with [AutomaticRetry(Attempts = 0)] on the recompute job, so letting this propagate would
            // abort every remaining month in a backfill with no record of which one failed.
            _logger.LogError(ex, "Marketing performance: save failed for {Month}", month);

            // A failed SaveChangesAsync leaves this row (and its ChannelCosts children, re-added above) still
            // tracked in the shared DbContext — a documented poison hazard where the stale tracked entity
            // resurfaces at a LATER, unrelated SaveChangesAsync call for a different month. Detach both the
            // row and its children so the next month's save starts clean.
            _repository.Detach(row);

            errors.Add($"Save: step failed ({ex.GetType().Name}); see server logs");
            return new MonthRefreshOutcome
            {
                Month = month,
                RevenueOk = false,
                CostsOk = false,
                Error = string.Join(" | ", errors),
                UnmatchedVatIdCount = unmatched,
            };
        }

        _logger.LogInformation("Marketing performance {Month}: revenue={RevenueOk} costs={CostsOk}", month, revenueOk, costsOk);
        return new MonthRefreshOutcome { Month = month, RevenueOk = revenueOk, CostsOk = costsOk, Error = row.LastError, UnmatchedVatIdCount = unmatched };
    }
}
