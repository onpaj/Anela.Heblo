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
        _logger.LogInformation("Getting details for invoice {InvoiceId}", request.InvoiceId);

        // GetInvoiceByIdAsync returns null when the invoice does not exist; any other failure
        // propagates to the global exception handler (HTTP 500) rather than masquerading as 404.
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
}
