using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetInvoiceDetails;

public class GetInvoiceDetailsHandler : IRequestHandler<GetInvoiceDetailsRequest, GetInvoiceDetailsResponse>
{
    private readonly IReceivedInvoicesClient _invoicesClient;
    private readonly ILogger<GetInvoiceDetailsHandler> _logger;
    private readonly IMapper _mapper;

    public GetInvoiceDetailsHandler(
        IReceivedInvoicesClient invoicesClient,
        ILogger<GetInvoiceDetailsHandler> logger,
        IMapper mapper)
    {
        _invoicesClient = invoicesClient;
        _logger = logger;
        _mapper = mapper;
    }

    public async Task<GetInvoiceDetailsResponse> Handle(GetInvoiceDetailsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Getting details for invoice {InvoiceId}", request.InvoiceId);

            var invoice = await _invoicesClient.GetInvoiceByIdAsync(request.InvoiceId);

            // Explicit null check: AutoMapper would otherwise allocate an empty destination,
            // breaking the API contract that returns `Invoice = null` when not found.
            var mapped = invoice is null ? null : _mapper.Map<Contracts.ReceivedInvoiceDto>(invoice);

            return new GetInvoiceDetailsResponse
            {
                Invoice = mapped,
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