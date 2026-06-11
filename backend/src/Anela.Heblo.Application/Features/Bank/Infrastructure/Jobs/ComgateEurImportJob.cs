using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

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
        IRecurringJobStatusChecker statusChecker)
        : base(mediator, loggerFactory, statusChecker)
    {
    }

    internal override BankImportJobParameters GetParameters()
    {
        var yesterday = DateTime.Today.AddDays(-1);
        return new BankImportJobParameters(BankAccountNames.ComgateEur, yesterday, yesterday);
    }
}
