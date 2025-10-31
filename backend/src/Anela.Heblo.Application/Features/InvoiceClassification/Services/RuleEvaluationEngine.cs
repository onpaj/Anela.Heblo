using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.Services;

public class RuleEvaluationEngine : IRuleEvaluationEngine
{
    private readonly IEnumerable<IClassificationRule> _classificationRules;

    public RuleEvaluationEngine(IEnumerable<IClassificationRule> classificationRules)
    {
        _classificationRules = classificationRules;
    }

    public ClassificationRule? FindMatchingRule(ReceivedInvoiceDto invoice, List<ClassificationRule> rules)
    {
        foreach (var rule in rules.Where(r => r.IsActive).OrderBy(r => r.Order))
        {
            if (EvaluateRule(invoice, rule))
            {
                return rule;
            }
        }

        return null;
    }

    private bool EvaluateRule(ReceivedInvoiceDto invoice, ClassificationRule rule)
    {
        var classificationRule = _classificationRules.FirstOrDefault(r => r.Identifier == rule.RuleTypeIdentifier);
        return classificationRule?.Evaluate(invoice, rule.Pattern) ?? false;
    }

}