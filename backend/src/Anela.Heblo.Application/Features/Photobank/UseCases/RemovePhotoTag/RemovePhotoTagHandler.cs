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
        private readonly IPhotobankPhotoRepository _photoRepository;
        private readonly IPhotobankPhotoTagRepository _photoTagRepository;
        private readonly IPhotobankTagsCache _cache;

        public RemovePhotoTagHandler(
            IPhotobankPhotoRepository photoRepository,
            IPhotobankPhotoTagRepository photoTagRepository,
            IPhotobankTagsCache cache)
        {
            _photoRepository = photoRepository;
            _photoTagRepository = photoTagRepository;
            _cache = cache;
        }

        public async Task<RemovePhotoTagResponse> Handle(RemovePhotoTagRequest request, CancellationToken cancellationToken)
        {
            var photo = await _photoRepository.GetPhotoByIdAsync(request.PhotoId, cancellationToken);
            if (photo == null)
                return new RemovePhotoTagResponse(ErrorCodes.PhotoNotFound);

            await _photoTagRepository.RemovePhotoTagAsync(request.PhotoId, request.TagId, cancellationToken);
            await _photoTagRepository.SaveChangesAsync(cancellationToken);
            _cache.Invalidate();

            return new RemovePhotoTagResponse();
        }
    }
}
