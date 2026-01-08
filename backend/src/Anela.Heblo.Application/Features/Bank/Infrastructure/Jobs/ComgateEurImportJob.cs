using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;

public class ComgateEurImportJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<ComgateEurImportJob> _logger;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-comgate-eur-import",
        DisplayName = "Daily Comgate EUR Import",
        Description = "Imports Comgate EUR payment statements from previous day",
        CronExpression = "40 4 * * *", // Daily at 4:40 AM
        DefaultIsEnabled = true
    };

    public ComgateEurImportJob(
        IMediator mediator,
        ILogger<ComgateEurImportJob> logger,
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
            var request = new ImportBankStatementRequest("ComgateEUR", yesterdayDate);

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
