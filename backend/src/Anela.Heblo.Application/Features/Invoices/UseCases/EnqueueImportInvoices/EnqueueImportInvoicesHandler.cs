using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Xcc.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.EnqueueImportInvoices;

public class EnqueueImportInvoicesHandler : IRequestHandler<EnqueueImportInvoicesRequest, EnqueueImportInvoicesResponse>
{
    private readonly IBackgroundWorker _backgroundWorker;
    private readonly ILogger<EnqueueImportInvoicesHandler> _logger;

    public EnqueueImportInvoicesHandler(
        IBackgroundWorker backgroundWorker,
        ILogger<EnqueueImportInvoicesHandler> logger)
    {
        _backgroundWorker = backgroundWorker;
        _logger = logger;
    }

    public Task<EnqueueImportInvoicesResponse> Handle(EnqueueImportInvoicesRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Enqueuing invoice import job with RequestId: {RequestId}", request.Query.RequestId);

        // Generate descriptive job name
        string description;
        if (request.Query.QueryByInvoice)
        {
            description = $"faktura {request.Query.InvoiceId}";
        }
        else if (request.Query.QueryByDate)
        {
            description = $"{request.Query.DateFromString} - {request.Query.DateToString}";
        }
        else
        {
            description = "obecný import";
        }

        var jobId = _backgroundWorker.Enqueue<IInvoiceImportService>(
            service => service.ImportInvoicesAsync(description, request.Query, cancellationToken));

        _logger.LogInformation("Invoice import job enqueued with JobId: {JobId}, Description: {Description}", jobId, description);

        return Task.FromResult(new EnqueueImportInvoicesResponse { JobId = jobId });
    }
}