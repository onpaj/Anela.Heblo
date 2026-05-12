using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.RemovePhotoTag
{
    public class RemovePhotoTagHandler : IRequestHandler<RemovePhotoTagRequest, RemovePhotoTagResponse>
    {
        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankTagsCache _cache;

        public RemovePhotoTagHandler(IPhotobankRepository repository, IPhotobankTagsCache cache)
        {
            _repository = repository;
            _cache = cache;
        }

        public async Task<RemovePhotoTagResponse> Handle(RemovePhotoTagRequest request, CancellationToken cancellationToken)
        {
            var photo = await _repository.GetPhotoByIdAsync(request.PhotoId, cancellationToken);
            if (photo == null)
                return new RemovePhotoTagResponse(ErrorCodes.PhotoNotFound);

            await _repository.RemovePhotoTagAsync(request.PhotoId, request.TagId, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            _cache.Invalidate();

            return new RemovePhotoTagResponse();
        }
    }
}
