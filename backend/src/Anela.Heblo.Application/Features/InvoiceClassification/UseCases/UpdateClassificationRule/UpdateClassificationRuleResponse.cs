using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.UpdateClassificationRule;

public class UpdateClassificationRuleResponse : BaseResponse
{
    public ClassificationRuleDto Rule { get; set; } = new();
}