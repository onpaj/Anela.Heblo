using Anela.Heblo.Application.Features.Invoice.Contracts;
using Anela.Heblo.Domain.Features.Invoice;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoice.UseCases.SearchInvoices;

public class SearchInvoicesHandler : IRequestHandler<SearchInvoicesRequest, SearchInvoicesResponse>
{
    private readonly IIssuedInvoiceRepository _repository;
    private readonly ILogger<SearchInvoicesHandler> _logger;

    public SearchInvoicesHandler(
        IIssuedInvoiceRepository repository,
        ILogger<SearchInvoicesHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<SearchInvoicesResponse> Handle(SearchInvoicesRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Searching invoices with term: {SearchTerm}", request.SearchTerm);

        try
        {
            var invoices = await _repository.SearchInvoicesAsync(request.SearchTerm);
            
            return new SearchInvoicesResponse
            {
                Invoices = invoices.Select(MapToDto).ToList(),
                SearchTerm = request.SearchTerm,
                ResultCount = invoices.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching invoices with term: {SearchTerm}", request.SearchTerm);
            
            return new SearchInvoicesResponse(Application.Shared.ErrorCodes.InternalServerError)
            {
                SearchTerm = request.SearchTerm
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