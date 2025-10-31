using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.CreateClassificationRule;

public class CreateClassificationRuleResponse : BaseResponse
{
    public ClassificationRuleDto Rule { get; set; } = new();
}