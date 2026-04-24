using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Domain.Features.Authorization;
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

        public PhotobankController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Get photos with optional tag AND filter, filename search, and pagination.
        /// </summary>
        [HttpGet("photos")]
        [ProducesResponseType(typeof(GetPhotosResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<GetPhotosResponse>> GetPhotos(
            [FromQuery] List<string>? tags,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 48,
            CancellationToken cancellationToken = default)
        {
            var request = new GetPhotosRequest
            {
                Tags = tags,
                Search = search,
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
    }

    public class AddPhotoTagBody
    {
        public string TagName { get; set; } = null!;
    }
}
