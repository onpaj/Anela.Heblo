using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;
using Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
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
    /// Streams the carrier-issued label PDF for a single package through our own origin.
    /// The same-origin proxy lets the SPA silent-print the label without CORS errors.
    /// </summary>
    [HttpGet("orders/{orderCode}/packages/{packageName}/label.pdf")]
    public async Task<ActionResult> GetPackageLabelPdf(
        [FromRoute] string orderCode,
        [FromRoute] string packageName,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new GetPackageLabelPdfRequest { OrderCode = orderCode, PackageName = packageName },
            cancellationToken);

        if (!response.Success || response.Content is null)
        {
            return response.ErrorCode switch
            {
                Application.Shared.ErrorCodes.PackageLabelNotFound => NotFound(response),
                Application.Shared.ErrorCodes.PackageLabelDownloadFailed => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
                _ => BadRequest(response),
            };
        }

        Response.Headers.CacheControl = "no-store";
        return File(response.Content, response.ContentType, response.FileName);
    }
}
