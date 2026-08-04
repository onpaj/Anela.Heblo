using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.AddPhotoTag
{
    public class AddPhotoTagHandler : IRequestHandler<AddPhotoTagRequest, AddPhotoTagResponse>
    {
        private readonly IPhotobankPhotoRepository _photoRepository;
        private readonly IPhotobankTagRepository _tagRepository;
        private readonly IPhotobankPhotoTagRepository _photoTagRepository;
        private readonly IPhotobankTagsCache _cache;

        public AddPhotoTagHandler(
            IPhotobankPhotoRepository photoRepository,
            IPhotobankTagRepository tagRepository,
            IPhotobankPhotoTagRepository photoTagRepository,
            IPhotobankTagsCache cache)
        {
            _photoRepository = photoRepository;
            _tagRepository = tagRepository;
            _photoTagRepository = photoTagRepository;
            _cache = cache;
        }

        public async Task<AddPhotoTagResponse> Handle(AddPhotoTagRequest request, CancellationToken cancellationToken)
        {
            var photo = await _photoRepository.GetPhotoByIdAsync(request.PhotoId, cancellationToken);
            if (photo == null)
                return new AddPhotoTagResponse(ErrorCodes.PhotoNotFound);

            var normalizedName = request.TagName.Trim().ToLowerInvariant();
            var tag = await _tagRepository.GetOrCreateTagAsync(normalizedName, cancellationToken);
            if (tag == null)
                return new AddPhotoTagResponse(ErrorCodes.PhotoTagCreationFailed);

            if (await _photoTagRepository.PhotoTagExistsAsync(photo.Id, tag.Id, cancellationToken))
                return new AddPhotoTagResponse { TagId = tag.Id, TagName = tag.Name };

            var photoTag = new PhotoTag
            {
                PhotoId = photo.Id,
                TagId = tag.Id,
                Source = PhotoTagSource.Manual,
                CreatedAt = DateTime.UtcNow,
            };

            await _photoTagRepository.AddPhotoTagAsync(photoTag, cancellationToken);
            await _photoTagRepository.SaveChangesAsync(cancellationToken);
            _cache.Invalidate();

            return new AddPhotoTagResponse { TagId = tag.Id, TagName = tag.Name };
        }
    }
}
