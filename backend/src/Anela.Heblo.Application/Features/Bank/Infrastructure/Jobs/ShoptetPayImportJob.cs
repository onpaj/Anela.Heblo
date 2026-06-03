using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public sealed class ShoptetPayImportJob : BankImportJobBase
{
    public override RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-shoptetpay-czk-import",
        DisplayName = "Daily ShoptetPay CZK Import",
        Description = "Imports ShoptetPay CZK payment statements from current day",
        CronExpression = "50 4 * * *",
        DefaultIsEnabled = true,
    };

    public ShoptetPayImportJob(
        IMediator mediator,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker)
        : base(mediator, loggerFactory, statusChecker)
    {
    }

    internal override BankImportJobParameters GetParameters()
    {
        var today = DateTime.Today;
        return new BankImportJobParameters(BankAccountNames.ShoptetPayCzk, today, today);
    }
}
