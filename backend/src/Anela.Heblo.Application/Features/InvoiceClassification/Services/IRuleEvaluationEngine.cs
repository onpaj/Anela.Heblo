using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.Services;

public interface IRuleEvaluationEngine
{
    ClassificationRule? FindMatchingRule(ReceivedInvoiceDto invoice, List<ClassificationRule> rules);
}