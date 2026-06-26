using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRuleTypes;

public class GetClassificationRuleTypesResponse : BaseResponse
{
    public List<ClassificationRuleTypeDto> RuleTypes { get; set; } = new();

    public GetClassificationRuleTypesResponse() : base() { }

    public GetClassificationRuleTypesResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
