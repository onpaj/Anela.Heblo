using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Features.InvoiceClassification.Services;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;

public class ClassifyInvoicesHandler : IRequestHandler<ClassifyInvoicesRequest, ClassifyInvoicesResponse>
{
    private readonly IReceivedInvoicesClient _invoicesClient;
    private readonly IInvoiceClassificationService _classificationService;
    private readonly ILogger<ClassifyInvoicesHandler> _logger;

    public ClassifyInvoicesHandler(
        IReceivedInvoicesClient invoicesClient,
        IInvoiceClassificationService classificationService,
        ILogger<ClassifyInvoicesHandler> logger)
    {
        _invoicesClient = invoicesClient;
        _classificationService = classificationService;
        _logger = logger;
    }

    public async Task<ClassifyInvoicesResponse> Handle(ClassifyInvoicesRequest request, CancellationToken cancellationToken)
    {
        var response = new ClassifyInvoicesResponse();
        var errorMessages = new List<string>();

        try
        {
            var unclassifiedInvoices = await _invoicesClient.GetUnclassifiedInvoicesAsync();
            response.TotalInvoicesProcessed = unclassifiedInvoices.Count;

            _logger.LogInformation("Starting classification of {Count} unclassified invoices", unclassifiedInvoices.Count);

            foreach (var invoice in unclassifiedInvoices)
            {
                try
                {
                    var result = await _classificationService.ClassifyInvoiceAsync(invoice);
                    
                    switch (result.Result)
                    {
                        case ClassificationResult.Success:
                            response.SuccessfulClassifications++;
                            break;
                        case ClassificationResult.ManualReviewRequired:
                            response.ManualReviewRequired++;
                            break;
                        case ClassificationResult.Error:
                            response.Errors++;
                            if (!string.IsNullOrEmpty(result.ErrorMessage))
                            {
                                errorMessages.Add($"Invoice {invoice.InvoiceNumber}: {result.ErrorMessage}");
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    response.Errors++;
                    var errorMessage = $"Invoice {invoice.InvoiceNumber}: {ex.Message}";
                    errorMessages.Add(errorMessage);
                    _logger.LogError(ex, "Error classifying invoice {InvoiceId}", invoice.InvoiceNumber);
                }
            }

            response.ErrorMessages = errorMessages;
            
            _logger.LogInformation("Classification completed. Success: {Success}, Manual Review: {ManualReview}, Errors: {Errors}",
                response.SuccessfulClassifications, response.ManualReviewRequired, response.Errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during invoice classification process");
            response.ErrorMessages.Add($"Classification process error: {ex.Message}");
        }

        return response;
    }
}