using Microsoft.AspNetCore.Mvc;
using MediatR;
using Anela.Heblo.Application.Features.Catalog.Model;
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
    public async Task<ActionResult<GetProductMarginsResponse>> GetProductMargins([FromQuery] GetProductMarginsRequest request)
    {
        _logger.LogInformation("Getting product margins with page {PageNumber}, size {PageSize}, product code {ProductCode}, product name {ProductName}",
            request.PageNumber, request.PageSize, request.ProductCode, request.ProductName);

        try
        {
            var response = await _mediator.Send(request);
            _logger.LogInformation("Successfully retrieved product margins with {Count} items, total {TotalCount}",
                response.Items.Count, response.TotalCount);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get product margins");
            throw;
        }
    }
}