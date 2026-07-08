using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Application.Features.InvoiceClassification.Services;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;

public class ClassifyInvoicesHandler : IRequestHandler<ClassifyInvoicesRequest, ClassifyInvoicesResponse>
{
    private readonly IReceivedInvoicesClient _invoicesClient;
    private readonly IInvoiceClassificationService _classificationService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ClassifyInvoicesHandler> _logger;

    public ClassifyInvoicesHandler(
        IReceivedInvoicesClient invoicesClient,
        IInvoiceClassificationService classificationService,
        ICurrentUserService currentUserService,
        ILogger<ClassifyInvoicesHandler> logger)
    {
        _invoicesClient = invoicesClient;
        _classificationService = classificationService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ClassifyInvoicesResponse> Handle(ClassifyInvoicesRequest request, CancellationToken cancellationToken)
    {
        var response = new ClassifyInvoicesResponse();
        var errorMessages = new List<string>();

        try
        {
            List<ReceivedInvoice> invoicesToClassify;

            if (request.InvoiceIds != null && request.InvoiceIds.Count > 0)
            {
                // Single/specific invoices mode — fetch in parallel
                var fetchTasks = request.InvoiceIds.Select(id => _invoicesClient.GetInvoiceByIdAsync(id)).ToList();
                var fetchedInvoices = await Task.WhenAll(fetchTasks);

                invoicesToClassify = new List<ReceivedInvoice>();
                for (var i = 0; i < request.InvoiceIds.Count; i++)
                {
                    var invoice = fetchedInvoices[i];
                    if (invoice == null)
                    {
                        response.Errors++;
                        errorMessages.Add($"Invoice {request.InvoiceIds[i]} not found");
                        _logger.LogWarning("Invoice {InvoiceId} not found", request.InvoiceIds[i]);
                    }
                    else
                    {
                        invoicesToClassify.Add(invoice);
                    }
                }
                _logger.LogInformation("Starting classification of {Count} specific invoices", invoicesToClassify.Count);
            }
            else
            {
                // Batch mode - all unclassified invoices
                invoicesToClassify = await _invoicesClient.GetUnclassifiedInvoicesAsync();
                _logger.LogInformation("Starting classification of {Count} unclassified invoices", invoicesToClassify.Count);
            }

            response.TotalInvoicesProcessed = invoicesToClassify.Count;

            var currentUser = _currentUserService.GetCurrentUser();
            var processedBy = currentUser.IsAuthenticated
                ? (string.IsNullOrEmpty(currentUser.Name) ? "system" : currentUser.Name)
                : "system";

            foreach (var invoice in invoicesToClassify)
            {
                try
                {
                    var result = await _classificationService.ClassifyInvoiceAsync(invoice, processedBy);

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
                                // Add rule name to error message if available
                                var errorMessage = !string.IsNullOrEmpty(result.RuleName)
                                    ? $"Invoice {invoice.InvoiceNumber} (Rule: {result.RuleName}): {result.ErrorMessage}"
                                    : $"Invoice {invoice.InvoiceNumber}: {result.ErrorMessage}";
                                errorMessages.Add(errorMessage);
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