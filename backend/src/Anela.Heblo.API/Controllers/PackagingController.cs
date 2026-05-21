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
}
