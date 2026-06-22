using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail
{
    public class GetThumbnailHandler : IRequestHandler<GetThumbnailRequest, GetThumbnailResponse>
    {
        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankGraphService _graphService;

        public GetThumbnailHandler(
            IPhotobankRepository repository,
            IPhotobankGraphService graphService)
        {
            _repository = repository;
            _graphService = graphService;
        }

        public async Task<GetThumbnailResponse> Handle(
            GetThumbnailRequest request,
            CancellationToken cancellationToken)
        {
            var locator = await _repository.GetLocatorAsync(request.Id, cancellationToken);
            if (locator is null)
            {
                return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailNotFound);
            }

            var thumbnailResult = await _graphService.GetThumbnailAsync(
                locator.DriveId, locator.SharePointFileId, request.Size, cancellationToken);

            return thumbnailResult switch
            {
                GetThumbnailResult.Success ok => new GetThumbnailResponse
                {
                    Content = ok.Thumbnail.Content,
                    ContentType = ok.Thumbnail.ContentType,
                    ContentLength = ok.Thumbnail.ContentLength,
                },
                GetThumbnailResult.NotFound => new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailNotFound),
                GetThumbnailResult.Throttled throttled => new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailThrottled)
                {
                    RetryAfterSeconds = throttled.RetryAfter.HasValue
                        ? (int)Math.Ceiling(throttled.RetryAfter.Value.TotalSeconds)
                        : null,
                },
                GetThumbnailResult.UpstreamError => new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailUpstream),
                GetThumbnailResult.AuthUnavailable => new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailAuthUnavailable),
                _ => throw new InvalidOperationException($"Unhandled GetThumbnailResult: {thumbnailResult.GetType().Name}"),
            };
        }
    }
}
