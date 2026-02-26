using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetMaterialForPurchase;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetWarehouseStatistics;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for Catalog operations.
/// Thin wrappers around MediatR handlers that expose catalog functionality to MCP clients.
/// </summary>
public class CatalogMcpTools
{
    private readonly IMediator _mediator;

    public CatalogMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    // TODO: Add [McpTool] attribute when Microsoft.Extensions.AI API is finalized
    // [McpTool(
    //     Name = "catalog_get_list",
    //     Description = "Get paginated list of catalog items with optional filtering by product type, search term, or warehouse status. Returns products, materials, and semi-products from the Heblo system."
    // )]
    public async Task<GetCatalogListResponse> GetCatalogList(
        // TODO: Add [McpToolParameter] attributes when API is finalized
        // [McpToolParameter(Description = "Search term to filter by product name or code")]
        string? searchTerm = null,

        // [McpToolParameter(Description = "Filter by product types (Product, Material, SemiProduct)")]
        ProductType[]? productTypes = null,

        // [McpToolParameter(Description = "Page number for pagination (default: 1)")]
        int pageNumber = 1,

        // [McpToolParameter(Description = "Page size for pagination (default: 50, max: 100)")]
        int pageSize = 50
    )
    {
        var request = new GetCatalogListRequest
        {
            SearchTerm = searchTerm,
            ProductTypes = productTypes,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        return await _mediator.Send(request);
    }

    // TODO: Add [McpTool] attribute when Microsoft.Extensions.AI API is finalized
    // [McpTool(
    //     Name = "catalog_get_detail",
    //     Description = "Get detailed information for a specific product including stock levels, recent transactions, and pricing history. Use this to analyze individual product performance."
    // )]
    public async Task<GetCatalogDetailResponse> GetCatalogDetail(
        // TODO: Add [McpToolParameter] attributes when API is finalized
        // [McpToolParameter(Description = "Product code (e.g., 'AKL001', 'SLU000001')", Required = true)]
        string productCode,

        // [McpToolParameter(Description = "Number of months to look back for transaction history (default: 13)")]
        int monthsBack = 13
    )
    {
        var request = new GetCatalogDetailRequest
        {
            ProductCode = productCode,
            MonthsBack = monthsBack
        };

        var response = await _mediator.Send(request);

        // Handle response envelope (Success/Error pattern)
        if (!response.Success)
        {
            throw new McpToolException(
                response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
                response.FullError()
            );
        }

        return response;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "catalog_get_composition",
    //     Description = "Get the composition/recipe of a product, showing all ingredients and their quantities. Use this to understand what materials are needed to manufacture a product."
    // )]
    public async Task<GetProductCompositionResponse> GetProductComposition(
        // [McpToolParameter(Description = "Product code (e.g., 'AKL001')", Required = true)]
        string productCode
    )
    {
        var request = new GetProductCompositionRequest { ProductCode = productCode };
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(
                response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
                response.FullError()
            );
        }

        return response;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "catalog_get_materials_for_purchase",
    //     Description = "Get list of materials that need to be purchased based on current stock levels and planned production. Use this for procurement planning."
    // )]
    public async Task<GetMaterialsForPurchaseResponse> GetMaterialsForPurchase()
    {
        var request = new GetMaterialsForPurchaseRequest();
        return await _mediator.Send(request);
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "catalog_get_autocomplete",
    //     Description = "Search for products by name or code with autocomplete functionality. Returns a limited set of matching products for quick lookup."
    // )]
    public async Task<GetCatalogListResponse> GetAutocomplete(
        // [McpToolParameter(Description = "Search term to match against product name or code")]
        string? searchTerm = null,

        // [McpToolParameter(Description = "Maximum number of results to return (default: 20)")]
        int limit = 20,

        // [McpToolParameter(Description = "Filter by product types")]
        ProductType[]? productTypes = null
    )
    {
        var request = new GetCatalogListRequest
        {
            SearchTerm = searchTerm,
            PageSize = limit,
            PageNumber = 1,
            ProductTypes = productTypes
        };

        return await _mediator.Send(request);
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "catalog_get_usage",
    //     Description = "Get information about where a product is used (which products include it as an ingredient). Use this for impact analysis when considering changes to a material or semi-product."
    // )]
    public async Task<GetProductUsageResponse> GetProductUsage(
        // [McpToolParameter(Description = "Product code (e.g., 'AKL001')", Required = true)]
        string productCode
    )
    {
        var request = new GetProductUsageRequest { ProductCode = productCode };
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            throw new McpToolException(
                response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR",
                response.FullError()
            );
        }

        return response;
    }

    // TODO: Add [McpTool] attribute
    // [McpTool(
    //     Name = "catalog_get_warehouse_statistics",
    //     Description = "Get warehouse statistics including total items, stock levels, and value metrics. Use this for high-level inventory analysis."
    // )]
    public async Task<GetWarehouseStatisticsResponse> GetWarehouseStatistics()
    {
        var request = new GetWarehouseStatisticsRequest();
        return await _mediator.Send(request);
    }
}
