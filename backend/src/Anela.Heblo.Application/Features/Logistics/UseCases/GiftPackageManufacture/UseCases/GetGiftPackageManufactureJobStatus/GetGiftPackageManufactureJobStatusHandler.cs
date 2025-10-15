using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Xcc.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageManufactureJobStatus;

public class GetGiftPackageManufactureJobStatusHandler : IRequestHandler<GetGiftPackageManufactureJobStatusRequest, GetGiftPackageManufactureJobStatusResponse>
{
    private readonly IBackgroundWorker _backgroundWorker;

    public GetGiftPackageManufactureJobStatusHandler(IBackgroundWorker backgroundWorker)
    {
        _backgroundWorker = backgroundWorker;
    }

    public Task<GetGiftPackageManufactureJobStatusResponse> Handle(GetGiftPackageManufactureJobStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Get real job data from Hangfire
            var jobInfo = _backgroundWorker.GetJobById(request.JobId);

            if (jobInfo == null)
            {
                var errorResponse = new GetGiftPackageManufactureJobStatusResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ResourceNotFound,
                    Params = new Dictionary<string, string> { { "JobId", request.JobId } },
                    JobStatus = new GiftPackageManufactureJobStatusDto
                    {
                        JobId = request.JobId,
                        Status = "NotFound",
                        DisplayName = "Job not found",
                        CreatedAt = null,
                        StartedAt = null,
                        CompletedAt = null,
                        ErrorMessage = $"Job with ID '{request.JobId}' not found."
                    }
                };
                return Task.FromResult(errorResponse);
            }

            // Determine completion time for finished jobs
            DateTime? completedAt = null;
            string? errorMessage = null;

            if (jobInfo.State == "Succeeded" || jobInfo.State == "Failed")
            {
                // For completed jobs, estimate completion time (in real scenario, this would come from Hangfire history)
                completedAt = jobInfo.StartedAt?.AddMinutes(1) ?? jobInfo.CreatedAt?.AddMinutes(2);

                if (jobInfo.State == "Failed")
                {
                    errorMessage = "Job execution failed. Please check logs for details.";
                }
            }

            var jobStatus = new GiftPackageManufactureJobStatusDto
            {
                JobId = jobInfo.Id,
                Status = jobInfo.State,
                DisplayName = jobInfo.JobName,
                CreatedAt = jobInfo.CreatedAt,
                StartedAt = jobInfo.StartedAt,
                CompletedAt = completedAt,
                ErrorMessage = errorMessage
            };

            return Task.FromResult(new GetGiftPackageManufactureJobStatusResponse
            {
                Success = true,
                JobStatus = jobStatus
            });
        }
        catch (Exception ex)
        {
            var errorResponse = new GetGiftPackageManufactureJobStatusResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Exception,
                Params = new Dictionary<string, string> { { "ErrorMessage", ex.Message } },
                JobStatus = new GiftPackageManufactureJobStatusDto
                {
                    JobId = request.JobId,
                    Status = "Error",
                    DisplayName = "Error retrieving job",
                    CreatedAt = null,
                    StartedAt = null,
                    CompletedAt = null,
                    ErrorMessage = ex.Message
                }
            };
            return Task.FromResult(errorResponse);
        }
    }
}