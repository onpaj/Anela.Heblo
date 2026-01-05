using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Jobs;

public class DailyInvoiceImportEurJob : IRecurringJob
{
    private readonly IInvoiceImportService _invoiceImportService;
    private readonly ILogger<DailyInvoiceImportEurJob> _logger;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-invoice-import-eur",
        DisplayName = "Daily Invoice Import (EUR)",
        Description = "Imports EUR invoices from Shoptet to ABRA Flexi",
        CronExpression = "0 4 * * *", // Daily at 4:00 AM
        DefaultIsEnabled = true
    };

    public DailyInvoiceImportEurJob(
        IInvoiceImportService invoiceImportService,
        ILogger<DailyInvoiceImportEurJob> logger,
        IRecurringJobStatusChecker statusChecker)
    {
        _invoiceImportService = invoiceImportService;
        _logger = logger;
        _statusChecker = statusChecker;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        await ImportYesterdayForCurrency("EUR", cancellationToken);
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
