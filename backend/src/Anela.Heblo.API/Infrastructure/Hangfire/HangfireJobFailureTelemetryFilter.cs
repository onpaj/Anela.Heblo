using Anela.Heblo.Xcc.Telemetry;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

/// <summary>
/// Reports Hangfire jobs that end in <see cref="FailedState"/> (i.e. automatic retries
/// exhausted) to Application Insights as a "HangfireJobFailed" custom event plus a tracked
/// exception, so external telemetry-scanning agents can pick them up. Intermediate failed
/// attempts that will be retried never reach FailedState and are intentionally not reported.
/// </summary>
public sealed class HangfireJobFailureTelemetryFilter : JobFilterAttribute, IApplyStateFilter
{
    public const string EventName = "HangfireJobFailed";

    private readonly ITelemetryService _telemetryService;

    public HangfireJobFailureTelemetryFilter(ITelemetryService telemetryService)
    {
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
    }

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is not FailedState failedState)
        {
            return;
        }

        try
        {
            var backgroundJob = context.BackgroundJob;
            var properties = new Dictionary<string, string>
            {
                ["JobId"] = backgroundJob.Id,
                ["JobType"] = backgroundJob.Job.Type.FullName ?? backgroundJob.Job.Type.Name,
                ["JobMethod"] = backgroundJob.Job.Method.Name,
                ["ExceptionType"] = failedState.Exception.GetType().FullName ?? failedState.Exception.GetType().Name,
                ["ExceptionMessage"] = failedState.Exception.Message,
            };

            var recurringJobId = context.Connection?.GetJobParameter(backgroundJob.Id, "RecurringJobId");
            if (!string.IsNullOrEmpty(recurringJobId))
            {
                // Hangfire stores job parameters JSON-serialized, so string values arrive quoted
                properties["RecurringJobId"] = recurringJobId.Trim('"');
            }

            var retryCount = context.Connection?.GetJobParameter(backgroundJob.Id, "RetryCount");
            if (!string.IsNullOrEmpty(retryCount))
            {
                properties["RetryCount"] = retryCount;
            }

            _telemetryService.TrackBusinessEvent(EventName, properties);
            _telemetryService.TrackException(failedState.Exception, properties);
        }
        catch
        {
            // Telemetry must never disturb Hangfire's state machine
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}
