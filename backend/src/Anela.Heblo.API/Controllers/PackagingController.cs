using Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/packaging")]
public class PackagingController : BaseApiController
{
    private readonly IMediator _mediator;

    public PackagingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Scans an order: returns order info, eligibility, and creates/fetches a shipment in one call.
    /// Ineligible orders return success: true with eligibility.isEligible: false.
    /// </summary>
    [HttpPost("orders/{orderCode}/scan")]
    public async Task<ActionResult<ScanPackingOrderResponse>> ScanOrder(
        [FromRoute] string orderCode,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new ScanPackingOrderRequest { OrderCode = orderCode }, cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// Resets an order shipment: deletes the existing shipment and creates a new one.
    /// </summary>
    [HttpPost("orders/{orderCode}/shipment/reset")]
    public async Task<ActionResult<ResetOrderShipmentResponse>> ResetShipment(
        [FromRoute] string orderCode,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new ResetOrderShipmentRequest { OrderCode = orderCode }, cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// Proxies a shipment label PDF same-origin so the kiosk iframe can print it.
    /// </summary>
    [HttpGet("orders/{orderCode}/label/pdf")]
    public async Task<IActionResult> GetLabelPdf(
        [FromRoute] string orderCode,
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
