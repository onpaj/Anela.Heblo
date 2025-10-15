using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Xcc.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetRunningJobsForGiftPackage;

public class GetRunningJobsForGiftPackageHandler : IRequestHandler<GetRunningJobsForGiftPackageRequest, GetRunningJobsForGiftPackageResponse>
{
    private readonly IBackgroundWorker _backgroundWorker;

    public GetRunningJobsForGiftPackageHandler(IBackgroundWorker backgroundWorker)
    {
        _backgroundWorker = backgroundWorker;
    }

    public Task<GetRunningJobsForGiftPackageResponse> Handle(GetRunningJobsForGiftPackageRequest request, CancellationToken cancellationToken)
    {
        var runningJobs = new List<GiftPackageManufactureJobStatusDto>();

        try
        {
            // Get all running jobs from Hangfire
            var hangfireRunningJobs = _backgroundWorker.GetRunningJobs();

            // Filter for gift package manufacturing jobs
            var giftPackageJobs = hangfireRunningJobs
                .Where(job => IsGiftPackageManufacturingJob(job.JobName))
                .Select(job => new GiftPackageManufactureJobStatusDto
                {
                    JobId = job.Id,
                    Status = job.State,
                    DisplayName = job.JobName,
                    CreatedAt = job.CreatedAt,
                    StartedAt = job.StartedAt
                })
                .ToList();

            runningJobs.AddRange(giftPackageJobs);

            // Also check pending jobs (Enqueued/Scheduled)
            var hangfirePendingJobs = _backgroundWorker.GetPendingJobs();
            var pendingGiftPackageJobs = hangfirePendingJobs
                .Where(job => IsGiftPackageManufacturingJob(job.JobName))
                .Select(job => new GiftPackageManufactureJobStatusDto
                {
                    JobId = job.Id,
                    Status = job.State,
                    DisplayName = job.JobName,
                    CreatedAt = job.CreatedAt,
                    StartedAt = job.StartedAt
                })
                .ToList();

            runningJobs.AddRange(pendingGiftPackageJobs);

            // Filter by specific gift package code if provided
            if (!string.IsNullOrEmpty(request.GiftPackageCode))
            {
                runningJobs = runningJobs
                    .Where(job => job.DisplayName?.Contains(request.GiftPackageCode, StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
            }
        }
        catch (Exception)
        {
            // If Hangfire monitoring fails, return empty list gracefully
            runningJobs = new List<GiftPackageManufactureJobStatusDto>();
        }

        return Task.FromResult(new GetRunningJobsForGiftPackageResponse
        {
            Success = true,
            RunningJobs = runningJobs
        });
    }

    private static bool IsGiftPackageManufacturingJob(string jobName)
    {
        if (string.IsNullOrEmpty(jobName))
            return false;

        // Check if the job name contains gift package manufacturing service and method
        return jobName.Contains("GiftPackageManufactureService", StringComparison.OrdinalIgnoreCase) &&
               jobName.Contains("CreateManufactureAsync", StringComparison.OrdinalIgnoreCase);
    }
}