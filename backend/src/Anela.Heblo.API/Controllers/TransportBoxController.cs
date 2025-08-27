using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/transport-boxes")]
public class TransportBoxController : ControllerBase
{
    private readonly IMediator _mediator;

    public TransportBoxController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get transport boxes with optional filtering and pagination
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<GetTransportBoxesResponse>> GetTransportBoxes(
        [FromQuery] GetTransportBoxesRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Get transport box summary with state counts
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<GetTransportBoxSummaryResponse>> GetTransportBoxSummary(
        [FromQuery] GetTransportBoxSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Get transport box by ID with full details
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<GetTransportBoxByIdResponse>> GetTransportBoxById(
        int id,
        CancellationToken cancellationToken = default)
    {
        var request = new GetTransportBoxByIdRequest { Id = id };
        var response = await _mediator.Send(request, cancellationToken);

        if (response.TransportBox == null)
        {
            return NotFound($"Transport box with ID {id} not found");
        }

        return Ok(response);
    }

    /// <summary>
    /// Change transport box state
    /// </summary>
    [HttpPut("{id:int}/state")]
    public async Task<ActionResult<ChangeTransportBoxStateResponse>> ChangeTransportBoxState(
        int id,
        [FromBody] ChangeTransportBoxStateRequest request,
        CancellationToken cancellationToken = default)
    {
        request.BoxId = id; // Ensure consistency
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            return BadRequest(response.ErrorMessage);
        }

        return Ok(response);
    }

    /// <summary>
    /// Create a new transport box in 'New' state
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateNewTransportBoxResponse>> CreateNewTransportBox(
        [FromBody] CreateNewTransportBoxRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            return BadRequest(response.ErrorMessage);
        }

        return Ok(response);
    }


    /// <summary>
    /// Add item to transport box (only allowed in 'Opened' state)
    /// </summary>
    [HttpPost("{id:int}/items")]
    public async Task<ActionResult<AddItemToBoxResponse>> AddItemToBox(
        int id,
        [FromBody] AddItemToBoxRequest request,
        CancellationToken cancellationToken = default)
    {
        request.BoxId = id; // Ensure consistency
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            return BadRequest(response.ErrorMessage);
        }

        return Ok(response);
    }

    /// <summary>
    /// Remove item from transport box (only allowed in 'Opened' state)
    /// </summary>
    [HttpDelete("{id:int}/items/{itemId:int}")]
    public async Task<ActionResult<RemoveItemFromBoxResponse>> RemoveItemFromBox(
        int id,
        int itemId,
        CancellationToken cancellationToken = default)
    {
        var request = new RemoveItemFromBoxRequest
        {
            BoxId = id,
            ItemId = itemId
        };

        var response = await _mediator.Send(request, cancellationToken);
        if (!response.Success)
        {
            return BadRequest(response.ErrorMessage);
        }
        return Ok(response);
    }

    /// <summary>
    /// Confirm transit with box number verification and transition to 'InTransit' state
    /// </summary>
    [HttpPut("{id:int}/confirm-transit")]
    public async Task<ActionResult<ConfirmTransitResponse>> ConfirmTransit(
        int id,
        [FromBody] ConfirmTransitRequest request,
        CancellationToken cancellationToken = default)
    {
        request.BoxId = id; // Ensure consistency
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            return BadRequest(response.ErrorMessage);
        }

        return Ok(response);
    }
}