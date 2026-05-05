using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers
{
    [ApiController]
    [Route("api/photobank")]
    [Authorize]
    public class PhotobankController : BaseApiController
    {
        private readonly IMediator _mediator;
        private readonly IPhotobankRepository _photobankRepository;
        private readonly IPhotobankGraphService _photobankGraphService;

        public PhotobankController(
            IMediator mediator,
            IPhotobankRepository photobankRepository,
            IPhotobankGraphService photobankGraphService)
        {
            _mediator = mediator;
            _photobankRepository = photobankRepository;
            _photobankGraphService = photobankGraphService;
        }

        /// <summary>
        /// Get photos with optional tag AND filter, filename search, and pagination.
        /// </summary>
        [HttpGet("photos")]
        [ProducesResponseType(typeof(GetPhotosResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<GetPhotosResponse>> GetPhotos(
            [FromQuery] List<string>? tags,
            [FromQuery] string? search,
            [FromQuery] string? folderPath,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 48,
            CancellationToken cancellationToken = default)
        {
            var request = new GetPhotosRequest
            {
                Tags = tags,
                Search = search,
                FolderPath = folderPath,
                Page = page,
                PageSize = pageSize,
            };
            var response = await _mediator.Send(request, cancellationToken);
            return HandleResponse(response);
        }

        /// <summary>
        /// Get all tags with photo counts, sorted by count descending.
        /// </summary>
        [HttpGet("tags")]
        [ProducesResponseType(typeof(GetTagsResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<GetTagsResponse>> GetTags(CancellationToken cancellationToken = default)
        {
            var response = await _mediator.Send(new GetTagsRequest(), cancellationToken);
            return HandleResponse(response);
        }

        /// <summary>
        /// Add a manual tag to a photo. Requires administrator role.
        /// </summary>
        [HttpPost("photos/{id:int}/tags")]
        [Authorize(Roles = AuthorizationConstants.Roles.Administrator)]
        [ProducesResponseType(typeof(AddPhotoTagResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AddPhotoTagResponse>> AddPhotoTag(
            int id,
            [FromBody] AddPhotoTagBody body,
            CancellationToken cancellationToken = default)
        {
            var request = new AddPhotoTagRequest { PhotoId = id, TagName = body.TagName };
            var response = await _mediator.Send(request, cancellationToken);
            return HandleResponse(response);
        }

        /// <summary>
        /// Remove a tag from a photo. Requires administrator role.
        /// </summary>
        [HttpDelete("photos/{id:int}/tags/{tagId:int}")]
        [Authorize(Roles = AuthorizationConstants.Roles.Administrator)]
        [ProducesResponseType(typeof(RemovePhotoTagResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<RemovePhotoTagResponse>> RemovePhotoTag(
            int id,
            int tagId,
            CancellationToken cancellationToken = default)
        {
            var request = new RemovePhotoTagRequest { PhotoId = id, TagId = tagId };
            var response = await _mediator.Send(request, cancellationToken);
            return HandleResponse(response);
        }

        [HttpGet("photos/{id:int}/thumbnail/{size}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> GetThumbnail(
            int id,
            ThumbnailSize size,
            CancellationToken cancellationToken = default)
        {
            var locator = await _photobankRepository.GetLocatorAsync(id, cancellationToken);
            if (locator is null)
            {
                return NotFound();
            }

            GraphThumbnail? rawThumbnail;
            try
            {
                rawThumbnail = await _photobankGraphService.GetThumbnailAsync(
                    locator.DriveId, locator.SharePointFileId, size, cancellationToken);
            }
            catch (GraphThrottledException ex)
            {
                Logger.LogWarning("Microsoft Graph thumbnail request throttled for photo {PhotoId}. RetryAfter: {RetryAfter}",
                    id, ex.RetryAfter);
                if (ex.RetryAfter.HasValue)
                {
                    Response.Headers["Retry-After"] = ((long)Math.Ceiling(ex.RetryAfter.Value.TotalSeconds)).ToString();
                }
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            catch (HttpRequestException ex)
            {
                Logger.LogWarning(ex, "Upstream HTTP error fetching thumbnail for photo {PhotoId}", id);
                return StatusCode(StatusCodes.Status502BadGateway);
            }

            if (rawThumbnail is null)
            {
                return NotFound();
            }

            using var thumbnail = rawThumbnail;

            Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
            if (thumbnail.ContentLength.HasValue)
                Response.ContentLength = thumbnail.ContentLength;

            return new FileStreamResult(thumbnail.Content, thumbnail.ContentType);
        }
    }

    public class AddPhotoTagBody
    {
        public string TagName { get; set; } = null!;
    }
}
