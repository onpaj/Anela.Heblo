using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetInvoiceDetails;

public class GetInvoiceDetailsHandler : IRequestHandler<GetInvoiceDetailsRequest, GetInvoiceDetailsResponse>
{
    private readonly IReceivedInvoicesClient _invoicesClient;
    private readonly ILogger<GetInvoiceDetailsHandler> _logger;

    public GetInvoiceDetailsHandler(
        IReceivedInvoicesClient invoicesClient,
        ILogger<GetInvoiceDetailsHandler> logger)
    {
        _invoicesClient = invoicesClient;
        _logger = logger;
    }

    public async Task<GetInvoiceDetailsResponse> Handle(GetInvoiceDetailsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Getting details for invoice {InvoiceId}", request.InvoiceId);

            var invoice = await _invoicesClient.GetInvoiceByIdAsync(request.InvoiceId);

            return new GetInvoiceDetailsResponse
            {
                Invoice = invoice,
                Found = invoice != null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoice details for {InvoiceId}", request.InvoiceId);
            return new GetInvoiceDetailsResponse
            {
                Invoice = null,
                Found = false
            };
        }
    }
}