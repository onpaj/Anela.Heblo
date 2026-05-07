using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTagByIds
{
    public class BulkAddPhotoTagByIdsHandler : IRequestHandler<BulkAddPhotoTagByIdsRequest, BulkAddPhotoTagByIdsResponse>
    {
        private const int BulkTagLimit = 5_000;

        private readonly IPhotobankRepository _repository;

        public BulkAddPhotoTagByIdsHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<BulkAddPhotoTagByIdsResponse> Handle(
            BulkAddPhotoTagByIdsRequest request,
            CancellationToken cancellationToken)
        {
            if (request.PhotoIds == null || request.PhotoIds.Count == 0)
                return new BulkAddPhotoTagByIdsResponse(ErrorCodes.BulkTagInvalidRequest);

            if (request.PhotoIds.Count > BulkTagLimit)
                return new BulkAddPhotoTagByIdsResponse(ErrorCodes.BulkTagLimitExceeded)
                {
                    Params = new Dictionary<string, string>
                    {
                        { "Count", request.PhotoIds.Count.ToString() },
                        { "Limit", BulkTagLimit.ToString() },
                    },
                };

            var normalizedName = request.TagName.Trim().ToLowerInvariant();
            var tag = await _repository.GetOrCreateTagAsync(normalizedName, cancellationToken);
            if (tag == null)
                return new BulkAddPhotoTagByIdsResponse(ErrorCodes.PhotoTagCreationFailed);

            var toAdd = await _repository.GetExistingPhotoIdsMissingTagAsync(
                request.PhotoIds, tag.Id, cancellationToken);

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
                await _repository.SaveChangesAsync(cancellationToken);

            var distinctCount = request.PhotoIds.Distinct().Count();

            return new BulkAddPhotoTagByIdsResponse
            {
                TagId = tag.Id,
                TagName = tag.Name,
                AddedCount = toAdd.Count,
                AlreadyTaggedCount = distinctCount - toAdd.Count,
            };
        }
    }
}
