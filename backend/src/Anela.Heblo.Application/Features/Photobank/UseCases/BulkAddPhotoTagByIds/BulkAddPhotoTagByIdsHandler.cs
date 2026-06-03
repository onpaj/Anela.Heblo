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
        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankTagsCache _cache;

        public BulkAddPhotoTagByIdsHandler(IPhotobankRepository repository, IPhotobankTagsCache cache)
        {
            _repository = repository;
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

            var tag = await _repository.GetOrCreateTagAsync(normalizedName, cancellationToken);
            if (tag == null)
                return new BulkAddPhotoTagByIdsResponse(ErrorCodes.PhotoTagCreationFailed);

            var toAdd = await _repository.GetExistingPhotoIdsMissingTagAsync(
                distinctIds, tag.Id, cancellationToken);

            var now = DateTime.UtcNow;
            foreach (var photoId in toAdd)
            {
                await _repository.AddPhotoTagAsync(new PhotoTag
                {
                    PhotoId = photoId,
                    TagId = tag.Id,
                    Source = PhotoTagSource.Manual,
                    CreatedAt = now,
                }, cancellationToken);
            }

            if (toAdd.Count > 0)
            {
                await _repository.SaveChangesAsync(cancellationToken);
                _cache.Invalidate();
            }

            var existingCount = await _repository.CountExistingPhotosAsync(distinctIds, cancellationToken);

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
