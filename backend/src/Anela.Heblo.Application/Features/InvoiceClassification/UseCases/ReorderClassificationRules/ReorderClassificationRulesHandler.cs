using MediatR;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ReorderClassificationRules;

public class ReorderClassificationRulesHandler : IRequestHandler<ReorderClassificationRulesRequest, ReorderClassificationRulesResponse>
{
    private readonly IClassificationRuleRepository _ruleRepository;

    public ReorderClassificationRulesHandler(IClassificationRuleRepository ruleRepository)
    {
        _ruleRepository = ruleRepository;
    }

    public async Task<ReorderClassificationRulesResponse> Handle(ReorderClassificationRulesRequest request, CancellationToken cancellationToken)
    {
        await _ruleRepository.ReorderRulesAsync(request.RuleIds);

        return new ReorderClassificationRulesResponse
        {
            Success = true
        };
    }
}