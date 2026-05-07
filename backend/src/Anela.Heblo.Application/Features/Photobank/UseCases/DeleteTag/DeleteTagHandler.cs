using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.DeleteTag
{
    public class DeleteTagHandler : IRequestHandler<DeleteTagRequest, DeleteTagResponse>
    {
        private readonly IPhotobankRepository _repository;

        public DeleteTagHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<DeleteTagResponse> Handle(DeleteTagRequest request, CancellationToken cancellationToken)
        {
            var tag = await _repository.GetTagByIdAsync(request.Id, cancellationToken);
            if (tag is null)
                return new DeleteTagResponse(ErrorCodes.PhotobankTagNotFound);

            var assignmentCount = tag.PhotoTags.Count;
            await _repository.DeleteTagAsync(tag, cancellationToken);

            return new DeleteTagResponse { RemovedAssignmentCount = assignmentCount };
        }
    }
}
