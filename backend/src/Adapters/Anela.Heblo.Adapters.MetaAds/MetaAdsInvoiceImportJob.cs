using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.MetaAds;

public class MetaAdsInvoiceImportJob : IRecurringJob
{
    private readonly MetaAdsTransactionSource _source;
    private readonly IImportedMarketingTransactionRepository _repository;
    private readonly ILogger<MarketingInvoiceImportService> _importLogger;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<MetaAdsInvoiceImportJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "meta-ads-invoice-import",
        DisplayName = "Meta Ads Invoice Import",
        Description = "Fetches billing transactions from Meta Ads Graph API (7-day lookback)",
        CronExpression = "0 6,18 * * *", // 6 AM and 6 PM Prague time
        DefaultIsEnabled = true,
    };

    public MetaAdsInvoiceImportJob(
        MetaAdsTransactionSource source,
        IImportedMarketingTransactionRepository repository,
        ILogger<MarketingInvoiceImportService> importLogger,
        IRecurringJobStatusChecker statusChecker,
        ILogger<MetaAdsInvoiceImportJob> logger)
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
                Metadata.JobName, result.Imported, result.Skipped, result.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
