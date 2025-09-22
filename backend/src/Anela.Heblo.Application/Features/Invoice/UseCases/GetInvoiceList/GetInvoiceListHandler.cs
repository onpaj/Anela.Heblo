using Anela.Heblo.Application.Features.Invoice.Contracts;
using Anela.Heblo.Domain.Features.Invoice;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoice.UseCases.GetInvoiceList;

public class GetInvoiceListHandler : IRequestHandler<GetInvoiceListRequest, GetInvoiceListResponse>
{
    private readonly IIssuedInvoiceRepository _repository;
    private readonly ILogger<GetInvoiceListHandler> _logger;

    public GetInvoiceListHandler(
        IIssuedInvoiceRepository repository,
        ILogger<GetInvoiceListHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetInvoiceListResponse> Handle(GetInvoiceListRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting invoice list for page {Page}, size {PageSize}", request.Page, request.PageSize);

        try
        {
            var invoices = await _repository.GetImportedInvoicesAsync(request.Page, request.PageSize);
            var totalCount = await _repository.GetTotalInvoicesCountAsync();
            
            return new GetInvoiceListResponse
            {
                Invoices = invoices.Select(MapToDto).ToList(),
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoice list");
            
            return new GetInvoiceListResponse(Application.Shared.ErrorCodes.InternalServerError)
            {
                Page = request.Page,
                PageSize = request.PageSize
            };
        }
    }

    private static InvoiceDto MapToDto(IssuedInvoice invoice)
    {
        return new InvoiceDto
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
            LatestImportAttempt = invoice.LatestImportAttempt != null ? new ImportAttemptDto
            {
                Id = invoice.LatestImportAttempt.Id,
                ExternalInvoiceId = invoice.LatestImportAttempt.ExternalInvoiceId,
                AttemptedAt = invoice.LatestImportAttempt.AttemptedAt,
                IsSuccess = invoice.LatestImportAttempt.IsSuccess,
                ErrorMessage = invoice.LatestImportAttempt.ErrorMessage,
                ImportId = invoice.LatestImportAttempt.ImportId,
                InvoiceNumber = invoice.LatestImportAttempt.InvoiceNumber,
                Amount = invoice.LatestImportAttempt.Amount,
                InvoiceDate = invoice.LatestImportAttempt.InvoiceDate,
                Currency = invoice.LatestImportAttempt.Currency
            } : null
        };
    }
}