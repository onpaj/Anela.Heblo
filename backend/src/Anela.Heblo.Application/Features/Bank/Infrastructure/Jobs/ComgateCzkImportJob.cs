using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public class ComgateCzkImportJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<ComgateCzkImportJob> _logger;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-comgate-czk-import",
        DisplayName = "Daily Comgate CZK Import",
        Description = "Imports Comgate CZK payment statements from previous day",
        CronExpression = "30 4 * * *", // Daily at 4:30 AM
        DefaultIsEnabled = true
    };

    public ComgateCzkImportJob(
        IMediator mediator,
        ILogger<ComgateCzkImportJob> logger,
        IRecurringJobStatusChecker statusChecker)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        try
        {
            _logger.LogInformation("Starting {JobName}", Metadata.JobName);

            var yesterdayDate = DateTime.Today.AddDays(-1);
            var request = new ImportBankStatementRequest("ComgateCZK", yesterdayDate);

            var response = await _mediator.Send(request, cancellationToken);

            _logger.LogInformation("{JobName} completed. Imported {Count} statements",
                Metadata.JobName, response.Statements.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
