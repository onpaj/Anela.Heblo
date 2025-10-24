using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Xcc.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingJobStatus;

public class GetStockTakingJobStatusHandler : IRequestHandler<GetStockTakingJobStatusRequest, GetStockTakingJobStatusResponse>
{
    private readonly IBackgroundWorker _backgroundWorker;

    public GetStockTakingJobStatusHandler(IBackgroundWorker backgroundWorker)
    {
        _backgroundWorker = backgroundWorker;
    }

    public async Task<GetStockTakingJobStatusResponse> Handle(GetStockTakingJobStatusRequest request, CancellationToken cancellationToken)
    {
        var job = _backgroundWorker.GetJobById(request.JobId);

        if (job == null)
        {
            return new GetStockTakingJobStatusResponse
            {
                JobId = request.JobId,
                Status = "NotFound",
                IsCompleted = true,
                IsSucceeded = false,
                IsFailed = true,
                ErrorMessage = "Job not found"
            };
        }

        // Determine completion and success based on state
        var isCompleted = job.State == "Succeeded" || job.State == "Failed";
        var isSucceeded = job.State == "Succeeded";
        var isFailed = job.State == "Failed";
        
        string? errorMessage = null;
        if (isFailed)
        {
            errorMessage = "Job execution failed. Please check logs for details.";
        }

        return new GetStockTakingJobStatusResponse
        {
            JobId = request.JobId,
            Status = job.State,
            IsCompleted = isCompleted,
            IsSucceeded = isSucceeded,
            IsFailed = isFailed,
            ErrorMessage = errorMessage,
            Result = null // For now, we don't return the actual result here - could be added later if needed
        };
    }
}