using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Features.InvoiceClassification.Services;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;

public class ClassifyInvoicesHandler : IRequestHandler<ClassifyInvoicesRequest, ClassifyInvoicesResponse>
{
    // Conservative cap on concurrent Flexi fetches. Promote to IOptions if per-environment tuning is needed.
    private const int MaxFetchConcurrency = 8;

    private readonly IReceivedInvoicesClient _invoicesClient;
    private readonly IInvoiceClassificationService _classificationService;
    private readonly IClassificationRuleRepository _ruleRepository;
    private readonly ILogger<ClassifyInvoicesHandler> _logger;

    public ClassifyInvoicesHandler(
        IReceivedInvoicesClient invoicesClient,
        IInvoiceClassificationService classificationService,
        IClassificationRuleRepository ruleRepository,
        ILogger<ClassifyInvoicesHandler> logger)
    {
        _invoicesClient = invoicesClient;
        _classificationService = classificationService;
        _ruleRepository = ruleRepository;
        _logger = logger;
    }

    public async Task<ClassifyInvoicesResponse> Handle(ClassifyInvoicesRequest request, CancellationToken cancellationToken)
    {
        var response = new ClassifyInvoicesResponse();
        var errorMessages = new List<string>();

        try
        {
            List<ReceivedInvoiceDto> invoicesToClassify;

            if (request.InvoiceIds != null && request.InvoiceIds.Count > 0)
            {
                // Specific invoices mode — fetch in parallel under a SemaphoreSlim throttle.
                // The cancellationToken flows only into throttle.WaitAsync; in-flight Flexi calls
                // are not cancellable because IReceivedInvoicesClient.GetInvoiceByIdAsync has no token overload.
                using var throttle = new SemaphoreSlim(MaxFetchConcurrency, MaxFetchConcurrency);
                var sw = Stopwatch.StartNew();

                var fetchTasks = request.InvoiceIds.Select(id => FetchOneAsync(id, throttle, cancellationToken)).ToList();
                var fetchResults = await Task.WhenAll(fetchTasks);

                sw.Stop();
                _logger.LogDebug(
                    "Fetched {FetchedCount}/{RequestedCount} invoices in {ElapsedMs}ms",
                    fetchResults.Count(r => r.Invoice != null),
                    request.InvoiceIds.Count,
                    sw.ElapsedMilliseconds);

                // Sequential aggregation in input order — preserves FR-5 ordering invariant.
                // Do NOT reorder this loop for "efficiency"; downstream expects input-order errors.
                invoicesToClassify = new List<ReceivedInvoiceDto>(fetchResults.Length);
                foreach (var r in fetchResults)
                {
                    if (r.Invoice != null)
                    {
                        invoicesToClassify.Add(r.Invoice);
                    }
                    else if (r.FetchError != null)
                    {
                        response.Errors++;
                        errorMessages.Add($"Invoice {r.Id}: fetch failed: {r.FetchError}");
                    }
                    else
                    {
                        response.Errors++;
                        errorMessages.Add($"Invoice {r.Id} not found");
                        _logger.LogWarning("Invoice {InvoiceId} not found", r.Id);
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

            foreach (var invoice in invoicesToClassify)
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
                                // Add rule name to error message if available
                                var errorMessage = $"Invoice {invoice.InvoiceNumber}: {result.ErrorMessage}";
                                if (result.RuleId.HasValue)
                                {
                                    var rule = await _ruleRepository.GetByIdAsync(result.RuleId.Value);
                                    if (rule != null)
                                    {
                                        errorMessage = $"Invoice {invoice.InvoiceNumber} (Rule: {rule.Name}): {result.ErrorMessage}";
                                    }
                                }
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

    private async Task<FetchOutcome> FetchOneAsync(string id, SemaphoreSlim throttle, CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var invoice = await _invoicesClient.GetInvoiceByIdAsync(id);
                return new FetchOutcome(id, invoice, FetchError: null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching invoice {InvoiceId}", id);
                return new FetchOutcome(id, Invoice: null, FetchError: ex.Message);
            }
        }
        finally
        {
            throttle.Release();
        }
    }

    private readonly record struct FetchOutcome(string Id, ReceivedInvoiceDto? Invoice, string? FetchError);
}
