using Anela.Heblo.Application.Features.Catalog.Infrastructure.Exceptions;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductMargins;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.API.Infrastructure;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProductMarginsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<ProductMarginsController> _logger;

    public ProductMarginsController(IMediator mediator, ILogger<ProductMarginsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<GetProductMarginsResponse>> GetProductMargins([FromQuery] GetProductMarginsRequest request)
    {
        _logger.LogInformation("Getting product margins with page {PageNumber}, size {PageSize}, product code {ProductCode}, product name {ProductName}, sort by {SortBy}, descending {SortDescending}",
            request.PageNumber, request.PageSize, request.ProductCode, request.ProductName, request.SortBy, request.SortDescending);

        try
        {
            var response = await _mediator.Send(request);

            if (!response.Success)
            {
                switch (response.ErrorCode)
                {
                    case ErrorCodes.MarginCalculationError:
                        _logger.LogError("Margin calculation error: {Details}", response.Params?["details"]);
                        return BadRequest(ErrorResponseHelper.CreateBusinessError<GetProductMarginsResponse>(
                            ErrorCodes.MarginCalculationError, response.Params?["details"] ?? "Margin calculation failed"));
                    case ErrorCodes.DataAccessUnavailable:
                        _logger.LogError("Data access error in product margins: {Details}", response.Params?["details"]);
                        return StatusCode(503, ErrorResponseHelper.CreateBusinessError<GetProductMarginsResponse>(
                            ErrorCodes.DataAccessUnavailable, "Data source unavailable"));
                    default:
                        _logger.LogWarning("Failed to get product margins: {ErrorCode}", response.ErrorCode);
                        return BadRequest(response);
                }
            }

            _logger.LogInformation("Successfully retrieved product margins with {Count} items, total {TotalCount}",
                response.Items.Count, response.TotalCount);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in product margins endpoint");
            return StatusCode(500, ErrorResponseHelper.CreateBusinessError<GetProductMarginsResponse>(
                ErrorCodes.InternalServerError, "An unexpected error occurred"));
        }
    }
}