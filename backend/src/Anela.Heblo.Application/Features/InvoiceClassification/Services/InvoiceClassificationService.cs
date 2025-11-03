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
                await RecordClassificationHistory(invoice, null, ClassificationResult.ManualReviewRequired,
                    null, "No matching rule found", currentUser.Name);

                await _classificationsClient.MarkInvoiceForManualReviewAsync(invoice.InvoiceNumber, "No matching classification rule");

                return new InvoiceClassificationResult
                {
                    Result = ClassificationResult.ManualReviewRequired
                };
            }

            var success = await _classificationsClient.UpdateInvoiceClassificationAsync(
                invoice.InvoiceNumber, matchedRule.AccountingTemplateCode);

            if (success)
            {
                await RecordClassificationHistory(invoice, matchedRule.Id, ClassificationResult.Success,
                    matchedRule.AccountingTemplateCode, null, currentUser.Name);

                return new InvoiceClassificationResult
                {
                    Result = ClassificationResult.Success,
                    RuleId = matchedRule.Id,
                    AccountingTemplateCode = matchedRule.AccountingTemplateCode
                };
            }
            else
            {
                var errorMessage = "Failed to update invoice classification in ABRA";
                await RecordClassificationHistory(invoice, matchedRule.Id, ClassificationResult.Error,
                    matchedRule.AccountingTemplateCode, errorMessage, currentUser.Name);

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
            await RecordClassificationHistory(invoice, null, ClassificationResult.Error,
                null, errorMessage, currentUser.Name);

            _logger.LogError(ex, "Error classifying invoice {InvoiceId}", invoice.InvoiceNumber);

            return new InvoiceClassificationResult
            {
                Result = ClassificationResult.Error,
                ErrorMessage = errorMessage
            };
        }
    }

    private async Task RecordClassificationHistory(ReceivedInvoiceDto invoice, Guid? ruleId,
        ClassificationResult result, string? accountingTemplateCode, string? errorMessage, string processedBy)
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
            errorMessage
        );

        await _historyRepository.AddAsync(history);
    }
}