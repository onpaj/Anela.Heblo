using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
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
            var activeRules = await _repository.GetRulesAsync(cancellationToken);
            var photosUpdated = await _repository.ReapplyRulesAsync(activeRules, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            return new ReapplyRulesResponse { PhotosUpdated = photosUpdated };
        }
    }
}
