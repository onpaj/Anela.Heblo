using System.Net.Http;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail
{
    public class GetThumbnailHandler : IRequestHandler<GetThumbnailRequest, GetThumbnailResponse>
    {
        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankGraphService _graphService;
        private readonly ILogger<GetThumbnailHandler> _logger;

        public GetThumbnailHandler(
            IPhotobankRepository repository,
            IPhotobankGraphService graphService,
            ILogger<GetThumbnailHandler> logger)
        {
            _repository = repository;
            _graphService = graphService;
            _logger = logger;
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

            GraphThumbnail? rawThumbnail;
            try
            {
                rawThumbnail = await _graphService.GetThumbnailAsync(
                    locator.DriveId, locator.SharePointFileId, request.Size, cancellationToken);
            }
            catch (GraphThrottledException ex)
            {
                _logger.LogWarning("Microsoft Graph thumbnail request throttled for photo {PhotoId}. RetryAfter: {RetryAfter}",
                    request.Id, ex.RetryAfter);
                return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailThrottled)
                {
                    RetryAfterSeconds = ex.RetryAfter.HasValue
                        ? (int)Math.Ceiling(ex.RetryAfter.Value.TotalSeconds)
                        : null,
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Upstream HTTP error fetching thumbnail for photo {PhotoId}", request.Id);
                return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailUpstream);
            }
            catch (MsalException ex)
            {
                _logger.LogError(ex, "Token acquisition failed for thumbnail {PhotoId}. MSAL error: {ErrorCode}", request.Id, ex.ErrorCode);
                return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailAuthUnavailable);
            }

            if (rawThumbnail is null)
            {
                return new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailNotFound);
            }

            // NFR-3: transfer stream ownership to the response. Do NOT dispose rawThumbnail
            // (GraphThumbnail.Dispose() closes the underlying Stream); FileStreamResult disposes it after writing.
            return new GetThumbnailResponse
            {
                Content = rawThumbnail.Content,
                ContentType = rawThumbnail.ContentType,
                ContentLength = rawThumbnail.ContentLength,
            };
        }
    }
}
