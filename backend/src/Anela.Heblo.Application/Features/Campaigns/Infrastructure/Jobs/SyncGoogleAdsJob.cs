using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Campaigns.Infrastructure.Jobs;

public class SyncGoogleAdsJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<SyncGoogleAdsJob> _logger;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-google-ads-sync",
        DisplayName = "Daily Google Ads Sync",
        Description = "Syncs campaigns, ad groups, ads, and daily metrics from Google Ads",
        CronExpression = "15 5 * * *",
        DefaultIsEnabled = true,
    };

    public SyncGoogleAdsJob(
        IMediator mediator,
        ILogger<SyncGoogleAdsJob> logger,
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

        try
        {
            _logger.LogInformation("Starting {JobName}", Metadata.JobName);
            await _mediator.Send(new SyncGoogleAdsRequest(), cancellationToken);
            _logger.LogInformation("{JobName} completed successfully", Metadata.JobName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
