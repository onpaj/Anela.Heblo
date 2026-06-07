using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

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
        IRecurringJobStatusChecker statusChecker)
        : base(mediator, loggerFactory, statusChecker)
    {
    }

    internal override BankImportJobParameters GetParameters()
    {
        var yesterday = DateTime.Today.AddDays(-1);
        return new BankImportJobParameters(BankAccountNames.ComgateCzk, yesterday, yesterday);
    }
}
