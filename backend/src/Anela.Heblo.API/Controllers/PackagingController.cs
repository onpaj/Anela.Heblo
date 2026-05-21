using Anela.Heblo.Application.Features.Packaging.UseCases.PrepareOrderLabel;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetShipmentLabelPdf;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

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
    /// Ensures a printable label exists for the order:
    /// checks eligibility, returns existing labels if present (unless forceRecreate),
    /// otherwise creates a shipment and polls until labels are ready.
    /// </summary>
    [HttpPost("orders/{orderCode}/label")]
    public async Task<ActionResult<PrepareOrderLabelResponse>> PrepareLabel(
        [FromRoute] string orderCode,
        [FromBody] PrepareOrderLabelBody body,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new PrepareOrderLabelRequest
        {
            OrderCode = orderCode,
            ForceRecreate = body.ForceRecreate,
        }, cancellationToken);

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

public class PrepareOrderLabelBody
{
    [JsonPropertyName("forceRecreate")]
    public bool ForceRecreate { get; set; }
}
