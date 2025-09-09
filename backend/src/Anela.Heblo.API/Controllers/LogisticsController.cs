using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;
using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.GetAvailableGiftPackages;
using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.GetGiftPackageDetail;
using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.GetManufactureLog;
using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.ValidateGiftPackageStock;
using Anela.Heblo.Domain.Features.Users;
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
    private readonly ICurrentUserService _currentUserService;

    public LogisticsController(IMediator mediator, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Get available gift package types (basic info only, without ingredients)
    /// </summary>
    [HttpGet("gift-packages/available")]
    public async Task<ActionResult<GetAvailableGiftPackagesResponse>> GetAvailableGiftPackages(
        CancellationToken cancellationToken)
    {
        var request = new GetAvailableGiftPackagesRequest();
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// Get detailed gift package information with ingredients/BOM
    /// </summary>
    [HttpGet("gift-packages/{giftPackageCode}/detail")]
    public async Task<ActionResult<GetGiftPackageDetailResponse>> GetGiftPackageDetail(
        string giftPackageCode,
        CancellationToken cancellationToken)
    {
        var request = new GetGiftPackageDetailRequest { GiftPackageCode = giftPackageCode };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// Validate source product availability for gift package manufacturing
    /// </summary>
    [HttpPost("gift-packages/validate-stock")]
    public async Task<ActionResult<ValidateGiftPackageStockResponse>> ValidateGiftPackageStock(
        [FromBody] ValidateGiftPackageStockRequest request,
        CancellationToken cancellationToken)
    {
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
        // Set the current user ID
        var currentUser = _currentUserService.GetCurrentUser();
        request.UserId = Guid.Parse(currentUser.Id);

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