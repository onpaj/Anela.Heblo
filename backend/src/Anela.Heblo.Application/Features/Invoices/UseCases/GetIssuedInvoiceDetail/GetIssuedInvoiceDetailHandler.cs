using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceDetail;

/// <summary>
/// Handler for getting detailed information about an issued invoice
/// </summary>
public class GetIssuedInvoiceDetailHandler : IRequestHandler<GetIssuedInvoiceDetailRequest, GetIssuedInvoiceDetailResponse>
{
    private readonly IIssuedInvoiceRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetIssuedInvoiceDetailHandler> _logger;

    public GetIssuedInvoiceDetailHandler(
        IIssuedInvoiceRepository repository,
        IMapper mapper,
        ILogger<GetIssuedInvoiceDetailHandler> logger)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<GetIssuedInvoiceDetailResponse> Handle(GetIssuedInvoiceDetailRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Id))
            {
                return new GetIssuedInvoiceDetailResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ValidationError,
                    Params = new Dictionary<string, string> { { "ErrorMessage", "ID faktury je povinné" } }
                };
            }

            _logger.LogInformation("Getting detailed information for issued invoice: {InvoiceId}", request.Id);

            // Get invoice with sync history
            var invoice = await _repository.GetByIdWithSyncHistoryAsync(request.Id, cancellationToken);

            if (invoice == null)
            {
                _logger.LogWarning("Issued invoice not found: {InvoiceId}", request.Id);
                return new GetIssuedInvoiceDetailResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ResourceNotFound,
                    Params = new Dictionary<string, string> { { "ErrorMessage", "Faktura nebyla nalezena" } }
                };
            }

            // Map to detailed DTO
            var invoiceDto = _mapper.Map<IssuedInvoiceDetailDto>(invoice);

            _logger.LogInformation("Retrieved detailed information for issued invoice: {InvoiceId} with {SyncHistoryCount} sync records", 
                request.Id, invoice.SyncHistoryCount);

            return new GetIssuedInvoiceDetailResponse
            {
                Invoice = invoiceDto,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting issued invoice detail for ID: {InvoiceId}", request.Id);
            return new GetIssuedInvoiceDetailResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Exception,
                Params = new Dictionary<string, string> { { "ErrorMessage", "Chyba při načítání detailu faktury" } }
            };
        }
    }
}