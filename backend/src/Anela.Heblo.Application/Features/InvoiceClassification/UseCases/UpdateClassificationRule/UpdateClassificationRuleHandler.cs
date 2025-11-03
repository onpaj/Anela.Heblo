using AutoMapper;
using MediatR;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.UpdateClassificationRule;

public class UpdateClassificationRuleHandler : IRequestHandler<UpdateClassificationRuleRequest, UpdateClassificationRuleResponse>
{
    private readonly IClassificationRuleRepository _ruleRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUserService;

    public UpdateClassificationRuleHandler(
        IClassificationRuleRepository ruleRepository,
        IMapper mapper,
        ICurrentUserService currentUserService)
    {
        _ruleRepository = ruleRepository;
        _mapper = mapper;
        _currentUserService = currentUserService;
    }

    public async Task<UpdateClassificationRuleResponse> Handle(UpdateClassificationRuleRequest request, CancellationToken cancellationToken)
    {
        var existingRule = await _ruleRepository.GetByIdAsync(request.Id);
        if (existingRule == null)
        {
            throw new ArgumentException($"Classification rule with ID {request.Id} not found");
        }

        var currentUser = _currentUserService.GetCurrentUser();
        
        existingRule.Update(
            request.Name,
            request.RuleTypeIdentifier,
            request.Pattern,
            request.AccountingTemplateCode,
            request.IsActive,
            currentUser.Name
        );

        var updatedRule = await _ruleRepository.UpdateAsync(existingRule);

        return new UpdateClassificationRuleResponse
        {
            Rule = _mapper.Map<Contracts.ClassificationRuleDto>(updatedRule)
        };
    }
}