using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MarketingCalendarController : BaseApiController
    {
        private readonly IMediator _mediator;

        public MarketingCalendarController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Get marketing actions with optional filtering and pagination
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(GetMarketingActionsResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<GetMarketingActionsResponse>> GetMarketingActions(
            [FromQuery] GetMarketingActionsRequest request)
        {
            var response = await _mediator.Send(request);
            return HandleResponse(response);
        }

        /// <summary>
        /// Get specific marketing action by ID
        /// </summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(GetMarketingActionResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<GetMarketingActionResponse>> GetMarketingAction(int id)
        {
            var request = new GetMarketingActionRequest { Id = id };
            var response = await _mediator.Send(request);
            return HandleResponse(response);
        }

        /// <summary>
        /// Get lightweight calendar view of marketing actions for a date range
        /// </summary>
        [HttpGet("calendar")]
        [ProducesResponseType(typeof(GetMarketingCalendarResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<GetMarketingCalendarResponse>> GetCalendar(
            [FromQuery] GetMarketingCalendarRequest request)
        {
            var response = await _mediator.Send(request);
            return HandleResponse(response);
        }

        /// <summary>
        /// Create a new marketing action
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(CreateMarketingActionResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<CreateMarketingActionResponse>> CreateMarketingAction(
            [FromBody] CreateMarketingActionRequest request)
        {
            var response = await _mediator.Send(request);
            if (response.Success)
                return CreatedAtAction(nameof(GetMarketingAction), new { id = response.Id }, response);
            return HandleResponse(response);
        }

        /// <summary>
        /// Update an existing marketing action
        /// </summary>
        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(UpdateMarketingActionResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<UpdateMarketingActionResponse>> UpdateMarketingAction(
            int id,
            [FromBody] UpdateMarketingActionRequest request)
        {
            request.Id = id;
            var response = await _mediator.Send(request);
            return HandleResponse(response);
        }

        /// <summary>
        /// Delete a marketing action (soft delete)
        /// </summary>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(typeof(DeleteMarketingActionResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<DeleteMarketingActionResponse>> DeleteMarketingAction(int id)
        {
            var request = new DeleteMarketingActionRequest { Id = id };
            var response = await _mediator.Send(request);
            return HandleResponse(response);
        }

        /// <summary>
        /// Import marketing actions from the configured Outlook calendar (admin only)
        /// </summary>
        [HttpPost("import-from-outlook")]
        [ProducesResponseType(typeof(ImportFromOutlookResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<ImportFromOutlookResponse>> ImportFromOutlook(
            [FromBody] ImportFromOutlookRequest request,
            CancellationToken cancellationToken)
        {
            var response = await _mediator.Send(request, cancellationToken);
            return HandleResponse(response);
        }
    }
}
