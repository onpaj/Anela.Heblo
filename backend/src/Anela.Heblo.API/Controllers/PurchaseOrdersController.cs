using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderById;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrders;
using Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderInvoiceAcquired;
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderStatus;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.API.Infrastructure;
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
            return BadRequest(ErrorResponseHelper.CreateValidationError<CreatePurchaseOrderResponse>());
        }

        try
        {
            var response = await _mediator.Send(request, cancellationToken);
            return CreatedAtAction(nameof(GetPurchaseOrderById), new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ErrorResponseHelper.CreateBusinessError<CreatePurchaseOrderResponse>(ErrorCodes.InvalidValue, ex.Message));
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
            return NotFound(ErrorResponseHelper.CreateNotFoundError<GetPurchaseOrderByIdResponse>(ErrorCodes.PurchaseOrderNotFound, id.ToString()));
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
            return BadRequest(ErrorResponseHelper.CreateErrorResponse<UpdatePurchaseOrderResponse>(
                ErrorCodes.ValidationError,
                new Dictionary<string, string> { { "detail", "Route ID does not match request ID" } }));
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ErrorResponseHelper.CreateValidationError<UpdatePurchaseOrderResponse>());
        }

        try
        {
            var response = await _mediator.Send(request, cancellationToken);

            if (response == null)
            {
                return NotFound(ErrorResponseHelper.CreateNotFoundError<UpdatePurchaseOrderResponse>(
                    ErrorCodes.PurchaseOrderNotFound, id.ToString()));
            }

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ErrorResponseHelper.CreateBusinessError<UpdatePurchaseOrderResponse>(
                ErrorCodes.InvalidOperation, ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ErrorResponseHelper.CreateBusinessError<UpdatePurchaseOrderResponse>(
                ErrorCodes.InvalidValue, ex.Message));
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
            return BadRequest(ErrorResponseHelper.CreateErrorResponse<UpdatePurchaseOrderStatusResponse>(
                ErrorCodes.ValidationError,
                new Dictionary<string, string> { { "detail", "Route ID does not match request ID" } }));
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ErrorResponseHelper.CreateValidationError<UpdatePurchaseOrderStatusResponse>());
        }

        try
        {
            var response = await _mediator.Send(request, cancellationToken);

            if (response == null)
            {
                return NotFound(ErrorResponseHelper.CreateNotFoundError<UpdatePurchaseOrderStatusResponse>(
                    ErrorCodes.PurchaseOrderNotFound, id.ToString()));
            }

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ErrorResponseHelper.CreateError<UpdatePurchaseOrderStatusResponse>(ex));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ErrorResponseHelper.CreateBusinessError<UpdatePurchaseOrderStatusResponse>(
                ErrorCodes.StatusTransitionNotAllowed, ex.Message));
        }
    }

    [HttpPut("{id:int}/invoice-acquired")]
    public async Task<ActionResult<UpdatePurchaseOrderInvoiceAcquiredResponse>> UpdateInvoiceAcquired(
        [FromRoute] int id,
        [FromBody] UpdatePurchaseOrderInvoiceAcquiredRequest request,
        CancellationToken cancellationToken)
    {
        if (id != request.Id)
        {
            return BadRequest(ErrorResponseHelper.CreateErrorResponse<UpdatePurchaseOrderInvoiceAcquiredResponse>(
                ErrorCodes.ValidationError,
                new Dictionary<string, string> { { "detail", "ID in route does not match ID in request body" } }));
        }

        try
        {
            var response = await _mediator.Send(request, cancellationToken);

            if (response == null)
            {
                return NotFound(ErrorResponseHelper.CreateNotFoundError<UpdatePurchaseOrderInvoiceAcquiredResponse>(
                    ErrorCodes.PurchaseOrderNotFound, id.ToString()));
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorResponseHelper.CreateBusinessError<UpdatePurchaseOrderInvoiceAcquiredResponse>(
                ErrorCodes.InvalidOperation, ex.Message));
        }
    }

    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<ListResponse<PurchaseOrderHistoryDto>>> GetPurchaseOrderHistory(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var request = new GetPurchaseOrderByIdRequest(id);
        var response = await _mediator.Send(request, cancellationToken);

        if (response == null)
        {
            return NotFound(ErrorResponseHelper.CreateNotFoundError<ListResponse<PurchaseOrderHistoryDto>>(
                ErrorCodes.PurchaseOrderNotFound, id.ToString()));
        }

        var listResponse = new ListResponse<PurchaseOrderHistoryDto>
        {
            Items = response.History,
            TotalCount = response.History.Count
        };
        return Ok(listResponse);
    }

    [HttpPost("recalculate-purchase-price")]
    public async Task<ActionResult<RecalculatePurchasePriceResponse>> RecalculatePurchasePrice(
        [FromBody] RecalculatePurchasePriceRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ErrorResponseHelper.CreateValidationError<RecalculatePurchasePriceResponse>());
        }

        try
        {
            var response = await _mediator.Send(request, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ErrorResponseHelper.CreateError<RecalculatePurchasePriceResponse>(ex));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ErrorResponseHelper.CreateError<RecalculatePurchasePriceResponse>(ex));
        }
    }
}