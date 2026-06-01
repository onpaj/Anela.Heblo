using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTag
{
    public class BulkAddPhotoTagHandler : IRequestHandler<BulkAddPhotoTagRequest, BulkAddPhotoTagResponse>
    {
        private const int BulkTagLimit = 5_000;

        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankTagsCache _cache;

        public BulkAddPhotoTagHandler(IPhotobankRepository repository, IPhotobankTagsCache cache)
        {
            _repository = repository;
            _cache = cache;
        }

        public async Task<BulkAddPhotoTagResponse> Handle(BulkAddPhotoTagRequest request, CancellationToken cancellationToken)
        {
            var total = await _repository.CountFilteredPhotosAsync(
                request.Tags, request.Search, cancellationToken);

            if (total > BulkTagLimit)
                return new BulkAddPhotoTagResponse(ErrorCodes.BulkTagLimitExceeded)
                {
                    Params = new Dictionary<string, string>
                    {
                        { "Count", total.ToString() },
                        { "Limit", BulkTagLimit.ToString() },
                    },
                };

            var normalizedName = request.TagName.Trim().ToLowerInvariant();
            var tag = await _repository.GetOrCreateTagAsync(normalizedName, cancellationToken);
            if (tag == null)
                return new BulkAddPhotoTagResponse(ErrorCodes.PhotoTagCreationFailed);

            var photoIds = await _repository.GetFilteredPhotoIdsMissingTagAsync(
                request.Tags, request.Search, tag.Id, cancellationToken);

            var now = DateTime.UtcNow;
            foreach (var photoId in photoIds)
            {
                await _repository.AddPhotoTagAsync(new PhotoTag
                {
                    PhotoId = photoId,
                    TagId = tag.Id,
                    Source = PhotoTagSource.Manual,
                    CreatedAt = now,
                }, cancellationToken);
            }

            if (photoIds.Count > 0)
            {
                await _repository.SaveChangesAsync(cancellationToken);
                _cache.Invalidate();
            }

            return new BulkAddPhotoTagResponse
            {
                TagId = tag.Id,
                TagName = tag.Name,
                AddedCount = photoIds.Count,
                AlreadyTaggedCount = total - photoIds.Count,
            };
        }
    }
}
