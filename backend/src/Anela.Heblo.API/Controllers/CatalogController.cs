using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty;
using Anela.Heblo.Application.Features.Catalog.UseCases.DeleteManufactureDifficulty;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetManufactureDifficultySettings;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetMaterialForPurchase;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;
using Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;
using Anela.Heblo.Application.Features.Catalog.UseCases.UpdateManufactureDifficulty;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.AspNetCore.Authorization;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.API.Infrastructure;

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

            if (!response.Success)
            {
                switch (response.ErrorCode)
                {
                    case ErrorCodes.ProductNotFound:
                        _logger.LogWarning("Product {ProductCode} not found", productCode);
                        return NotFound(ErrorResponseHelper.CreateNotFoundError<GetCatalogDetailResponse>(
                            ErrorCodes.ProductNotFound, productCode));
                    default:
                        _logger.LogWarning("Failed to get catalog detail for product {ProductCode}: {ErrorCode}",
                            productCode, response.ErrorCode);
                        return BadRequest(response);
                }
            }

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

    // Manufacture Difficulty Management Endpoints

    [HttpGet("{productCode}/manufacture-difficulty")]
    public async Task<ActionResult<GetManufactureDifficultySettingsResponse>> GetManufactureDifficultyHistory(
        string productCode,
        [FromQuery] DateTime? asOfDate = null)
    {
        _logger.LogInformation("Getting manufacture difficulty history for product {ProductCode} as of {AsOfDate}",
            productCode, asOfDate);

        try
        {
            var request = new GetManufactureDifficultySettingsRequest
            {
                ProductCode = productCode,
                AsOfDate = asOfDate
            };

            var response = await _mediator.Send(request);
            _logger.LogInformation("Successfully retrieved manufacture difficulty history for product {ProductCode}",
                productCode);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get manufacture difficulty history for product {ProductCode}", productCode);
            throw;
        }
    }

    [HttpPost("manufacture-difficulty")]
    [ProducesResponseType(typeof(CreateManufactureDifficultyResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<CreateManufactureDifficultyResponse>> CreateManufactureDifficulty(
        CreateManufactureDifficultyRequest request)
    {
        _logger.LogInformation("Creating manufacture difficulty for product {ProductCode} with value {DifficultyValue}",
            request.ProductCode, request.DifficultyValue);

        try
        {
            var response = await _mediator.Send(request);
            _logger.LogInformation("Successfully created manufacture difficulty {Id} for product {ProductCode}",
                response.DifficultyHistory.Id, response.DifficultyHistory.ProductCode);
            return CreatedAtAction(
                nameof(GetManufactureDifficultyHistory),
                new { productCode = response.DifficultyHistory.ProductCode },
                response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid request for creating manufacture difficulty");
            return BadRequest(ErrorResponseHelper.CreateBusinessError<CreateManufactureDifficultyResponse>(ErrorCodes.InvalidValue, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Business rule violation when creating manufacture difficulty");
            return Conflict(ErrorResponseHelper.CreateBusinessError<CreateManufactureDifficultyResponse>(ErrorCodes.ManufactureDifficultyConflict, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create manufacture difficulty");
            throw;
        }
    }

    [HttpPut("manufacture-difficulty/{id}")]
    public async Task<ActionResult<UpdateManufactureDifficultyResponse>> UpdateManufactureDifficulty(
        int id,
        UpdateManufactureDifficultyRequest request)
    {
        if (id != request.Id)
        {
            return BadRequest(ErrorResponseHelper.CreateErrorResponse<UpdateManufactureDifficultyResponse>(
                ErrorCodes.ValidationError,
                new Dictionary<string, string> { { "detail", "URL parameter ID must match request body ID" } }));
        }

        _logger.LogInformation("Updating manufacture difficulty {Id} with value {DifficultyValue}",
            request.Id, request.DifficultyValue);

        try
        {
            var response = await _mediator.Send(request);
            _logger.LogInformation("Successfully updated manufacture difficulty {Id}",
                response.DifficultyHistory.Id);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid request for updating manufacture difficulty");
            return BadRequest(ErrorResponseHelper.CreateBusinessError<UpdateManufactureDifficultyResponse>(ErrorCodes.InvalidValue, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Business rule violation when updating manufacture difficulty");
            return Conflict(ErrorResponseHelper.CreateBusinessError<UpdateManufactureDifficultyResponse>(ErrorCodes.ManufactureDifficultyConflict, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update manufacture difficulty {Id}", id);
            throw;
        }
    }

    [HttpDelete("manufacture-difficulty/{id}")]
    public async Task<ActionResult<DeleteManufactureDifficultyResponse>> DeleteManufactureDifficulty(int id)
    {
        _logger.LogInformation("Deleting manufacture difficulty {Id}", id);

        try
        {
            var request = new DeleteManufactureDifficultyRequest { Id = id };
            var response = await _mediator.Send(request);

            if (response.Success)
            {
                _logger.LogInformation("Successfully deleted manufacture difficulty {Id}", id);
                return Ok(response);
            }
            else
            {
                _logger.LogWarning("Failed to delete manufacture difficulty {Id}: {Message}", id, response.Message);
                return NotFound(ErrorResponseHelper.CreateNotFoundError<DeleteManufactureDifficultyResponse>(
                    ErrorCodes.ManufactureDifficultyNotFound, id.ToString()));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete manufacture difficulty {Id}", id);
            throw;
        }
    }

    [HttpGet("{productCode}/usage")]
    public async Task<ActionResult<GetProductUsageResponse>> GetProductUsage(string productCode)
    {
        _logger.LogInformation("Getting product usage for product code {ProductCode}", productCode);

        try
        {
            var request = new GetProductUsageRequest { ProductCode = productCode };
            var response = await _mediator.Send(request);
            _logger.LogInformation("Successfully retrieved product usage for product {ProductCode} - found {Count} products using it",
                productCode, response.ManufactureTemplates.Count);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get product usage for product {ProductCode}", productCode);
            throw;
        }
    }

}