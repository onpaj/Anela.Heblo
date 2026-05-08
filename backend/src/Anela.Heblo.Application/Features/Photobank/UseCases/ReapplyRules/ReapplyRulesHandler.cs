using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules
{
    public class ReapplyRulesHandler : IRequestHandler<ReapplyRulesRequest, ReapplyRulesResponse>
    {
        private readonly IPhotobankRepository _repository;

        public ReapplyRulesHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<ReapplyRulesResponse> Handle(ReapplyRulesRequest request, CancellationToken cancellationToken)
        {
            var allRules = await _repository.GetRulesAsync(cancellationToken);

            string? scopeToTagName = null;
            if (request.RuleId.HasValue)
            {
                var rule = allRules.FirstOrDefault(r => r.Id == request.RuleId.Value);
                if (rule == null)
                    return new ReapplyRulesResponse(ErrorCodes.PhotobankRuleNotFound);

                scopeToTagName = rule.TagName.ToLowerInvariant();
            }

            var photosUpdated = await _repository.ReapplyRulesAsync(allRules, scopeToTagName, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            return new ReapplyRulesResponse { PhotosUpdated = photosUpdated };
        }
    }
}
