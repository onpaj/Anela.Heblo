using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Campaigns.Infrastructure.Jobs;

public class SyncMetaAdsJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<SyncMetaAdsJob> _logger;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-meta-ads-sync",
        DisplayName = "Daily Meta Ads Sync",
        Description = "Syncs campaigns, ad sets, ads, and daily metrics from Meta Ads (Facebook/Instagram)",
        CronExpression = "0 5 * * *", // Daily at 5:00 AM
        DefaultIsEnabled = true
    };

    public SyncMetaAdsJob(
        IMediator mediator,
        ILogger<SyncMetaAdsJob> logger,
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
            await _mediator.Send(new SyncMetaAdsRequest(), cancellationToken);
            _logger.LogInformation("{JobName} completed successfully", Metadata.JobName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
