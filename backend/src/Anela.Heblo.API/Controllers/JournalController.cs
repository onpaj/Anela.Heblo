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
    public class JournalController : ControllerBase
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
        public async Task<IActionResult> GetJournalEntries([FromQuery] GetJournalEntriesRequest request)
        {
            var response = await _mediator.Send(request);
            return Ok(response);
        }

        /// <summary>
        /// Search journal entries with advanced filtering
        /// </summary>
        [HttpGet("search")]
        [ProducesResponseType(typeof(SearchJournalEntriesResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> SearchJournalEntries([FromQuery] SearchJournalEntriesRequest request)
        {
            var response = await _mediator.Send(request);
            return Ok(response);
        }

        /// <summary>
        /// Get specific journal entry by ID
        /// </summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(JournalEntryDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetJournalEntry(int id)
        {
            var request = new GetJournalEntryRequest { Id = id };
            var response = await _mediator.Send(request);

            if (response == null)
                return NotFound($"Journal entry with ID {id} was not found.");

            return Ok(response);
        }

        /// <summary>
        /// Create new journal entry
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(CreateJournalEntryResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateJournalEntry([FromBody] CreateJournalEntryRequest request)
        {
            var response = await _mediator.Send(request);
            return CreatedAtAction(nameof(GetJournalEntry), new { id = response.Id }, response);
        }

        /// <summary>
        /// Update existing journal entry
        /// </summary>
        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(UpdateJournalEntryResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> UpdateJournalEntry(int id, [FromBody] UpdateJournalEntryRequest request)
        {
            request.Id = id;
            var response = await _mediator.Send(request);
            return Ok(response);
        }

        /// <summary>
        /// Delete journal entry (soft delete)
        /// </summary>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> DeleteJournalEntry(int id)
        {
            var request = new DeleteJournalEntryRequest { Id = id };
            await _mediator.Send(request);
            return NoContent();
        }

        /// <summary>
        /// Get all available tags
        /// </summary>
        [HttpGet("tags")]
        [ProducesResponseType(typeof(GetJournalTagsResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetJournalTags()
        {
            var request = new GetJournalTagsRequest();
            var response = await _mediator.Send(request);
            return Ok(response);
        }

        /// <summary>
        /// Create new tag
        /// </summary>
        [HttpPost("tags")]
        [ProducesResponseType(typeof(CreateJournalTagResponse), StatusCodes.Status201Created)]
        public async Task<IActionResult> CreateJournalTag([FromBody] CreateJournalTagRequest request)
        {
            var response = await _mediator.Send(request);
            return CreatedAtAction(nameof(GetJournalTags), response);
        }
    }
}