using Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder;
using Anela.Heblo.Application.Features.Packaging.UseCases.DeletePackage;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;
using Anela.Heblo.Application.Features.Packaging.UseCases.ResetOrderShipment;
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Warehouse_Packaging)]
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
    [FeatureAuthorize(Feature.Warehouse_Packaging, AccessLevel.Write)]
    public async Task<ActionResult<ScanPackingOrderResponse>> ScanOrder(
        [FromRoute] string orderCode,
        [FromQuery] int numberOfPackages = 1,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new ScanPackingOrderRequest { OrderCode = orderCode, NumberOfPackages = numberOfPackages },
            cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// Resets an order shipment: deletes the existing shipment and creates a new one.
    /// </summary>
    [HttpPost("orders/{orderCode}/shipment/reset")]
    [FeatureAuthorize(Feature.Warehouse_Packaging, AccessLevel.Write)]
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
    [HttpGet("orders/{orderCode}/packages/{packageNumber:int}/label.pdf")]
    public async Task<ActionResult> GetPackageLabelPdf(
        [FromRoute] string orderCode,
        [FromRoute] int packageNumber,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new GetPackageLabelPdfRequest { OrderCode = orderCode, PackageNumber = packageNumber },
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

    [HttpGet("packages")]
    public async Task<ActionResult<GetPackagesResponse>> GetPackages(
        [FromQuery] GetPackagesRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpDelete("packages/{id:int}")]
    [FeatureAuthorize(Feature.Warehouse_Packaging, AccessLevel.Write)]
    public async Task<ActionResult<DeletePackageResponse>> DeletePackage(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new DeletePackageRequest { Id = id }, cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// Marks the order as packed after all multi-package labels have been printed.
    /// </summary>
    [HttpPost("orders/{orderCode}/packing/complete")]
    [FeatureAuthorize(Feature.Warehouse_Packaging, AccessLevel.Write)]
    public async Task<ActionResult<CompletePackingOrderResponse>> CompletePacking(
        [FromRoute] string orderCode,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new CompletePackingOrderRequest { OrderCode = orderCode },
            cancellationToken);
        return HandleResponse(response);
    }
}
