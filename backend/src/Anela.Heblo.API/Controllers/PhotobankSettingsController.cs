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
    [Route("api/photobank/settings")]
    [Authorize(Roles = AuthorizationConstants.Roles.Administrator)]
    public class PhotobankSettingsController : BaseApiController
    {
        private readonly IMediator _mediator;

        public PhotobankSettingsController(IMediator mediator)
        {
            _mediator = mediator;
        }

        // --- Index Roots ---

        /// <summary>
        /// Get configured SharePoint index roots.
        /// </summary>
        [HttpGet("roots")]
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
        [HttpPost("roots")]
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
        [HttpDelete("roots/{id:int}")]
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

        // --- Tag Rules ---

        /// <summary>
        /// Get all tag rules.
        /// </summary>
        [HttpGet("rules")]
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
        [HttpPost("rules")]
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
        [HttpPut("rules/{id:int}")]
        [ProducesResponseType(typeof(UpdateRuleResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UpdateRuleResponse>> UpdateRule(
            int id,
            [FromBody] UpdateRuleRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Id = id;
            var response = await _mediator.Send(request, cancellationToken);
            return HandleResponse(response);
        }

        /// <summary>
        /// Delete a tag rule.
        /// </summary>
        [HttpDelete("rules/{id:int}")]
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
        /// Re-apply all active tag rules. Deletes Rule-sourced tags and recomputes them.
        /// Manual tags are never touched.
        /// </summary>
        [HttpPost("rules/reapply")]
        [ProducesResponseType(typeof(ReapplyRulesResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<ReapplyRulesResponse>> ReapplyRules(CancellationToken cancellationToken = default)
        {
            var response = await _mediator.Send(new ReapplyRulesRequest(), cancellationToken);
            return HandleResponse(response);
        }
    }
}
