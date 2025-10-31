using MediatR;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.DeleteClassificationRule;

public class DeleteClassificationRuleHandler : IRequestHandler<DeleteClassificationRuleRequest, DeleteClassificationRuleResponse>
{
    private readonly IClassificationRuleRepository _ruleRepository;

    public DeleteClassificationRuleHandler(IClassificationRuleRepository ruleRepository)
    {
        _ruleRepository = ruleRepository;
    }

    public async Task<DeleteClassificationRuleResponse> Handle(DeleteClassificationRuleRequest request, CancellationToken cancellationToken)
    {
        await _ruleRepository.DeleteAsync(request.Id);

        return new DeleteClassificationRuleResponse
        {
            Success = true
        };
    }
}