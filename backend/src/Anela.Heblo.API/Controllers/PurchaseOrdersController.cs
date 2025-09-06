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
public class PurchaseOrdersController : BaseApiController
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

        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            return HandleResponse(response);
        }

        return CreatedAtAction(nameof(GetPurchaseOrderById), new { id = response.Id }, response);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GetPurchaseOrderByIdResponse>> GetPurchaseOrderById(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var request = new GetPurchaseOrderByIdRequest(id);
        var response = await _mediator.Send(request, cancellationToken);

        return HandleResponse(response);
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

        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
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

        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
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

        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<ListResponse<PurchaseOrderHistoryDto>>> GetPurchaseOrderHistory(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var request = new GetPurchaseOrderByIdRequest(id);
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            return HandleResponse<ListResponse<PurchaseOrderHistoryDto>>(new ListResponse<PurchaseOrderHistoryDto>(response.ErrorCode!.Value, response.Params));
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

        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
}