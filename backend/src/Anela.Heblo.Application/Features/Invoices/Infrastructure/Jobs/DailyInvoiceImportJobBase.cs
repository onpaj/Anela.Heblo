using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Jobs;

public abstract class DailyInvoiceImportJobBase : IRecurringJob
{
    private readonly IInvoiceImportService _invoiceImportService;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger _logger;

    public abstract RecurringJobMetadata Metadata { get; }

    protected abstract string Currency { get; }

    protected DailyInvoiceImportJobBase(
        IInvoiceImportService invoiceImportService,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker)
    {
        _invoiceImportService = invoiceImportService;
        _statusChecker = statusChecker;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        await ImportYesterdayForCurrency(Currency, cancellationToken);
    }

    private async Task ImportYesterdayForCurrency(string currency, CancellationToken cancellationToken)
    {
        var yesterday = DateTime.Today.AddDays(-1);

        _logger.LogInformation("Starting {JobName} for {Date:yyyy-MM-dd}", Metadata.JobName, yesterday);

        try
        {
            var query = new IssuedInvoiceSourceQuery
            {
                RequestId = Guid.NewGuid().ToString(),
                DateFrom = yesterday,
                DateTo = yesterday,
                Currency = currency
            };

            var result = await _invoiceImportService.ImportInvoicesAsync(
                $"denní import {currency} za {yesterday:dd.MM.yyyy}",
                query,
                cancellationToken);

            _logger.LogInformation(
                "{JobName} completed for {Date:yyyy-MM-dd}. Succeeded: {SucceededCount}, Failed: {FailedCount}",
                Metadata.JobName, yesterday, result.Succeeded.Count, result.Failed.Count);

            if (result.Failed.Count > 0)
            {
                _logger.LogWarning("{JobName} - some invoices failed to import on {Date:yyyy-MM-dd}: {FailedInvoices}",
                    Metadata.JobName, yesterday, string.Join(", ", result.Failed));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed for {Date:yyyy-MM-dd}", Metadata.JobName, yesterday);
            throw; // Re-throw to let Hangfire handle retry logic
        }
    }
}
