using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;
using Anela.Heblo.Application.Shared;
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

    /// <summary>
    /// Creates a new shipment for an order via the Shoptet API and returns label data.
    /// </summary>
    [HttpPost("create")]
    public async Task<ActionResult<CreateOrderShipmentResponse>> CreateShipment(
        [FromBody] CreateShipmentRequest body,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new CreateOrderShipmentRequest
        {
            OrderCode = body.OrderCode,
            ForceCreate = body.ForceCreate,
        }, cancellationToken);

        return HandleResponse(response);
    }

    /// <summary>
    /// Proxies a shipment label PDF same-origin so the kiosk iframe can print it.
    /// Resolves the carrier URL server-side — the frontend never receives a raw external URL.
    /// </summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> GetLabelPdf(
        [FromQuery] string orderCode,
        [FromQuery] Guid shipmentGuid,
        [FromQuery] string packageName,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetShipmentLabelPdfRequest
        {
            OrderCode = orderCode,
            ShipmentGuid = shipmentGuid,
            PackageName = packageName,
        }, cancellationToken);

        if (!response.Success)
        {
            return response.ErrorCode == ErrorCodes.ShipmentLabelPdfNotFound
                ? NotFound(new { errorCode = response.ErrorCode?.ToString() })
                : StatusCode(500, new { errorCode = response.ErrorCode?.ToString() });
        }

        return File(response.PdfStream!, "application/pdf");
    }
}

public class GetShipmentLabelsRequest
{
    [JsonPropertyName("orderCode")]
    public string OrderCode { get; set; } = null!;
}

public class CreateShipmentRequest
{
    [JsonPropertyName("orderCode")]
    public string OrderCode { get; set; } = null!;

    [JsonPropertyName("forceCreate")]
    public bool ForceCreate { get; set; }
}
