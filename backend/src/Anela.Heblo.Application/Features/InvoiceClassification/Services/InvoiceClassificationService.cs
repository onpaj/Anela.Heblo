using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.InvoiceClassification.Services;

public class InvoiceClassificationService : IInvoiceClassificationService
{
    private readonly IClassificationRuleRepository _ruleRepository;
    private readonly IClassificationHistoryRepository _historyRepository;
    private readonly IInvoiceClassificationsClient _classificationsClient;
    private readonly IRuleEvaluationEngine _ruleEngine;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<InvoiceClassificationService> _logger;

    public InvoiceClassificationService(
        IClassificationRuleRepository ruleRepository,
        IClassificationHistoryRepository historyRepository,
        IInvoiceClassificationsClient classificationsClient,
        IRuleEvaluationEngine ruleEngine,
        ICurrentUserService currentUserService,
        ILogger<InvoiceClassificationService> logger)
    {
        _ruleRepository = ruleRepository;
        _historyRepository = historyRepository;
        _classificationsClient = classificationsClient;
        _ruleEngine = ruleEngine;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoiceDto invoice)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        
        try
        {
            var rules = await _ruleRepository.GetActiveRulesOrderedAsync();
            
            var matchedRule = _ruleEngine.FindMatchingRule(invoice, rules);
            
            if (matchedRule == null)
            {
                await RecordClassificationHistory(invoice.Id, null, ClassificationResult.ManualReviewRequired, 
                    null, "No matching rule found", currentUser.Name);
                
                await _classificationsClient.MarkInvoiceForManualReviewAsync(invoice.Id, "No matching classification rule");
                
                return new InvoiceClassificationResult
                {
                    Result = ClassificationResult.ManualReviewRequired
                };
            }

            var success = await _classificationsClient.UpdateInvoiceClassificationAsync(
                invoice.Id, matchedRule.AccountingPrescription);

            if (success)
            {
                await RecordClassificationHistory(invoice.Id, matchedRule.Id, ClassificationResult.Success,
                    matchedRule.AccountingPrescription, null, currentUser.Name);

                return new InvoiceClassificationResult
                {
                    Result = ClassificationResult.Success,
                    RuleId = matchedRule.Id,
                    AccountingPrescription = matchedRule.AccountingPrescription
                };
            }
            else
            {
                var errorMessage = "Failed to update invoice classification in ABRA";
                await RecordClassificationHistory(invoice.Id, matchedRule.Id, ClassificationResult.Error,
                    matchedRule.AccountingPrescription, errorMessage, currentUser.Name);

                return new InvoiceClassificationResult
                {
                    Result = ClassificationResult.Error,
                    RuleId = matchedRule.Id,
                    ErrorMessage = errorMessage
                };
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Exception during classification: {ex.Message}";
            await RecordClassificationHistory(invoice.Id, null, ClassificationResult.Error,
                null, errorMessage, currentUser.Name);

            _logger.LogError(ex, "Error classifying invoice {InvoiceId}", invoice.Id);
            
            return new InvoiceClassificationResult
            {
                Result = ClassificationResult.Error,
                ErrorMessage = errorMessage
            };
        }
    }

    private async Task RecordClassificationHistory(string invoiceId, Guid? ruleId, 
        ClassificationResult result, string? accountingPrescription, string? errorMessage, string processedBy)
    {
        var history = new ClassificationHistory(
            invoiceId,
            result,
            processedBy,
            ruleId,
            accountingPrescription,
            errorMessage
        );

        await _historyRepository.AddAsync(history);
    }
}