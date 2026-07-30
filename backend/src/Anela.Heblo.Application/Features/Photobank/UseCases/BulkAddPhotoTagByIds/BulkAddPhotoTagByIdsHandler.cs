using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTagByIds
{
    public class BulkAddPhotoTagByIdsHandler : IRequestHandler<BulkAddPhotoTagByIdsRequest, BulkAddPhotoTagByIdsResponse>
    {
        private readonly IPhotobankPhotoRepository _photoRepository;
        private readonly IPhotobankTagRepository _tagRepository;
        private readonly IPhotobankPhotoTagRepository _photoTagRepository;
        private readonly IPhotobankTagsCache _cache;

        public BulkAddPhotoTagByIdsHandler(
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

        public async Task<BulkAddPhotoTagByIdsResponse> Handle(
            BulkAddPhotoTagByIdsRequest request,
            CancellationToken cancellationToken)
        {
            if (request.PhotoIds == null || request.PhotoIds.Count == 0)
                return new BulkAddPhotoTagByIdsResponse(ErrorCodes.BulkTagInvalidRequest);

            if (request.PhotoIds.Count > PhotobankConstants.BulkTagLimit)
                return new BulkAddPhotoTagByIdsResponse(ErrorCodes.BulkTagLimitExceeded)
                {
                    Params = new Dictionary<string, string>
                    {
                        { "Count", request.PhotoIds.Count.ToString() },
                        { "Limit", PhotobankConstants.BulkTagLimit.ToString() },
                    },
                };

            var distinctIds = request.PhotoIds.Distinct().ToList();

            var normalizedName = request.TagName.Trim().ToLowerInvariant();
            if (normalizedName.Length == 0)
                return new BulkAddPhotoTagByIdsResponse(ErrorCodes.BulkTagInvalidRequest);

            var tag = await _tagRepository.GetOrCreateTagAsync(normalizedName, cancellationToken);
            if (tag == null)
                return new BulkAddPhotoTagByIdsResponse(ErrorCodes.PhotoTagCreationFailed);

            var toAdd = await _photoRepository.GetExistingPhotoIdsMissingTagAsync(
                distinctIds, tag.Id, cancellationToken);

            var now = DateTime.UtcNow;
            foreach (var photoId in toAdd)
            {
                await _photoTagRepository.AddPhotoTagAsync(new PhotoTag
                {
                    PhotoId = photoId,
                    TagId = tag.Id,
                    Source = PhotoTagSource.Manual,
                    CreatedAt = now,
                }, cancellationToken);
            }

            if (toAdd.Count > 0)
            {
                await _photoTagRepository.SaveChangesAsync(cancellationToken);
                _cache.Invalidate();
            }

            var existingCount = await _photoRepository.CountExistingPhotosAsync(distinctIds, cancellationToken);

            return new BulkAddPhotoTagByIdsResponse
            {
                TagId = tag.Id,
                TagName = tag.Name,
                AddedCount = toAdd.Count,
                AlreadyTaggedCount = existingCount - toAdd.Count,
            };
        }
    }
}
