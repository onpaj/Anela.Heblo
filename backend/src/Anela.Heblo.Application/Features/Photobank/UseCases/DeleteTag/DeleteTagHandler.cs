using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.DeleteTag
{
    public class DeleteTagHandler : IRequestHandler<DeleteTagRequest, DeleteTagResponse>
    {
        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankTagsCache _cache;

        public DeleteTagHandler(IPhotobankRepository repository, IPhotobankTagsCache cache)
        {
            _repository = repository;
            _cache = cache;
        }

        public async Task<DeleteTagResponse> Handle(DeleteTagRequest request, CancellationToken cancellationToken)
        {
            var tag = await _repository.GetTagByIdAsync(request.Id, cancellationToken);
            if (tag is null)
                return new DeleteTagResponse(ErrorCodes.PhotobankTagNotFound);

            var assignmentCount = tag.PhotoTags.Count;
            await _repository.DeleteTagAsync(tag, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            _cache.Invalidate();

            return new DeleteTagResponse { RemovedAssignmentCount = assignmentCount };
        }
    }
}
