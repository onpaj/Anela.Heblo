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
public class CatalogController : BaseApiController
{
    private readonly IMediator _mediator;

    public CatalogController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetCatalogListResponse>> GetCatalogList([FromQuery] GetCatalogListRequest request)
    {
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("{productCode}")]
    public async Task<ActionResult<GetCatalogDetailResponse>> GetCatalogDetail(string productCode, [FromQuery] int monthsBack = 13)
    {
        var request = new GetCatalogDetailRequest { ProductCode = productCode, MonthsBack = monthsBack };
        var response = await _mediator.Send(request);
        return HandleResponse(response);
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
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("autocomplete")]
    public async Task<ActionResult<GetCatalogListResponse>> GetProductsForAutocomplete(
        [FromQuery] string? searchTerm,
        [FromQuery] int limit = 20,
        [FromQuery] ProductType[]? productTypes = null)
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

        return Ok(response);
    }

    // Manufacture Difficulty Management Endpoints

    [HttpGet("{productCode}/manufacture-difficulty")]
    public async Task<ActionResult<GetManufactureDifficultySettingsResponse>> GetManufactureDifficultyHistory(
        string productCode,
        [FromQuery] DateTime? asOfDate = null)
    {
        var request = new GetManufactureDifficultySettingsRequest
        {
            ProductCode = productCode,
            AsOfDate = asOfDate
        };

        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpPost("manufacture-difficulty")]
    [ProducesResponseType(typeof(CreateManufactureDifficultyResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<CreateManufactureDifficultyResponse>> CreateManufactureDifficulty(
        CreateManufactureDifficultyRequest request)
    {
        var response = await _mediator.Send(request);
        
        if (response.Success)
        {
            return CreatedAtAction(
                nameof(GetManufactureDifficultyHistory),
                new { productCode = response.DifficultyHistory.ProductCode },
                response);
        }
        
        return HandleResponse(response);
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

        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    [HttpDelete("manufacture-difficulty/{id}")]
    public async Task<ActionResult<DeleteManufactureDifficultyResponse>> DeleteManufactureDifficulty(int id)
    {
        var request = new DeleteManufactureDifficultyRequest { Id = id };
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    [HttpGet("{productCode}/usage")]
    public async Task<ActionResult<GetProductUsageResponse>> GetProductUsage(string productCode)
    {
        var request = new GetProductUsageRequest { ProductCode = productCode };
        var response = await _mediator.Send(request);
        return Ok(response);
    }

}