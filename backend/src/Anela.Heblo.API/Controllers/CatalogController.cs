using Microsoft.AspNetCore.Mvc;
using MediatR;
using Anela.Heblo.Application.Features.Catalog.Model;
using Microsoft.AspNetCore.Authorization;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CatalogController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<CatalogController> _logger;

    public CatalogController(IMediator mediator, ILogger<CatalogController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<GetCatalogListResponse>> GetCatalogList([FromQuery] GetCatalogListRequest request)
    {
        _logger.LogInformation("Getting catalog list with page {PageNumber}, size {PageSize}, product name {ProductName}",
            request.PageNumber, request.PageSize, request.ProductName);

        try
        {
            var response = await _mediator.Send(request);
            _logger.LogInformation("Successfully retrieved catalog list with {Count} items, total {TotalCount}",
                response.Items.Count, response.TotalCount);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get catalog list");
            throw;
        }
    }

    [HttpGet("{productCode}")]
    public async Task<ActionResult<GetCatalogDetailResponse>> GetCatalogDetail(string productCode)
    {
        _logger.LogInformation("Getting catalog detail for product code {ProductCode}", productCode);

        try
        {
            var request = new GetCatalogDetailRequest { ProductCode = productCode };
            var response = await _mediator.Send(request);
            _logger.LogInformation("Successfully retrieved catalog detail for product {ProductCode}", productCode);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get catalog detail for product {ProductCode}", productCode);
            throw;
        }
    }

    [HttpPost("refresh/transport")]
    public async Task<IActionResult> RefreshTransportData()
    {
        await _mediator.Send(new RefreshTransportDataRequest());
        return NoContent();
    }

    [HttpPost("refresh/reserve")]
    public async Task<IActionResult> RefreshReserveData()
    {
        await _mediator.Send(new RefreshReserveDataRequest());
        return NoContent();
    }

    [HttpPost("refresh/sales")]
    public async Task<IActionResult> RefreshSalesData()
    {
        await _mediator.Send(new RefreshSalesDataRequest());
        return NoContent();
    }

    [HttpPost("refresh/attributes")]
    public async Task<IActionResult> RefreshAttributesData()
    {
        await _mediator.Send(new RefreshAttributesDataRequest());
        return NoContent();
    }

    [HttpPost("refresh/erp-stock")]
    public async Task<IActionResult> RefreshErpStockData()
    {
        await _mediator.Send(new RefreshErpStockDataRequest());
        return NoContent();
    }

    [HttpPost("refresh/eshop-stock")]
    public async Task<IActionResult> RefreshEshopStockData()
    {
        await _mediator.Send(new RefreshEshopStockDataRequest());
        return NoContent();
    }

    [HttpPost("refresh/purchase-history")]
    public async Task<IActionResult> RefreshPurchaseHistoryData()
    {
        await _mediator.Send(new RefreshPurchaseHistoryDataRequest());
        return NoContent();
    }

    [HttpPost("refresh/consumed-history")]
    public async Task<IActionResult> RefreshConsumedHistoryData()
    {
        await _mediator.Send(new RefreshConsumedHistoryDataRequest());
        return NoContent();
    }

    [HttpPost("refresh/stock-taking")]
    public async Task<IActionResult> RefreshStockTakingData()
    {
        await _mediator.Send(new RefreshStockTakingDataRequest());
        return NoContent();
    }

    [HttpPost("refresh/lots")]
    public async Task<IActionResult> RefreshLotsData()
    {
        await _mediator.Send(new RefreshLotsDataRequest());
        return NoContent();
    }

}