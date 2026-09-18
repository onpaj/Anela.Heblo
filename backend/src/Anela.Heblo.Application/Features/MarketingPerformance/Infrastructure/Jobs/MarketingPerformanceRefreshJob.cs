using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Infrastructure.Jobs;

/// <summary>
/// Daily snapshot of ad spend (Flexi received invoices by supplier DIČ) and orders/revenue (IssuedInvoices)
/// for the current and previous month (MarketingPerformance:RecomputeWindowMonths). Older months are locked.
/// </summary>
public class MarketingPerformanceRefreshJob : IRecurringJob
{
    public const string Name = "marketing-performance-refresh";
    private const int LockTimeoutSeconds = 600;

    private readonly IMarketingPerformanceRefreshService _service;
    private readonly MarketingPerformanceRunGuard _guard;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<MarketingPerformanceRefreshJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public MarketingPerformanceRefreshJob(
        IMarketingPerformanceRefreshService service,
        MarketingPerformanceRunGuard guard,
        IRecurringJobStatusChecker statusChecker,
        IOptions<MarketingPerformanceOptions> options,
        ILogger<MarketingPerformanceRefreshJob> logger)
    {
        _service = service;
        _guard = guard;
        _statusChecker = statusChecker;
        _logger = logger;
        Metadata = new RecurringJobMetadata
        {
            JobName = Name,
            DisplayName = "Marketing — výkon reklamy (měsíční snapshot)",
            Description = "Denně přepočítá aktuální a předchozí měsíc: náklady na reklamu z přijatých faktur v ABRA Flexi (podle DIČ dodavatele na kanál) a objednávky/tržby z vydaných faktur (CZK, podle DUZP, maloobchod/velkoobchod zvlášť). Starší měsíce uzamkne; ty mění jen ruční přepočet na obrazovce Výkon reklamy.",
            CronExpression = options.Value.CronExpression,
            DefaultIsEnabled = true,
        };
    }

    [DisableConcurrentExecution(LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken, Metadata.DefaultIsEnabled))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        if (!_guard.TryBegin())
        {
            _logger.LogWarning("Job {JobName}: another marketing performance run is active. Skipping.", Metadata.JobName);
            return;
        }

        try
        {
            var result = await _service.RefreshWindowAsync(cancellationToken);
            _logger.LogInformation("{JobName} complete: {Ok}/{Total} months fully refreshed, {Locked} locked",
                Metadata.JobName, result.Months.Count(m => m.RevenueOk && m.CostsOk), result.Months.Count, result.LockedMonths);

            if (result.AllFailed)
            {
                throw new InvalidOperationException(
                    $"{Metadata.JobName}: every month in the window failed: {string.Join(" | ", result.Months.Select(m => $"{m.Month}: {m.Error}"))}");
            }
        }
        finally
        {
            _guard.End();
        }
    }
}
