using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Bank;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public sealed class ComgateEurImportJob : BankImportJobBase
{
    public override RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-comgate-eur-import",
        DisplayName = "Daily Comgate EUR Import",
        Description = "Imports Comgate EUR payment statements from previous day",
        CronExpression = "40 4 * * *",
        DefaultIsEnabled = true,
    };

    public ComgateEurImportJob(
        IMediator mediator,
        ILoggerFactory loggerFactory,
        IRecurringJobStatusChecker statusChecker,
        IBankImportStateRepository stateRepository,
        IBankStatementImportRepository statementRepository,
        IOptions<BankImportWatermarkOptions> options)
        : base(mediator, loggerFactory, statusChecker, stateRepository, statementRepository, options)
    {
    }

    protected override string AccountName => BankAccountNames.ComgateEur;
    protected override DateTime GetTargetEndDate(DateTime today) => today.AddDays(-1);
}
