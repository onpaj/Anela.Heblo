using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.GoogleAds;

public class GoogleAdsInvoiceImportJob : IRecurringJob
{
    private readonly GoogleAdsTransactionSource _source;
    private readonly IImportedMarketingTransactionRepository _repository;
    private readonly ILogger<MarketingInvoiceImportService> _importLogger;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<GoogleAdsInvoiceImportJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "google-ads-invoice-import",
        DisplayName = "Google Ads Invoice Import",
        Description = "Fetches billing transactions from Google Ads API via account_budget GAQL queries (7-day lookback)",
        CronExpression = "15 6,18 * * *",
        DefaultIsEnabled = true,
    };

    public GoogleAdsInvoiceImportJob(
        GoogleAdsTransactionSource source,
        IImportedMarketingTransactionRepository repository,
        ILogger<MarketingInvoiceImportService> importLogger,
        IRecurringJobStatusChecker statusChecker,
        ILogger<GoogleAdsInvoiceImportJob> logger)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _importLogger = importLogger ?? throw new ArgumentNullException(nameof(importLogger));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        try
        {
            _logger.LogInformation("Starting {JobName}", Metadata.JobName);

            var to = DateTime.UtcNow;
            var from = to.AddDays(-7);

            var service = new MarketingInvoiceImportService(_source, _repository, _importLogger);
            var result = await service.ImportAsync(from, to, cancellationToken);

            _logger.LogInformation(
                "{JobName} completed. Imported={Imported}, Skipped={Skipped}, Failed={Failed}",
                Metadata.JobName,
                result.Imported,
                result.Skipped,
                result.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
