using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Bank;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public sealed class ComgateCzkImportJob : BankImportJobBase
{
    public override RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-comgate-czk-import",
        DisplayName = "Daily Comgate CZK Import",
        Description = "Imports Comgate CZK payment statements from previous day",
        CronExpression = "30 4 * * *",
        DefaultIsEnabled = true,
    };

    public ComgateCzkImportJob(
        IMediator mediator,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker,
        IBankImportStateRepository stateRepository,
        IBankStatementImportRepository statementRepository,
        IOptions<BankImportWatermarkOptions> options)
        : base(mediator, loggerFactory, statusChecker, stateRepository, statementRepository, options)
    {
    }

    protected override string AccountName => BankAccountNames.ComgateCzk;
    protected override DateTime GetTargetEndDate(DateTime today) => today.AddDays(-1);
}
