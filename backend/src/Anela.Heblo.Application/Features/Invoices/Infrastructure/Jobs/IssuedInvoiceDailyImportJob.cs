using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Jobs;

public class IssuedInvoiceDailyImportJob
{
    private readonly IInvoiceImportService _invoiceImportService;
    private readonly ILogger<IssuedInvoiceDailyImportJob> _logger;

    public IssuedInvoiceDailyImportJob(
        IInvoiceImportService invoiceImportService,
        ILogger<IssuedInvoiceDailyImportJob> logger)
    {
        _invoiceImportService = invoiceImportService;
        _logger = logger;
    }

    public async Task ImportYesterdayEur()
    {
        await ImportYesterdayForCurrency("EUR");
    }

    public async Task ImportYesterdayCzk()
    {
        await ImportYesterdayForCurrency("CZK");
    }

    private async Task ImportYesterdayForCurrency(string currency)
    {
        var yesterday = DateTime.Today.AddDays(-1);

        _logger.LogInformation("Starting daily {Currency} invoice import job for {Date:yyyy-MM-dd}", currency, yesterday);

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
                CancellationToken.None);

            _logger.LogInformation(
                "Daily {Currency} invoice import job completed for {Date:yyyy-MM-dd}. Succeeded: {SucceededCount}, Failed: {FailedCount}",
                currency, yesterday, result.Succeeded.Count, result.Failed.Count);

            if (result.Failed.Count > 0)
            {
                _logger.LogWarning("Some {Currency} invoices failed to import on {Date:yyyy-MM-dd}: {FailedInvoices}",
                    currency, yesterday, string.Join(", ", result.Failed));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daily {Currency} invoice import job failed for {Date:yyyy-MM-dd}", currency, yesterday);
            throw; // Re-throw to let Hangfire handle retry logic
        }
    }
}