using Anela.Heblo.Xcc.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetInvoiceImportJobStatus;

public class GetInvoiceImportJobStatusHandler : IRequestHandler<GetInvoiceImportJobStatusRequest, BackgroundJobInfo?>
{
    private readonly IBackgroundWorker _backgroundWorker;
    private readonly ILogger<GetInvoiceImportJobStatusHandler> _logger;

    public GetInvoiceImportJobStatusHandler(
        IBackgroundWorker backgroundWorker,
        ILogger<GetInvoiceImportJobStatusHandler> logger)
    {
        _backgroundWorker = backgroundWorker;
        _logger = logger;
    }

    public Task<BackgroundJobInfo?> Handle(GetInvoiceImportJobStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var jobInfo = _backgroundWorker.GetJobById(request.JobId);
            _logger.LogDebug("Retrieved job status for JobId: {JobId}, Status: {Status}", 
                request.JobId, jobInfo?.State ?? "Not Found");
            
            return Task.FromResult(jobInfo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get job status for JobId: {JobId}", request.JobId);
            return Task.FromResult<BackgroundJobInfo?>(null);
        }
    }
}