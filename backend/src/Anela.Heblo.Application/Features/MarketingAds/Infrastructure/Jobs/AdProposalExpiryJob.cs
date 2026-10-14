using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ExpireAdProposals;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;

/// <summary>Expires Pending ad proposals past their TTL and re-enqueues Approved ones whose execution never started.</summary>
public sealed class AdProposalExpiryJob : IRecurringJob
{
    private const int LockTimeoutSeconds = 300;

    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<AdProposalExpiryJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public AdProposalExpiryJob(IMediator mediator, IRecurringJobStatusChecker statusChecker,
        IOptions<AdProposalOptions> options, ILogger<AdProposalExpiryJob> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var proposalOptions = (options ?? throw new ArgumentNullException(nameof(options))).Value;

        // Never throws on bad settings: job discovery constructs every job, and a throw would unschedule all of
        // them. A bad cron falls back to the default here; the handler's EnsureValid reports it at run time.
        if (proposalOptions.ExpiryCronOrDefault != proposalOptions.ExpiryCronExpression)
        {
            _logger.LogWarning("Ads:ExpiryCronExpression '{Cron}' is invalid; scheduling {JobName} with the default {Default}",
                proposalOptions.ExpiryCronExpression, "ads-proposal-expiry", AdProposalOptions.DefaultExpiryCronExpression);
        }

        Metadata = new RecurringJobMetadata
        {
            JobName = "ads-proposal-expiry",
            DisplayName = "Ads Proposal Expiry",
            Description = "Expires pending ad-change proposals past their time to live and re-enqueues approved proposals whose execution never started.",
            CronExpression = proposalOptions.ExpiryCronOrDefault,
            DefaultIsEnabled = true,
            TimeZoneId = proposalOptions.ExpiryTimeZone,
            Category = RecurringJobCategory.Marketing,
        };
    }

    [DisableConcurrentExecution(LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken, Metadata.DefaultIsEnabled))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        await _mediator.Send(new ExpireAdProposalsRequest(), cancellationToken);
    }
}
