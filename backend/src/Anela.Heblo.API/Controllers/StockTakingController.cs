using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingHistory;
using Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Manufacture_MaterialInventory)]
[ApiController]
[Route("api/[controller]")]
public class StockTakingController : BaseApiController
{
    private readonly IMediator _mediator;

    public StockTakingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Submit stock taking for a specific product
    /// </summary>
    /// <param name="request">Stock taking submission request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stock taking record</returns>
    [HttpPost("submit")]
    [FeatureAuthorize(Feature.Manufacture_MaterialInventory, AccessLevel.Write)]
    public async Task<ActionResult<SubmitStockTakingResponse>> SubmitStockTaking(
        [FromBody] SubmitStockTakingRequest request,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Received stock taking submission request for product code {ProductCode}", request?.ProductCode);

        var response = await _mediator.Send(request!, cancellationToken);
        return HandleResponse(response);
    }

    /// <summary>
    /// Get stock taking history with pagination
    /// </summary>
    /// <param name="request">Stock taking history request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated stock taking history</returns>
    [HttpGet("history")]
    public async Task<ActionResult<GetStockTakingHistoryResponse>> GetStockTakingHistory(
        [FromQuery] GetStockTakingHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Received stock taking history request for product code {ProductCode}, page {PageNumber}",
            request?.ProductCode, request?.PageNumber);

        var response = await _mediator.Send(request!, cancellationToken);
        return HandleResponse(response);
    }
}