using Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.Infrastructure.Jobs;

public class DailyConsumptionJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<DailyConsumptionJob> _logger;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-consumption-calculation",
        DisplayName = "Daily Consumption Calculation",
        Description = "Calculates daily consumption of packing materials",
        CronExpression = "0 3 * * *", // Daily at 3:00 AM
        DefaultIsEnabled = true
    };

    public DailyConsumptionJob(
        IMediator mediator,
        ILogger<DailyConsumptionJob> logger,
        IRecurringJobStatusChecker statusChecker)
    {
        _mediator = mediator;
        _logger = logger;
        _statusChecker = statusChecker;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        var processingDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)); // Process previous day

        _logger.LogInformation("Starting {JobName} for {Date}", Metadata.JobName, processingDate);

        try
        {
            var request = new ProcessDailyConsumptionRequest
            {
                ProcessingDate = processingDate
            };

            var result = await _mediator.Send(request, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("{JobName} completed successfully for {Date}: {Message}",
                    Metadata.JobName, processingDate, result.Message);
            }
            else
            {
                _logger.LogWarning("{JobName} completed with warnings for {Date}: {Message}",
                    Metadata.JobName, processingDate, result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed for {Date}", Metadata.JobName, processingDate);
            throw; // Re-throw to let Hangfire handle retry logic
        }
    }
}