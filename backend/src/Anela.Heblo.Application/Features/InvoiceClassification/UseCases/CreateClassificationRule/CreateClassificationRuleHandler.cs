using AutoMapper;
using MediatR;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.CreateClassificationRule;

public class CreateClassificationRuleHandler : IRequestHandler<CreateClassificationRuleRequest, CreateClassificationRuleResponse>
{
    private readonly IClassificationRuleRepository _ruleRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUserService;

    public CreateClassificationRuleHandler(
        IClassificationRuleRepository ruleRepository,
        IMapper mapper,
        ICurrentUserService currentUserService)
    {
        _ruleRepository = ruleRepository;
        _mapper = mapper;
        _currentUserService = currentUserService;
    }

    public async Task<CreateClassificationRuleResponse> Handle(CreateClassificationRuleRequest request, CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        var now = DateTime.UtcNow;
        
        var allRules = await _ruleRepository.GetAllAsync();
        var maxOrder = allRules.Count > 0 ? allRules.Max(r => r.Order) : 0;

        var rule = new ClassificationRule(
            request.Name,
            request.RuleTypeIdentifier,
            request.Pattern,
            request.AccountingPrescription,
            currentUser.Name
        );
        
        rule.SetOrder(maxOrder + 1);

        var createdRule = await _ruleRepository.AddAsync(rule);

        return new CreateClassificationRuleResponse
        {
            Rule = _mapper.Map<Contracts.ClassificationRuleDto>(createdRule)
        };
    }
}