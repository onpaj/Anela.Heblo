using AutoMapper;
using MediatR;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRules;

public class GetClassificationRulesHandler : IRequestHandler<GetClassificationRulesRequest, GetClassificationRulesResponse>
{
    private readonly IClassificationRuleRepository _ruleRepository;
    private readonly IMapper _mapper;

    public GetClassificationRulesHandler(IClassificationRuleRepository ruleRepository, IMapper mapper)
    {
        _ruleRepository = ruleRepository;
        _mapper = mapper;
    }

    public async Task<GetClassificationRulesResponse> Handle(GetClassificationRulesRequest request, CancellationToken cancellationToken)
    {
        var rules = request.IncludeInactive
            ? await _ruleRepository.GetAllAsync()
            : await _ruleRepository.GetActiveRulesOrderedAsync();

        return new GetClassificationRulesResponse
        {
            Rules = _mapper.Map<List<Contracts.ClassificationRuleDto>>(rules)
        };
    }
}