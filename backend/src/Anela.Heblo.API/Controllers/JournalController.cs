using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Anela.Heblo.Application.Features.Journal.Contracts;

namespace Anela.Heblo.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class JournalController : BaseApiController
    {
        private readonly IMediator _mediator;

        public JournalController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Get journal entries with optional filtering and pagination
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(GetJournalEntriesResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<GetJournalEntriesResponse>> GetJournalEntries([FromQuery] GetJournalEntriesRequest request)
        {
            var response = await _mediator.Send(request);
            return HandleResponse(response);
        }

        /// <summary>
        /// Search journal entries with advanced filtering
        /// </summary>
        [HttpGet("search")]
        [ProducesResponseType(typeof(SearchJournalEntriesResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<SearchJournalEntriesResponse>> SearchJournalEntries([FromQuery] SearchJournalEntriesRequest request)
        {
            var response = await _mediator.Send(request);
            return HandleResponse(response);
        }

        /// <summary>
        /// Get specific journal entry by ID
        /// </summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(GetJournalEntryResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<GetJournalEntryResponse>> GetJournalEntry(int id)
        {
            var request = new GetJournalEntryRequest { Id = id };
            var response = await _mediator.Send(request);

            return HandleResponse(response);
        }

        /// <summary>
        /// Create new journal entry
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(CreateJournalEntryResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<CreateJournalEntryResponse>> CreateJournalEntry([FromBody] CreateJournalEntryRequest request)
        {
            var response = await _mediator.Send(request);

            if (response.Success)
            {
                return CreatedAtAction(nameof(GetJournalEntry), new { id = response.Id }, response);
            }

            return HandleResponse(response);
        }

        /// <summary>
        /// Update existing journal entry
        /// </summary>
        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(UpdateJournalEntryResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<UpdateJournalEntryResponse>> UpdateJournalEntry(int id, [FromBody] UpdateJournalEntryRequest request)
        {
            request.Id = id;
            var response = await _mediator.Send(request);

            return HandleResponse(response);
        }

        /// <summary>
        /// Delete journal entry (soft delete)
        /// </summary>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(typeof(DeleteJournalEntryResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<DeleteJournalEntryResponse>> DeleteJournalEntry(int id)
        {
            var request = new DeleteJournalEntryRequest { Id = id };
            var response = await _mediator.Send(request);

            return HandleResponse(response);
        }

        /// <summary>
        /// Get all available tags
        /// </summary>
        [HttpGet("tags")]
        [ProducesResponseType(typeof(GetJournalTagsResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<GetJournalTagsResponse>> GetJournalTags()
        {
            var request = new GetJournalTagsRequest();
            var response = await _mediator.Send(request);
            return HandleResponse(response);
        }

        /// <summary>
        /// Create new tag
        /// </summary>
        [HttpPost("tags")]
        [ProducesResponseType(typeof(CreateJournalTagResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<CreateJournalTagResponse>> CreateJournalTag([FromBody] CreateJournalTagRequest request)
        {
            var response = await _mediator.Send(request);

            if (response.Success)
            {
                return CreatedAtAction(nameof(GetJournalTags), response);
            }

            return HandleResponse(response);
        }
    }
}