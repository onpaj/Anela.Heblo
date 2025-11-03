using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Features.InvoiceClassification.Services;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifySingleInvoice;

public class ClassifySingleInvoiceHandler : IRequestHandler<ClassifySingleInvoiceRequest, ClassifySingleInvoiceResponse>
{
    private readonly IReceivedInvoicesClient _invoicesClient;
    private readonly IInvoiceClassificationService _classificationService;
    private readonly IClassificationRuleRepository _ruleRepository;
    private readonly ILogger<ClassifySingleInvoiceHandler> _logger;

    public ClassifySingleInvoiceHandler(
        IReceivedInvoicesClient invoicesClient,
        IInvoiceClassificationService classificationService,
        IClassificationRuleRepository ruleRepository,
        ILogger<ClassifySingleInvoiceHandler> logger)
    {
        _invoicesClient = invoicesClient;
        _classificationService = classificationService;
        _ruleRepository = ruleRepository;
        _logger = logger;
    }

    public async Task<ClassifySingleInvoiceResponse> Handle(ClassifySingleInvoiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting classification for invoice {InvoiceId}", request.InvoiceId);

            // Get invoice details
            var invoice = await _invoicesClient.GetInvoiceByIdAsync(request.InvoiceId);
            if (invoice == null)
            {
                _logger.LogWarning("Invoice {InvoiceId} not found", request.InvoiceId);
                return new ClassifySingleInvoiceResponse
                {
                    Success = false,
                    Result = ClassificationResult.Error,
                    ErrorMessage = $"Invoice {request.InvoiceId} not found"
                };
            }

            // Classify the invoice
            var result = await _classificationService.ClassifyInvoiceAsync(invoice);

            // Get rule name if a rule was applied
            string? appliedRuleName = null;
            if (result.RuleId.HasValue)
            {
                var rule = await _ruleRepository.GetByIdAsync(result.RuleId.Value);
                appliedRuleName = rule?.Name;
            }

            _logger.LogInformation("Classification completed for invoice {InvoiceId} with result {Result}", 
                request.InvoiceId, result.Result);

            return new ClassifySingleInvoiceResponse
            {
                Success = result.Result != ClassificationResult.Error,
                Result = result.Result,
                AppliedRule = appliedRuleName,
                AccountingTemplateCode = result.AccountingTemplateCode,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error classifying invoice {InvoiceId}", request.InvoiceId);
            return new ClassifySingleInvoiceResponse
            {
                Success = false,
                Result = ClassificationResult.Error,
                ErrorMessage = ex.Message
            };
        }
    }
}