using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MeetingTasks.Infrastructure.Jobs;

public class PlaudPollingJob : IRecurringJob
{
    private readonly IPlaudClient _plaudClient;
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IOptions<MeetingTasksOptions> _options;
    private readonly PlaudPollingHealthTracker _tracker;
    private readonly ILogger<PlaudPollingJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "plaud-polling",
        Category = RecurringJobCategory.Integrations,
        DisplayName = "Plaud — pull meeting transcripts",
        Description = "Polls Plaud CLI every 5 minutes for completed recordings, extracts action items via Claude, and stores them as proposed tasks awaiting human review.",
        CronExpression = "*/5 * * * *",
        DefaultIsEnabled = false
    };

    public PlaudPollingJob(
        IPlaudClient plaudClient,
        IMediator mediator,
        IRecurringJobStatusChecker statusChecker,
        IOptions<MeetingTasksOptions> options,
        PlaudPollingHealthTracker tracker,
        ILogger<PlaudPollingJob> logger)
    {
        _plaudClient = plaudClient;
        _mediator = mediator;
        _statusChecker = statusChecker;
        _options = options;
        _tracker = tracker;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        _logger.LogInformation("Starting {JobName}", Metadata.JobName);

        var maxAgeDays = _options.Value.MaxRecordingAgeDays;
        IReadOnlyCollection<PlaudRecordingSummary> readyRecordings;
        try
        {
            readyRecordings = await _plaudClient.ListRecentAsync(maxAgeDays, cancellationToken);
        }
        catch (PlaudUpstreamUnavailableException ex)
        {
            HandleUpstreamFailure(ex);
            return;
        }

        _tracker.Reset();

        _logger.LogInformation("{Ready} recording(s) found to ingest", readyRecordings.Count);

        int ingested = 0;
        int skipped = 0;
        int notGenerated = 0;
        int failed = 0;

        foreach (var recording in readyRecordings)
        {
            try
            {
                var request = new IngestPlaudRecordingRequest
                {
                    PlaudRecordingId = recording.Id,
                    Name = recording.Name,
                    PlaudCreatedAt = recording.CreatedAt
                };

                var response = await _mediator.Send(request, cancellationToken);

                if (response.Skipped)
                {
                    if (response.NotGenerated)
                        notGenerated++;
                    else
                        skipped++;
                }
                else if (!response.Success)
                {
                    failed++;
                }
                else
                {
                    ingested++;
                }
            }
            catch (PlaudUpstreamUnavailableException ex)
            {
                HandleUpstreamFailure(ex);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest recording {RecordingId}", recording.Id);
            }
        }

        _logger.LogInformation(
            "{JobName} complete. {Ingested} new recordings ingested, {Skipped} already known, {NotGenerated} not yet generated, {Failed} failed",
            Metadata.JobName, ingested, skipped, notGenerated, failed);
    }

    private void HandleUpstreamFailure(PlaudUpstreamUnavailableException ex)
    {
        var count = _tracker.RecordUpstreamFailure();
        _logger.LogWarning(
            "Plaud upstream unavailable (HTTP {Status}); will retry on next tick. Consecutive failed runs: {Count}",
            ex.StatusCode, count);

        var threshold = _options.Value.UpstreamFailureAlertThreshold;
        if (threshold > 0 && count % threshold == 0)
        {
            _logger.LogError("Plaud upstream has been unavailable for {Count} consecutive runs", count);
        }
    }
}
