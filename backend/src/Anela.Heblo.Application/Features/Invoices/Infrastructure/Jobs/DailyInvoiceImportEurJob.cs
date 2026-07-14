using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Jobs;

public sealed class DailyInvoiceImportEurJob : DailyInvoiceImportJobBase
{
    public override RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-invoice-import-eur",
        DisplayName = "Daily Invoice Import (EUR)",
        Description = "Imports EUR invoices from Shoptet to ABRA Flexi",
        CronExpression = "0 4 * * *", // Daily at 4:00 AM
        DefaultIsEnabled = true
    };

    protected override string Currency => "EUR";

    public DailyInvoiceImportEurJob(
        IInvoiceImportService invoiceImportService,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker)
        : base(invoiceImportService, loggerFactory, statusChecker)
    {
    }
}
