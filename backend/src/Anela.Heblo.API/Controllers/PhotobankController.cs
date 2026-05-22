using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddPhotoTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddRoot;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddRule;
using Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTagByIds;
using Anela.Heblo.Application.Features.Photobank.UseCases.CreateTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteRoot;
using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteRule;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetPhotos;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetRoots;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetRules;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetTags;
using Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules;
using Anela.Heblo.Application.Features.Photobank.UseCases.RemovePhotoTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.RetagPhotos;
using Anela.Heblo.Application.Features.Photobank.UseCases.UpdateRule;
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
        /// Get photos with optional tag AND filter, path search (matches folderPath/fileName), and pagination.
        /// Set useRegex=true to use POSIX regex matching on the full path instead of substring search.
        /// </summary>
        [HttpGet("photos")]
        [ProducesResponseType(typeof(GetPhotosResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<GetPhotosResponse>> GetPhotos(
            [FromQuery] List<string>? tags,
            [FromQuery] string? search,
            [FromQuery] bool useRegex = false,
            [FromQuery] bool withoutTags = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 48,
            CancellationToken cancellationToken = default)
        {
            var request = new GetPhotosRequest
            {
                Tags = tags,
                Search = search,
                UseRegex = useRegex,
                WithoutTags = withoutTags,
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
        /// Create a new tag. Requires super user role.
        /// </summary>
        [HttpPost("tags")]
        [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
        [ProducesResponseType(typeof(CreateTagResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<CreateTagResponse>> CreateTag([FromBody] CreateTagBody body, CancellationToken ct)
        {
            var response = await _mediator.Send(new CreateTagRequest { Name = body.Name }, ct);
            return HandleResponse(response);
        }

        /// <summary>
        /// Delete a tag by ID. Requires super user role.
        /// </summary>
        [HttpDelete("tags/{id:int}")]
        [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
        [ProducesResponseType(typeof(DeleteTagResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeleteTagResponse>> DeleteTag(int id, CancellationToken ct)
        {
            var response = await _mediator.Send(new DeleteTagRequest { Id = id }, ct);
            return HandleResponse(response);
        }

        /// <summary>
        /// Add a manual tag to a photo. Requires administrator role.
        /// </summary>
        [HttpPost("photos/{id:int}/tags")]
        [Authorize(Roles = AuthorizationConstants.Roles.MarketingWriter)]
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
        [Authorize(Roles = AuthorizationConstants.Roles.MarketingWriter)]
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

        /// <summary>
        /// Bulk-add a manual tag to all photos matching the given filters. Requires administrator role.
        /// At least one filter (Search, FolderPath, or Tags) must be provided.
        /// Capped at 5 000 matching photos per call.
        /// </summary>
        [HttpPost("photos/bulk-tag")]
        [Authorize(Roles = AuthorizationConstants.Roles.MarketingWriter)]
        [ProducesResponseType(typeof(BulkAddPhotoTagResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<BulkAddPhotoTagResponse>> BulkAddPhotoTag(
            [FromBody] BulkAddPhotoTagBody body,
            CancellationToken cancellationToken = default)
        {
            var request = new BulkAddPhotoTagRequest
            {
                Tags = body.Tags,
                Search = body.Search,
                TagName = body.TagName,
            };
            var response = await _mediator.Send(request, cancellationToken);
            return HandleResponse(response);
        }

        /// <summary>
        /// Bulk-add a manual tag to an explicit list of photos by ID. Requires MarketingWriter role.
        /// Capped at 5 000 photo IDs per call. Idempotent: photos already carrying the tag are counted
        /// in AlreadyTaggedCount and not modified.
        /// </summary>
        [HttpPost("photos/tag-by-ids")]
        [Authorize(Roles = AuthorizationConstants.Roles.MarketingWriter)]
        [ProducesResponseType(typeof(BulkAddPhotoTagByIdsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<BulkAddPhotoTagByIdsResponse>> BulkAddPhotoTagByIds(
            [FromBody] BulkAddPhotoTagByIdsBody body,
            CancellationToken cancellationToken = default)
        {
            var request = new BulkAddPhotoTagByIdsRequest
            {
                PhotoIds = body.PhotoIds,
                TagName = body.TagName,
            };
            var response = await _mediator.Send(request, cancellationToken);
            return HandleResponse(response);
        }

        /// <summary>
        /// Re-tag specific photos via AI. Resets LastAutoTaggedAt and enqueues a Hangfire job.
        /// Optionally clears existing AI tags before re-processing.
        /// </summary>
        [HttpPost("photos/auto-tag")]
        [Authorize(Roles = AuthorizationConstants.Roles.MarketingWriter)]
        [ProducesResponseType(typeof(RetagPhotosResponse), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<RetagPhotosResponse>> RetagPhotos(
            [FromBody] RetagPhotosRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = await _mediator.Send(request, cancellationToken);
            return result.Success ? Accepted(result) : BadRequest(result);
        }

        // --- Settings: Index Roots ---

        /// <summary>
        /// Get configured SharePoint index roots.
        /// </summary>
        [HttpGet("settings/roots")]
        [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
        [ProducesResponseType(typeof(GetRootsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<GetRootsResponse>> GetRoots(CancellationToken cancellationToken = default)
        {
            var response = await _mediator.Send(new GetRootsRequest(), cancellationToken);
            return HandleResponse(response);
        }

        /// <summary>
        /// Add a new SharePoint index root.
        /// </summary>
        [HttpPost("settings/roots")]
        [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
        [ProducesResponseType(typeof(AddRootResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<AddRootResponse>> AddRoot(
            [FromBody] AddRootRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = await _mediator.Send(request, cancellationToken);
            if (response.Success)
                return CreatedAtAction(nameof(GetRoots), response);
            return HandleResponse(response);
        }

        /// <summary>
        /// Delete a SharePoint index root.
        /// </summary>
        [HttpDelete("settings/roots/{id:int}")]
        [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
        [ProducesResponseType(typeof(DeleteRootResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeleteRootResponse>> DeleteRoot(
            int id,
            CancellationToken cancellationToken = default)
        {
            var response = await _mediator.Send(new DeleteRootRequest { Id = id }, cancellationToken);
            return HandleResponse(response);
        }

        // --- Settings: Tag Rules ---

        /// <summary>
        /// Get all tag rules.
        /// </summary>
        [HttpGet("settings/rules")]
        [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
        [ProducesResponseType(typeof(GetRulesResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<GetRulesResponse>> GetRules(CancellationToken cancellationToken = default)
        {
            var response = await _mediator.Send(new GetRulesRequest(), cancellationToken);
            return HandleResponse(response);
        }

        /// <summary>
        /// Add a new tag rule.
        /// </summary>
        [HttpPost("settings/rules")]
        [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
        [ProducesResponseType(typeof(AddRuleResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<AddRuleResponse>> AddRule(
            [FromBody] AddRuleRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = await _mediator.Send(request, cancellationToken);
            if (response.Success)
                return CreatedAtAction(nameof(GetRules), response);
            return HandleResponse(response);
        }

        /// <summary>
        /// Update an existing tag rule.
        /// </summary>
        [HttpPut("settings/rules/{id:int}")]
        [ProducesResponseType(typeof(UpdateRuleResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
        public async Task<ActionResult<UpdateRuleResponse>> UpdateRule(
            int id,
            [FromBody] UpdateRuleRequest request,
            CancellationToken cancellationToken = default)
        {
            var command = new UpdateRuleRequest
            {
                Id = id,
                PathPattern = request.PathPattern,
                TagName = request.TagName,
                IsActive = request.IsActive,
                SortOrder = request.SortOrder,
            };
            var response = await _mediator.Send(command, cancellationToken);
            return HandleResponse(response);
        }

        /// <summary>
        /// Delete a tag rule.
        /// </summary>
        [HttpDelete("settings/rules/{id:int}")]
        [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
        [ProducesResponseType(typeof(DeleteRuleResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeleteRuleResponse>> DeleteRule(
            int id,
            CancellationToken cancellationToken = default)
        {
            var response = await _mediator.Send(new DeleteRuleRequest { Id = id }, cancellationToken);
            return HandleResponse(response);
        }

        /// <summary>
        /// Re-apply all active tag rules. Deletes all Rule-sourced tags and recomputes them from scratch.
        /// Manual and AI tags are never touched.
        /// </summary>
        [HttpPost("settings/rules/reapply")]
        [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
        [ProducesResponseType(typeof(ReapplyRulesResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<ReapplyRulesResponse>> ReapplyRules(CancellationToken cancellationToken = default)
        {
            var response = await _mediator.Send(new ReapplyRulesRequest(), cancellationToken);
            return HandleResponse(response);
        }

        /// <summary>
        /// Re-apply a single tag rule. Only Rule-sourced tags for the tag name this rule produces
        /// are removed and recomputed; all other rules' tags are left untouched.
        /// </summary>
        [HttpPost("settings/rules/{id:int}/reapply")]
        [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
        [ProducesResponseType(typeof(ReapplyRulesResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ReapplyRulesResponse>> ReapplyRule(int id, CancellationToken cancellationToken = default)
        {
            var response = await _mediator.Send(new ReapplyRulesRequest { RuleId = id }, cancellationToken);
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
            var response = await _mediator.Send(
                new GetThumbnailRequest { Id = id, Size = size }, cancellationToken);

            if (response.Success)
            {
                Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
                if (response.ContentLength.HasValue)
                {
                    Response.ContentLength = response.ContentLength;
                }

                return new FileStreamResult(response.Content!, response.ContentType!);
            }

            switch (response.ErrorCode)
            {
                case ErrorCodes.PhotobankThumbnailNotFound:
                    return NotFound();
                case ErrorCodes.PhotobankThumbnailThrottled:
                    if (response.RetryAfterSeconds.HasValue)
                    {
                        Response.Headers["Retry-After"] = response.RetryAfterSeconds.Value.ToString();
                    }
                    return StatusCode(StatusCodes.Status503ServiceUnavailable);
                case ErrorCodes.PhotobankThumbnailAuthUnavailable:
                    return StatusCode(StatusCodes.Status503ServiceUnavailable);
                case ErrorCodes.PhotobankThumbnailUpstream:
                    return StatusCode(StatusCodes.Status502BadGateway);
                default:
                    return StatusCode(StatusCodes.Status502BadGateway);
            }
        }
    }
}
