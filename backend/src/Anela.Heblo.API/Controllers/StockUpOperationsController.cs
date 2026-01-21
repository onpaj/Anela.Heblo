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
    /// Get stock-up operations with advanced filtering and sorting
    /// </summary>
    /// <param name="state">Optional filter by operation state (supports "Active" for Pending+Submitted+Failed)</param>
    /// <param name="pageSize">Page size (default: 50)</param>
    /// <param name="page">Page number (default: 1)</param>
    /// <param name="sourceType">Optional filter by source type (TransportBox or GiftPackageManufacture)</param>
    /// <param name="sourceId">Optional filter by source ID</param>
    /// <param name="productCode">Optional filter by product code (exact match)</param>
    /// <param name="documentNumber">Optional filter by document number (partial match)</param>
    /// <param name="createdFrom">Optional filter by creation date (from)</param>
    /// <param name="createdTo">Optional filter by creation date (to)</param>
    /// <param name="sortBy">Sort column (id, createdAt, state, documentNumber, productCode)</param>
    /// <param name="sortDescending">Sort direction (default: true)</param>
    [HttpGet]
    public async Task<ActionResult<GetStockUpOperationsResponse>> GetOperations(
        [FromQuery] string? state = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] int? page = null,
        [FromQuery] StockUpSourceType? sourceType = null,
        [FromQuery] int? sourceId = null,
        [FromQuery] string? productCode = null,
        [FromQuery] string? documentNumber = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = true)
    {
        var request = new GetStockUpOperationsRequest
        {
            State = state,
            PageSize = pageSize,
            Page = page,
            SourceType = sourceType,
            SourceId = sourceId,
            ProductCode = productCode,
            DocumentNumber = documentNumber,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            SortBy = sortBy,
            SortDescending = sortDescending
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
