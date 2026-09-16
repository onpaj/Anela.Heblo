using Anela.Heblo.Application.Features.Attendance.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Attendance.UseCases.RunBreakInsertion;

public class RunBreakInsertionHandler : IRequestHandler<RunBreakInsertionRequest, RunBreakInsertionResponse>
{
    private readonly BreakInsertionService _service;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<RunBreakInsertionHandler> _logger;

    public RunBreakInsertionHandler(
        BreakInsertionService service,
        IRecurringJobStatusChecker statusChecker,
        ILogger<RunBreakInsertionHandler> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RunBreakInsertionResponse> Handle(
        RunBreakInsertionRequest request, CancellationToken cancellationToken)
    {
        // Running on demand must respect the same off-switch as the schedule: the job writes to a
        // live external account, and being disabled is how that gets stopped.
        var isEnabled = await _statusChecker.IsJobEnabledAsync(
            BreakInsertionJob.Name, cancellationToken, BreakInsertionJob.DefaultEnabled);

        if (!isEnabled)
        {
            _logger.LogWarning(
                "On-demand break insertion refused: job {JobName} is disabled.", BreakInsertionJob.Name);

            return new RunBreakInsertionResponse(
                ErrorCodes.RecurringJobDisabled,
                new Dictionary<string, string> { { "jobName", BreakInsertionJob.Name } });
        }

        _logger.LogInformation(
            "Break insertion requested on demand for window {FromDaysAgo}–{ToDaysAgo} days ago",
            request.FromDaysAgo?.ToString() ?? "(configured lookback)",
            request.ToDaysAgo?.ToString() ?? "0");

        try
        {
            var summary = await _service.RunAsync(
                request.FromDaysAgo, request.ToDaysAgo, cancellationToken);

            return new RunBreakInsertionResponse
            {
                DaysScanned = summary.DaysScanned,
                BreaksInserted = summary.BreaksInserted,
                DaysHealed = summary.DaysHealed,
                RecordsTouched = summary.RecordsTouched,
                TouchFailed = summary.TouchFailed,
                SkippedExistingBreak = summary.SkippedExistingBreak,
                SkippedInProgress = summary.SkippedInProgress,
                SkippedBelowThreshold = summary.SkippedBelowThreshold,
                SkippedHoursOnly = summary.SkippedHoursOnly,
                SkippedNoSlot = summary.SkippedNoSlot,
                Failed = summary.Failed
            };
        }
        catch (BreakInsertionAlreadyRunningException)
        {
            _logger.LogWarning(
                "On-demand break insertion refused: a run is already in progress.");

            return new RunBreakInsertionResponse(
                ErrorCodes.RecurringJobAlreadyRunning,
                new Dictionary<string, string> { { "jobName", BreakInsertionJob.Name } });
        }
        catch (InvalidOperationException ex)
        {
            // Raised when the configured break activity is missing from Logeto or is not a Break.
            _logger.LogError(ex, "Break insertion failed: the walk could not be configured.");

            return new RunBreakInsertionResponse(
                ErrorCodes.ConfigurationError,
                new Dictionary<string, string> { { "message", ex.Message } });
        }
        catch (Exception ex)
        {
            // Everything reaching here came from Logeto: the per-day guard inside the walk already
            // absorbs failures it can retry, so what is left is the account-level calls failing.
            // The concrete exception type lives in the adapter, which this layer cannot reference.
            _logger.LogError(ex, "Break insertion failed: Logeto returned an error.");

            return new RunBreakInsertionResponse(
                ErrorCodes.ExternalServiceError,
                new Dictionary<string, string> { { "message", ex.Message } });
        }
    }
}
