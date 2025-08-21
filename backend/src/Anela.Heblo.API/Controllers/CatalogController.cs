using Microsoft.AspNetCore.Mvc;
using MediatR;
using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Domain.Features.Catalog;
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
    public async Task<ActionResult<GetCatalogDetailResponse>> GetCatalogDetail(string productCode, [FromQuery] int monthsBack = 13)
    {
        _logger.LogInformation("Getting catalog detail for product code {ProductCode} with {MonthsBack} months back", productCode, monthsBack);

        try
        {
            var request = new GetCatalogDetailRequest { ProductCode = productCode, MonthsBack = monthsBack };
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

    [HttpPost("refresh/manufacture-history")]
    public async Task<IActionResult> RefreshManufactureHistoryData()
    {
        await _mediator.Send(new RefreshManufactureHistoryDataRequest());
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

    [HttpPost("refresh/eshop-prices")]
    public async Task<IActionResult> RefreshEshopPricesData()
    {
        await _mediator.Send(new RefreshEshopPricesDataRequest());
        return NoContent();
    }

    [HttpPost("refresh/erp-prices")]
    public async Task<IActionResult> RefreshErpPricesData()
    {
        await _mediator.Send(new RefreshErpPricesDataRequest());
        return NoContent();
    }

    [HttpPost("refresh/manufacture-difficulty")]
    public async Task<IActionResult> RefreshManufactureDifficultyData()
    {
        await _mediator.Send(new RefreshManufactureDifficultyDataRequest());
        return NoContent();
    }

    [HttpGet("materials-for-purchase")]
    public async Task<ActionResult<GetMaterialsForPurchaseResponse>> GetMaterialsForPurchase([FromQuery] GetMaterialsForPurchaseRequest request)
    {
        _logger.LogInformation("Getting materials for purchase with search term '{SearchTerm}', limit {Limit}",
            request.SearchTerm, request.Limit);

        try
        {
            var response = await _mediator.Send(request);
            _logger.LogInformation("Successfully retrieved {Count} materials for purchase", response.Materials.Count);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get materials for purchase");
            throw;
        }
    }

    [HttpGet("autocomplete")]
    public async Task<ActionResult<GetCatalogListResponse>> GetProductsForAutocomplete(
        [FromQuery] string? searchTerm,
        [FromQuery] int limit = 20,
        [FromQuery] ProductType[]? productTypes = null)
    {
        _logger.LogInformation("Getting products for autocomplete with search term '{SearchTerm}', limit {Limit}, types {ProductTypes}",
            searchTerm, limit, productTypes != null ? string.Join(",", productTypes) : "all");

        try
        {
            var request = new GetCatalogListRequest
            {
                SearchTerm = searchTerm, // OR search in both ProductName and ProductCode
                PageSize = limit,
                PageNumber = 1
            };

            var response = await _mediator.Send(request);

            // Filter by product types if specified
            if (productTypes != null && productTypes.Length > 0)
            {
                response.Items = response.Items.Where(item => productTypes.Contains(item.Type)).ToList();
                response.TotalCount = response.Items.Count;
            }

            _logger.LogInformation("Successfully retrieved {Count} products for autocomplete", response.Items.Count);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get products for autocomplete");
            throw;
        }
    }

}