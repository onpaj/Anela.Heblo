using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Jobs;

public sealed class DailyInvoiceImportCzkJob : DailyInvoiceImportJobBase
{
    public override RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-invoice-import-czk",
        DisplayName = "Daily Invoice Import (CZK)",
        Description = "Imports CZK invoices from Shoptet to ABRA Flexi",
        CronExpression = "15 4 * * *", // Daily at 4:15 AM
        DefaultIsEnabled = true
    };

    protected override string Currency => "CZK";

    public DailyInvoiceImportCzkJob(
        IInvoiceImportService invoiceImportService,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker)
        : base(invoiceImportService, loggerFactory, statusChecker)
    {
    }
}
