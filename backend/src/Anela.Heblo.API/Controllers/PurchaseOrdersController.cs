using Anela.Heblo.Application.Features.Purchase.Model;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/purchase-orders")]
public class PurchaseOrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public PurchaseOrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetPurchaseOrdersResponse>> GetPurchaseOrders(
        [FromQuery] GetPurchaseOrdersRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreatePurchaseOrderResponse>> CreatePurchaseOrder(
        [FromBody] CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var response = await _mediator.Send(request, cancellationToken);
            return CreatedAtAction(nameof(GetPurchaseOrderById), new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GetPurchaseOrderByIdResponse>> GetPurchaseOrderById(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var request = new GetPurchaseOrderByIdRequest(id);
        var response = await _mediator.Send(request, cancellationToken);

        if (response == null)
        {
            return NotFound($"Purchase order with ID {id} not found");
        }

        return Ok(response);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UpdatePurchaseOrderResponse>> UpdatePurchaseOrder(
        [FromRoute] int id,
        [FromBody] UpdatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (id != request.Id)
        {
            return BadRequest("Route ID does not match request ID");
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var response = await _mediator.Send(request, cancellationToken);

            if (response == null)
            {
                return NotFound($"Purchase order with ID {id} not found");
            }

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:int}/status")]
    public async Task<ActionResult<UpdatePurchaseOrderStatusResponse>> UpdatePurchaseOrderStatus(
        [FromRoute] int id,
        [FromBody] UpdatePurchaseOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (id != request.Id)
        {
            return BadRequest("Route ID does not match request ID");
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var response = await _mediator.Send(request, cancellationToken);

            if (response == null)
            {
                return NotFound($"Purchase order with ID {id} not found");
            }

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<List<PurchaseOrderHistoryDto>>> GetPurchaseOrderHistory(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var request = new GetPurchaseOrderByIdRequest(id);
        var response = await _mediator.Send(request, cancellationToken);

        if (response == null)
        {
            return NotFound($"Purchase order with ID {id} not found");
        }

        return Ok(response.History);
    }
}