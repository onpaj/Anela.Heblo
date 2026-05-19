using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/shipment-labels")]
public class ShipmentLabelsController : BaseApiController
{
    private readonly IMediator _mediator;

    public ShipmentLabelsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns shipment label payloads (PDF URL and/or ZPL) for an order.
    /// The Baleni kiosk uses these to print on a USB-connected Zebra printer.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<GetOrderShipmentLabelsResponse>> GetLabels(
        [FromBody] GetShipmentLabelsRequest body)
    {
        var response = await _mediator.Send(new GetOrderShipmentLabelsRequest
        {
            OrderCode = body.OrderCode,
        });

        return HandleResponse(response);
    }
}

public class GetShipmentLabelsRequest
{
    [JsonPropertyName("orderCode")]
    public string OrderCode { get; set; } = null!;
}
