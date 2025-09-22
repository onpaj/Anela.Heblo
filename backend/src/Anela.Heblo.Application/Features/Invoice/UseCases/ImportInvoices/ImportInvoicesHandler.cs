using Anela.Heblo.Application.Features.Invoice.Contracts;
using Anela.Heblo.Application.Features.Invoice.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoice.UseCases.ImportInvoices;

public class ImportInvoicesHandler : IRequestHandler<ImportInvoicesRequest, ImportInvoicesResponse>
{
    private readonly IInvoiceImportService _importService;
    private readonly ILogger<ImportInvoicesHandler> _logger;

    public ImportInvoicesHandler(
        IInvoiceImportService importService,
        ILogger<ImportInvoicesHandler> logger)
    {
        _importService = importService;
        _logger = logger;
    }

    public async Task<ImportInvoicesResponse> Handle(ImportInvoicesRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting synchronous invoice import with criteria: {@Criteria}", request.Criteria);

        try
        {
            var result = await _importService.ImportBatchAsync(request.Criteria, cancellationToken);
            
            return new ImportInvoicesResponse
            {
                ProcessedCount = result.ProcessedCount,
                SuccessCount = result.SuccessCount,
                FailedCount = result.FailedCount,
                ImportAttempts = result.Attempts.Select(a => new ImportAttemptDto
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
            _logger.LogError(ex, "Error during invoice import");
            
            return new ImportInvoicesResponse(Application.Shared.ErrorCodes.InternalServerError)
            {
                ProcessedCount = 0,
                SuccessCount = 0,
                FailedCount = 0
            };
        }
    }
}