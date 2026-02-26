using Anela.Heblo.Application.Features.Catalog.Contracts;
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
}
