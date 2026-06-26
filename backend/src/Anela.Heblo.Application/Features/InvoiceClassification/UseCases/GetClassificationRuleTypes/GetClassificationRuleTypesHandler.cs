using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using MediatR;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRuleTypes;

public class GetClassificationRuleTypesHandler
    : IRequestHandler<GetClassificationRuleTypesRequest, GetClassificationRuleTypesResponse>
{
    private readonly IEnumerable<IClassificationRule> _classificationRules;

    public GetClassificationRuleTypesHandler(IEnumerable<IClassificationRule> classificationRules)
    {
        _classificationRules = classificationRules;
    }

    public Task<GetClassificationRuleTypesResponse> Handle(
        GetClassificationRuleTypesRequest request,
        CancellationToken cancellationToken)
    {
        var ruleTypes = _classificationRules
            .Select(rule => new ClassificationRuleTypeDto
            {
                Identifier = rule.Identifier,
                DisplayName = rule.DisplayName,
                Description = rule.Description
            })
            .ToList();

        return Task.FromResult(new GetClassificationRuleTypesResponse { RuleTypes = ruleTypes });
    }
}
