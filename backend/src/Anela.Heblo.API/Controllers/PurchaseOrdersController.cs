using Anela.Heblo.Application.Features.Purchase.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

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
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] Guid? supplierId = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "OrderDate",
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default)
    {
        var request = new GetPurchaseOrdersRequest(
            searchTerm, status, fromDate, toDate, supplierId,
            pageNumber, pageSize, sortBy, sortDescending);

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

        var response = await _mediator.Send(request, cancellationToken);
        return CreatedAtAction(nameof(GetPurchaseOrderById), new { id = response.Id }, response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetPurchaseOrderByIdResponse>> GetPurchaseOrderById(
        [FromRoute] Guid id,
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

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UpdatePurchaseOrderResponse>> UpdatePurchaseOrder(
        [FromRoute] Guid id,
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
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<UpdatePurchaseOrderStatusResponse>> UpdatePurchaseOrderStatus(
        [FromRoute] Guid id,
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

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<List<PurchaseOrderHistoryDto>>> GetPurchaseOrderHistory(
        [FromRoute] Guid id,
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