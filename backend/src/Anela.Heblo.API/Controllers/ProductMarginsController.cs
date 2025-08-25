using Microsoft.AspNetCore.Mvc;
using MediatR;
using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Application.Features.Catalog.Exceptions;
using Microsoft.AspNetCore.Authorization;

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
    [Authorize(Policy = "ViewProductMargins")]
    public async Task<ActionResult<GetProductMarginsResponse>> GetProductMargins([FromQuery] GetProductMarginsRequest request)
    {
        _logger.LogInformation("Getting product margins with page {PageNumber}, size {PageSize}, product code {ProductCode}, product name {ProductName}, sort by {SortBy}, descending {SortDescending}",
            request.PageNumber, request.PageSize, request.ProductCode, request.ProductName, request.SortBy, request.SortDescending);

        try
        {
            var response = await _mediator.Send(request);
            _logger.LogInformation("Successfully retrieved product margins with {Count} items, total {TotalCount}",
                response.Items.Count, response.TotalCount);
            return Ok(response);
        }
        catch (DataAccessException ex)
        {
            _logger.LogError(ex, "Data access error in product margins");
            return StatusCode(503, new { error = "Service temporarily unavailable", details = "Data source unavailable" });
        }
        catch (MarginCalculationException ex)
        {
            _logger.LogError(ex, "Margin calculation error");
            return BadRequest(new { error = "Unable to calculate product margins", details = ex.Message });
        }
        catch (ProductMarginsException ex)
        {
            _logger.LogError(ex, "Product margins business logic error");
            return BadRequest(new { error = "Unable to process margin request", details = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            var currentUserId = User?.FindFirst("sub")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Unknown";
            var currentUserName = User?.Identity?.Name ?? "Unknown User";

            _logger.LogWarning(ex, "Unauthorized access to product margins by user {UserId} ({UserName})",
                currentUserId, currentUserName);

            return StatusCode(403, new { error = "Insufficient permissions to access margin data" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in product margins endpoint");
            return StatusCode(500, new { error = "Internal server error", details = "An unexpected error occurred" });
        }
    }
}