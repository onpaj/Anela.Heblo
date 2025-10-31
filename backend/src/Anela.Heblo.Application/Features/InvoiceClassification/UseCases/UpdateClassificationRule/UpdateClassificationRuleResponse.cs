using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.UpdateClassificationRule;

public class UpdateClassificationRuleResponse
{
    public ClassificationRuleDto Rule { get; set; } = new();
}