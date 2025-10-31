using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRules;

public class GetClassificationRulesResponse : BaseResponse
{
    public List<ClassificationRuleDto> Rules { get; set; } = new();
}