using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.CreateClassificationRule;

public class CreateClassificationRuleResponse
{
    public ClassificationRuleDto Rule { get; set; } = new();
}