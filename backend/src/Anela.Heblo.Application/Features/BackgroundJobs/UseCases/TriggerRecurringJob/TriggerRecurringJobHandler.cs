using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;

public class TriggerRecurringJobHandler : IRequestHandler<TriggerRecurringJobRequest, TriggerRecurringJobResponse>
{
    private readonly IEnumerable<IRecurringJob> _jobs;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IHangfireJobEnqueuer _jobEnqueuer;
    private readonly ILogger<TriggerRecurringJobHandler> _logger;

    public TriggerRecurringJobHandler(
        IEnumerable<IRecurringJob> jobs,
        IRecurringJobStatusChecker statusChecker,
        IHangfireJobEnqueuer jobEnqueuer,
        ILogger<TriggerRecurringJobHandler> logger)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _jobEnqueuer = jobEnqueuer ?? throw new ArgumentNullException(nameof(jobEnqueuer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TriggerRecurringJobResponse> Handle(TriggerRecurringJobRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to trigger job {JobName} (forceDisabled={ForceDisabled})",
            request.JobName, request.ForceDisabled);

        // Find the job instance
        var job = _jobs.FirstOrDefault(j => j.Metadata.JobName == request.JobName);

        if (job == null)
        {
            _logger.LogWarning("Job {JobName} not found in registered jobs", request.JobName);
            return new TriggerRecurringJobResponse(
                ErrorCodes.RecurringJobNotFound,
                new Dictionary<string, string>
                {
                    { "jobName", request.JobName },
                    { "forceDisabled", request.ForceDisabled.ToString() }
                }
            );
        }

        // Check if job is enabled (unless forced)
        if (!request.ForceDisabled)
        {
            var isEnabled = await _statusChecker.IsJobEnabledAsync(request.JobName, cancellationToken);
            if (!isEnabled)
            {
                _logger.LogWarning("Job {JobName} is disabled. Use forceDisabled=true to trigger anyway.", request.JobName);
                return new TriggerRecurringJobResponse(
                    ErrorCodes.RecurringJobNotFound,
                    new Dictionary<string, string>
                    {
                        { "jobName", request.JobName },
                        { "forceDisabled", request.ForceDisabled.ToString() }
                    }
                );
            }
        }

        // Enqueue the job for immediate execution via Hangfire
        var jobId = _jobEnqueuer.EnqueueJob(job, cancellationToken);

        if (jobId == null)
        {
            _logger.LogError("Failed to enqueue job {JobName}", request.JobName);
            return new TriggerRecurringJobResponse(
                ErrorCodes.RecurringJobNotFound,
                new Dictionary<string, string>
                {
                    { "jobName", request.JobName },
                    { "forceDisabled", request.ForceDisabled.ToString() }
                }
            );
        }

        _logger.LogInformation("Job {JobName} enqueued with Hangfire job ID: {JobId}", request.JobName, jobId);

        return new TriggerRecurringJobResponse
        {
            JobId = jobId
        };
    }
}
