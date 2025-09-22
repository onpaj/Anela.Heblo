using Anela.Heblo.Application.Features.Invoice.Contracts;
using Anela.Heblo.Domain.Features.Invoice;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoice.UseCases.GetInvoiceDetail;

public class GetInvoiceDetailHandler : IRequestHandler<GetInvoiceDetailRequest, GetInvoiceDetailResponse>
{
    private readonly IIssuedInvoiceRepository _repository;
    private readonly ILogger<GetInvoiceDetailHandler> _logger;

    public GetInvoiceDetailHandler(
        IIssuedInvoiceRepository repository,
        ILogger<GetInvoiceDetailHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetInvoiceDetailResponse> Handle(GetInvoiceDetailRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting invoice detail for external ID: {ExternalId}", request.ExternalId);

        try
        {
            var invoice = await _repository.GetInvoiceDetailAsync(request.ExternalId);
            if (invoice == null)
            {
                _logger.LogWarning("Invoice not found with external ID: {ExternalId}", request.ExternalId);
                
                return new GetInvoiceDetailResponse(Application.Shared.ErrorCodes.ResourceNotFound, 
                    new Dictionary<string, string> { { "externalId", request.ExternalId } });
            }

            return new GetInvoiceDetailResponse
            {
                ExternalId = invoice.ExternalId,
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceDate = invoice.InvoiceDate,
                Amount = invoice.Amount,
                Currency = invoice.Currency,
                CustomerName = invoice.CustomerName,
                CustomerEmail = invoice.CustomerEmail,
                Description = invoice.Description,
                CreatedAt = invoice.CreatedAt,
                LastModifiedAt = invoice.LastModifiedAt,
                IsSuccessfullyImported = invoice.IsSuccessfullyImported,
                ImportAttempts = invoice.ImportAttempts
                    .OrderByDescending(a => a.AttemptedAt)
                    .Select(a => new ImportAttemptDto
                    {
                        Id = a.Id,
                        ExternalInvoiceId = a.ExternalInvoiceId,
                        AttemptedAt = a.AttemptedAt,
                        IsSuccess = a.IsSuccess,
                        ErrorMessage = a.ErrorMessage,
                        ImportId = a.ImportId,
                        InvoiceNumber = a.InvoiceNumber,
                        Amount = a.Amount,
                        InvoiceDate = a.InvoiceDate,
                        Currency = a.Currency
                    }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoice detail for external ID: {ExternalId}", request.ExternalId);
            
            return new GetInvoiceDetailResponse(Application.Shared.ErrorCodes.InternalServerError);
        }
    }
}