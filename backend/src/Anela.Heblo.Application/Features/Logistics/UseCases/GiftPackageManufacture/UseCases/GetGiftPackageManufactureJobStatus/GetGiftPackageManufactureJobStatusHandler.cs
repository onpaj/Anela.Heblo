using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Hangfire;
using Hangfire.Storage;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageManufactureJobStatus;

public class GetGiftPackageManufactureJobStatusHandler : IRequestHandler<GetGiftPackageManufactureJobStatusRequest, GetGiftPackageManufactureJobStatusResponse>
{
    public async Task<GetGiftPackageManufactureJobStatusResponse> Handle(GetGiftPackageManufactureJobStatusRequest request, CancellationToken cancellationToken)
    {
        using var connection = JobStorage.Current.GetConnection();
        var jobData = connection.GetJobData(request.JobId);

        if (jobData == null)
        {
            return new GetGiftPackageManufactureJobStatusResponse
            {
                IsSuccess = false,
                Message = $"Job with ID '{request.JobId}' not found"
            };
        }

        var history = connection.GetStateHistory(request.JobId);
        var createdState = history.FirstOrDefault(x => x.Name == "Enqueued");
        var processingState = history.FirstOrDefault(x => x.Name == "Processing");
        var completedState = history.Where(x => x.Name == "Succeeded" || x.Name == "Failed").OrderByDescending(x => x.CreatedAt).FirstOrDefault();

        var jobStatus = new GiftPackageManufactureJobStatusDto
        {
            JobId = request.JobId,
            Status = jobData.State,
            CreatedAt = createdState?.CreatedAt,
            StartedAt = processingState?.CreatedAt,
            CompletedAt = completedState?.CreatedAt,
            ErrorMessage = jobData.State == "Failed" ? completedState?.Reason : null
        };

        return new GetGiftPackageManufactureJobStatusResponse
        {
            IsSuccess = true,
            JobStatus = jobStatus
        };
    }
}