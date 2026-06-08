using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[GateOn(Feature.Warehouse_Expedition)]
[ApiController]
[Route("api/shoptet-orders")]
public class ShoptetOrdersController : BaseApiController
{
    private readonly IMediator _mediator;

    public ShoptetOrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Block an order from processing: validates source state, changes status to the configured
    /// blocked state, and writes an internal note. Requires the order to be in one of the
    /// allowed source states configured in ShoptetOrders:AllowedBlockSourceStateIds.
    /// </summary>
    [HttpPatch("{code}/block")]
    public async Task<ActionResult<BlockOrderProcessingResponse>> BlockOrder(
        string code,
        [FromBody] BlockOrderRequest body)
    {
        var response = await _mediator.Send(new BlockOrderProcessingRequest
        {
            OrderCode = code,
            Note = body.Note,
        });

        if (!response.Success)
            return HandleResponse(response);

        return NoContent();
    }

    /// <summary>
    /// Loads a single order for the Balení packing screen: header, customer, shipping
    /// method, cooling status, and expanded item list with product images.
    /// </summary>
    [HttpGet("{code}/packing")]
    public async Task<ActionResult<GetPackingOrderResponse>> GetPackingOrder(string code)
    {
        var response = await _mediator.Send(new GetPackingOrderRequest { Code = code });
        return HandleResponse(response);
    }
}
