using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Bank;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        IRecurringJobStatusChecker statusChecker,
        IBankImportStateRepository stateRepository,
        IBankStatementImportRepository statementRepository,
        IOptions<BankImportWatermarkOptions> options)
        : base(mediator, loggerFactory, statusChecker, stateRepository, statementRepository, options)
    {
    }

    protected override string AccountName => BankAccountNames.ShoptetPayCzk;
    protected override DateTime GetTargetEndDate(DateTime today) => today;
}
