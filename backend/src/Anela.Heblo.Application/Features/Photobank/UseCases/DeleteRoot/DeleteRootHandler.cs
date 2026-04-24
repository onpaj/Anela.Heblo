using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.DeleteRoot
{
    public class DeleteRootHandler : IRequestHandler<DeleteRootRequest, DeleteRootResponse>
    {
        private readonly IPhotobankRepository _repository;

        public DeleteRootHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<DeleteRootResponse> Handle(DeleteRootRequest request, CancellationToken cancellationToken)
        {
            var deleted = await _repository.DeleteRootAsync(request.Id, cancellationToken);
            if (!deleted)
                return new DeleteRootResponse(ErrorCodes.PhotobankRootNotFound);

            await _repository.SaveChangesAsync(cancellationToken);
            return new DeleteRootResponse();
        }
    }
}
