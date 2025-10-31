using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRules;

public class GetClassificationRulesResponse
{
    public List<ClassificationRuleDto> Rules { get; set; } = new();
}