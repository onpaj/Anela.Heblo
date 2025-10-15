using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Hangfire;
using Hangfire.Storage;
using Hangfire.Common;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetRunningJobsForGiftPackage;

public class GetRunningJobsForGiftPackageHandler : IRequestHandler<GetRunningJobsForGiftPackageRequest, GetRunningJobsForGiftPackageResponse>
{
    public async Task<GetRunningJobsForGiftPackageResponse> Handle(GetRunningJobsForGiftPackageRequest request, CancellationToken cancellationToken)
    {
        using var connection = JobStorage.Current.GetConnection();
        
        var runningJobs = new List<GiftPackageManufactureJobStatusDto>();

        // Get enqueued jobs
        var enqueuedJobs = connection.GetEnqueuedJobs("default", 0, 1000);
        foreach (var job in enqueuedJobs)
        {
            if (IsGiftPackageManufactureJob(job.Value, request.GiftPackageCode))
            {
                runningJobs.Add(CreateJobStatusDto(job.Key, job.Value, connection));
            }
        }

        // Get processing jobs
        var processingJobs = connection.GetProcessingJobs(0, 1000);
        foreach (var job in processingJobs)
        {
            if (IsGiftPackageManufactureJob(job.Value, request.GiftPackageCode))
            {
                runningJobs.Add(CreateJobStatusDto(job.Key, job.Value, connection));
            }
        }

        return new GetRunningJobsForGiftPackageResponse
        {
            IsSuccess = true,
            RunningJobs = runningJobs
        };
    }

    private static bool IsGiftPackageManufactureJob(JobDto job, string giftPackageCode)
    {
        // Check if this is a CreateManufactureAsync call for the specific gift package
        if (job.Job?.Method?.Name == "CreateManufactureAsync")
        {
            var arguments = job.Job.Args;
            if (arguments?.Count > 0 && arguments[0]?.ToString() == giftPackageCode)
            {
                return true;
            }
        }
        return false;
    }

    private static GiftPackageManufactureJobStatusDto CreateJobStatusDto(string jobId, JobDto jobData, IStorageConnection connection)
    {
        var history = connection.GetStateHistory(jobId);
        var createdState = history.FirstOrDefault(x => x.Name == "Enqueued");
        var processingState = history.FirstOrDefault(x => x.Name == "Processing");

        return new GiftPackageManufactureJobStatusDto
        {
            JobId = jobId,
            Status = jobData.State,
            CreatedAt = createdState?.CreatedAt,
            StartedAt = processingState?.CreatedAt
        };
    }
}