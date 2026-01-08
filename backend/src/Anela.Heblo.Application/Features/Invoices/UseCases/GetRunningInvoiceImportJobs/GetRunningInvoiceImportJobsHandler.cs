using Anela.Heblo.Xcc.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetRunningInvoiceImportJobs;

public class GetRunningInvoiceImportJobsHandler : IRequestHandler<GetRunningInvoiceImportJobsRequest, IList<BackgroundJobInfo>>
{
    private readonly IBackgroundWorker _backgroundWorker;
    private readonly ILogger<GetRunningInvoiceImportJobsHandler> _logger;

    public GetRunningInvoiceImportJobsHandler(
        IBackgroundWorker backgroundWorker,
        ILogger<GetRunningInvoiceImportJobsHandler> logger)
    {
        _backgroundWorker = backgroundWorker;
        _logger = logger;
    }

    public Task<IList<BackgroundJobInfo>> Handle(GetRunningInvoiceImportJobsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var runningJobs = _backgroundWorker.GetRunningJobs();
            var pendingJobs = _backgroundWorker.GetPendingJobs();

            // Filter for invoice import jobs based on job name containing "InvoiceImport"
            var invoiceImportJobs = runningJobs
                .Concat(pendingJobs)
                .Where(job => job.JobName != null && job.JobName.Contains("InvoiceImport", StringComparison.OrdinalIgnoreCase))
                .ToList();

            _logger.LogDebug("Found {Count} running/pending invoice import jobs", invoiceImportJobs.Count);

            return Task.FromResult<IList<BackgroundJobInfo>>(invoiceImportJobs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get running invoice import jobs");
            return Task.FromResult<IList<BackgroundJobInfo>>(new List<BackgroundJobInfo>());
        }
    }
}