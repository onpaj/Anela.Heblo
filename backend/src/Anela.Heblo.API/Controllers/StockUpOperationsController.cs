using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperations;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;
using Anela.Heblo.Application.Features.Catalog.UseCases.RetryStockUpOperation;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class StockUpOperationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public StockUpOperationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get stock-up operations with optional filtering by state
    /// </summary>
    /// <param name="state">Optional filter by operation state (e.g., Failed)</param>
    /// <param name="pageSize">Page size (default: 50)</param>
    /// <param name="page">Page number (default: 1)</param>
    [HttpGet]
    public async Task<ActionResult<GetStockUpOperationsResponse>> GetOperations(
        [FromQuery] StockUpOperationState? state = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] int? page = null)
    {
        var request = new GetStockUpOperationsRequest
        {
            State = state,
            PageSize = pageSize,
            Page = page
        };

        var response = await _mediator.Send(request);
        return Ok(response);
    }

    /// <summary>
    /// Retry a failed stock-up operation
    /// </summary>
    /// <param name="id">Operation ID to retry</param>
    [HttpPost("{id}/retry")]
    public async Task<ActionResult<RetryStockUpOperationResponse>> RetryOperation(int id)
    {
        var request = new RetryStockUpOperationRequest { OperationId = id };
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Get summary counts of stock-up operations by state
    /// </summary>
    /// <param name="sourceType">Optional filter by source type (GiftPackageManufacture or TransportBox)</param>
    [HttpGet("summary")]
    public async Task<ActionResult<GetStockUpOperationsSummaryResponse>> GetSummary(
        [FromQuery] StockUpSourceType? sourceType = null)
    {
        var request = new GetStockUpOperationsSummaryRequest
        {
            SourceType = sourceType
        };

        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}
