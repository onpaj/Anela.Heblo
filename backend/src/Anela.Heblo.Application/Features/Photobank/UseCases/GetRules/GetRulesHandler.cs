using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetRules
{
    public class GetRulesHandler : IRequestHandler<GetRulesRequest, GetRulesResponse>
    {
        private readonly IPhotobankRepository _repository;

        public GetRulesHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetRulesResponse> Handle(GetRulesRequest request, CancellationToken cancellationToken)
        {
            var rules = await _repository.GetRulesAsync(cancellationToken);

            return new GetRulesResponse
            {
                Rules = rules.Select(r => new TagRuleDto
                {
                    Id = r.Id,
                    PathPattern = r.PathPattern,
                    TagName = r.TagName,
                    IsActive = r.IsActive,
                    SortOrder = r.SortOrder,
                }).ToList(),
            };
        }
    }
}
