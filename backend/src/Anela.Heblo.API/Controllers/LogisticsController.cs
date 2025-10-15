using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.EnqueueGiftPackageManufacture;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetAvailableGiftPackages;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageDetail;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageManufactureJobStatus;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetManufactureLog;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetRunningJobsForGiftPackage;
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
        // Set the current user ID
        var currentUser = _currentUserService.GetCurrentUser();

        // Try to parse the user ID as GUID, fallback to a default GUID if parsing fails
        if (!Guid.TryParse(currentUser.Id, out var userId))
        {
            // If ID is not a valid GUID (e.g., in mock auth scenarios), generate a consistent one
            // or use a default system user GUID
            userId = Guid.Parse("00000000-0000-0000-0000-000000000001"); // System/Mock user GUID
        }

        request.UserId = userId;

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
    /// Get status of a gift package manufacturing job
    /// </summary>
    [HttpGet("gift-packages/manufacture/job-status/{jobId}")]
    public async Task<ActionResult<GetGiftPackageManufactureJobStatusResponse>> GetGiftPackageManufactureJobStatus(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        var request = new GetGiftPackageManufactureJobStatusRequest { JobId = jobId };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// Get running manufacturing jobs for a specific gift package
    /// </summary>
    [HttpGet("gift-packages/{giftPackageCode}/running-jobs")]
    public async Task<ActionResult<GetRunningJobsForGiftPackageResponse>> GetRunningJobsForGiftPackage(
        string giftPackageCode,
        CancellationToken cancellationToken = default)
    {
        var request = new GetRunningJobsForGiftPackageRequest { GiftPackageCode = giftPackageCode };
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