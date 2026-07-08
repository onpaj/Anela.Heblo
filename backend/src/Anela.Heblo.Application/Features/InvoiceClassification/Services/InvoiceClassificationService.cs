using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.Services;

public class InvoiceClassificationService : IInvoiceClassificationService
{
    private readonly IClassificationRuleRepository _ruleRepository;
    private readonly IClassificationHistoryRepository _historyRepository;
    private readonly IInvoiceClassificationsClient _classificationsClient;
    private readonly IRuleEvaluationEngine _ruleEngine;
    private readonly ILogger<InvoiceClassificationService> _logger;

    public InvoiceClassificationService(
        IClassificationRuleRepository ruleRepository,
        IClassificationHistoryRepository historyRepository,
        IInvoiceClassificationsClient classificationsClient,
        IRuleEvaluationEngine ruleEngine,
        ILogger<InvoiceClassificationService> logger)
    {
        _ruleRepository = ruleRepository;
        _historyRepository = historyRepository;
        _classificationsClient = classificationsClient;
        _ruleEngine = ruleEngine;
        _logger = logger;
    }

    public async Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoice invoice, string processedBy)
    {
        try
        {
            var rules = await _ruleRepository.GetActiveRulesOrderedAsync();

            var matchedRule = _ruleEngine.FindMatchingRule(invoice, rules);

            if (matchedRule == null)
            {
                await RecordClassificationHistory(invoice, null, ClassificationResult.ManualReviewRequired,
                    null, null, "No matching rule found", processedBy);

                await _classificationsClient.MarkInvoiceForManualReviewAsync(invoice.InvoiceNumber, "No matching classification rule");

                return new InvoiceClassificationResult
                {
                    Result = ClassificationResult.ManualReviewRequired
                };
            }

            var success = await _classificationsClient.UpdateInvoiceClassificationAsync(
                invoice.InvoiceNumber, matchedRule.AccountingTemplateCode, matchedRule.Department);

            if (success)
            {
                await RecordClassificationHistory(invoice, matchedRule.Id, ClassificationResult.Success,
                    matchedRule.AccountingTemplateCode, matchedRule.Department, null, processedBy);

                return new InvoiceClassificationResult
                {
                    Result = ClassificationResult.Success,
                    RuleId = matchedRule.Id,
                    RuleName = matchedRule.Name,
                    AccountingTemplateCode = matchedRule.AccountingTemplateCode,
                    Department = matchedRule.Department
                };
            }
            else
            {
                var errorMessage = "Failed to update invoice classification in ABRA";
                await RecordClassificationHistory(invoice, matchedRule.Id, ClassificationResult.Error,
                    matchedRule.AccountingTemplateCode, matchedRule.Department, errorMessage, processedBy);

                return new InvoiceClassificationResult
                {
                    Result = ClassificationResult.Error,
                    RuleId = matchedRule.Id,
                    RuleName = matchedRule.Name,
                    Department = matchedRule.Department,
                    ErrorMessage = errorMessage
                };
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Exception during classification: {ex.Message}";
            await RecordClassificationHistory(invoice, null, ClassificationResult.Error,
                null, null, errorMessage, processedBy);

            _logger.LogError(ex, "Error classifying invoice {InvoiceId}", invoice.InvoiceNumber);

            return new InvoiceClassificationResult
            {
                Result = ClassificationResult.Error,
                ErrorMessage = errorMessage
            };
        }
    }

    private async Task RecordClassificationHistory(ReceivedInvoice invoice, Guid? ruleId,
        ClassificationResult result, string? accountingTemplateCode, string? department, string? errorMessage, string processedBy)
    {
        var history = new ClassificationHistory(
            invoice.InvoiceNumber, // AbraInvoiceId
            invoice.InvoiceNumber, // InvoiceNumber
            invoice.InvoiceDate,   // InvoiceDate
            invoice.CompanyName,   // CompanyName
            invoice.Description,   // Description
            result,
            processedBy,
            ruleId,
            accountingTemplateCode,
            department,
            errorMessage
        );

        await _historyRepository.AddAsync(history);
    }
}