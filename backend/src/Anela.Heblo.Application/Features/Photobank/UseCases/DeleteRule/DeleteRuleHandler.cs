using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.DeleteRule
{
    public class DeleteRuleHandler : IRequestHandler<DeleteRuleRequest, DeleteRuleResponse>
    {
        private readonly IPhotobankTagRuleRepository _repository;

        public DeleteRuleHandler(IPhotobankTagRuleRepository repository)
        {
            _repository = repository;
        }

        public async Task<DeleteRuleResponse> Handle(DeleteRuleRequest request, CancellationToken cancellationToken)
        {
            var deleted = await _repository.DeleteRuleAsync(request.Id, cancellationToken);
            if (!deleted)
                return new DeleteRuleResponse(ErrorCodes.PhotobankRuleNotFound);

            await _repository.SaveChangesAsync(cancellationToken);
            return new DeleteRuleResponse();
        }
    }
}
