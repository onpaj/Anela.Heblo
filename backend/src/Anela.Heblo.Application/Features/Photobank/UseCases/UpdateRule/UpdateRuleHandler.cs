using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.UpdateRule
{
    public class UpdateRuleHandler : IRequestHandler<UpdateRuleRequest, UpdateRuleResponse>
    {
        private readonly IPhotobankRepository _repository;

        public UpdateRuleHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<UpdateRuleResponse> Handle(UpdateRuleRequest request, CancellationToken cancellationToken)
        {
            var rule = await _repository.GetRuleByIdAsync(request.Id, cancellationToken);
            if (rule == null)
                return new UpdateRuleResponse(ErrorCodes.PhotobankRuleNotFound);

            rule.PathPattern = request.PathPattern.Trim();
            rule.TagName = request.TagName.Trim().ToLowerInvariant();
            rule.IsActive = request.IsActive;
            rule.SortOrder = request.SortOrder;

            await _repository.UpdateRuleAsync(rule, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            return new UpdateRuleResponse();
        }
    }
}
