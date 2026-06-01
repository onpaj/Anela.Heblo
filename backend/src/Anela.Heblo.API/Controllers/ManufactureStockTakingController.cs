using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingHistory;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ManufactureStockTakingController : BaseApiController
{
    private readonly IMediator _mediator;

    public ManufactureStockTakingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Submit manufacture stock taking for a specific material
    /// </summary>
    /// <param name="request">Manufacture stock taking submission request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stock taking record</returns>
    [HttpPost("submit")]
    public async Task<ActionResult<SubmitManufactureStockTakingResponse>> SubmitManufactureStockTaking(
        [FromBody] SubmitManufactureStockTakingRequest request,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Received manufacture stock taking submission request for product code {ProductCode}", request?.ProductCode);

        var response = await _mediator.Send(request!, cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// Get manufacture stock taking history with pagination
    /// </summary>
    /// <param name="request">Stock taking history request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated stock taking history</returns>
    [HttpGet("history")]
    public async Task<ActionResult<GetStockTakingHistoryResponse>> GetManufactureStockTakingHistory(
        [FromQuery] GetStockTakingHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Received manufacture stock taking history request for product code {ProductCode}, page {PageNumber}",
            request?.ProductCode, request?.PageNumber);

        var response = await _mediator.Send(request!, cancellationToken);
        return HandleResponse(response);
    }
}