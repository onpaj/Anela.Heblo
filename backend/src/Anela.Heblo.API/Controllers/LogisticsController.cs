using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.DisassembleGiftPackage;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.EnqueueGiftPackageManufacture;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetAvailableGiftPackages;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageDetail;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetManufactureLog;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/logistics")]
public class LogisticsController : BaseApiController
{
    private readonly IMediator _mediator;

    public LogisticsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get available gift package types (basic info only, without ingredients)
    /// </summary>
    [HttpGet("gift-packages/available")]
    public async Task<ActionResult<GetAvailableGiftPackagesResponse>> GetAvailableGiftPackages(
        [FromQuery] decimal salesCoefficient = 1.0m,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GetAvailableGiftPackagesRequest
        {
            SalesCoefficient = salesCoefficient,
            FromDate = fromDate,
            ToDate = toDate
        };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// Get detailed gift package information with ingredients/BOM
    /// </summary>
    [HttpGet("gift-packages/{giftPackageCode}/detail")]
    public async Task<ActionResult<GetGiftPackageDetailResponse>> GetGiftPackageDetail(
        string giftPackageCode,
        [FromQuery] decimal salesCoefficient = 1.0m,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GetGiftPackageDetailRequest
        {
            GiftPackageCode = giftPackageCode,
            SalesCoefficient = salesCoefficient,
            FromDate = fromDate,
            ToDate = toDate
        };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }


    /// <summary>
    /// Execute gift package manufacturing process
    /// </summary>
    [HttpPost("gift-packages/manufacture")]
    public async Task<ActionResult<CreateGiftPackageManufactureResponse>> CreateGiftPackageManufacture(
        [FromBody] CreateGiftPackageManufactureRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// Disassemble gift package back to individual components
    /// </summary>
    [HttpPost("gift-packages/disassemble")]
    public async Task<ActionResult<DisassembleGiftPackageResponse>> DisassembleGiftPackage(
        [FromBody] DisassembleGiftPackageRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// Queue gift package manufacturing process as background job
    /// </summary>
    [HttpPost("gift-packages/manufacture/enqueue")]
    public async Task<ActionResult<EnqueueGiftPackageManufactureResponse>> EnqueueGiftPackageManufacture(
        [FromBody] EnqueueGiftPackageManufactureRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// View gift package manufacturing history
    /// </summary>
    [HttpGet("gift-packages/manufacture-log")]
    public async Task<ActionResult<GetManufactureLogResponse>> GetManufactureLog(
        [FromQuery] int count = 10,
        CancellationToken cancellationToken = default)
    {
        var request = new GetManufactureLogRequest { Count = count };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
}