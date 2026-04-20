using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetMaterialForPurchase;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetWarehouseStatistics;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// MCP tools for Catalog operations.
/// Thin wrappers around MediatR handlers that expose catalog functionality to MCP clients.
/// </summary>
[McpServerToolType]
public class CatalogMcpTools
{
    private readonly IMediator _mediator;

    public CatalogMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpServerTool]
    public async Task<string> GetCatalogList(
        [Description("Search term to filter by product name or code")]
        string? searchTerm = null,
        [Description("Filter by product types (Product, Material, SemiProduct)")]
        ProductType[]? productTypes = null,
        [Description("Page number for pagination (default: 1)")]
        int pageNumber = 1,
        [Description("Page size for pagination (default: 50, max: 100)")]
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var request = new GetCatalogListRequest
        {
            SearchTerm = searchTerm,
            ProductTypes = productTypes,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        var response = await _mediator.Send(request, cancellationToken);
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    public async Task<string> GetCatalogDetail(
        [Description("Product code (e.g., 'AKL001', 'SLU000001')")]
        string productCode,
        [Description("Number of months to look back for transaction history (default: 13)")]
        int monthsBack = 13,
        CancellationToken cancellationToken = default)
    {
        var request = new GetCatalogDetailRequest
        {
            ProductCode = productCode,
            MonthsBack = monthsBack
        };

        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    public async Task<string> GetProductComposition(
        [Description("Product code (e.g., 'AKL001')")]
        string productCode,
        CancellationToken cancellationToken = default)
    {
        var request = new GetProductCompositionRequest { ProductCode = productCode };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    public async Task<string> GetMaterialsForPurchase(CancellationToken cancellationToken = default)
    {
        var request = new GetMaterialsForPurchaseRequest();
        var response = await _mediator.Send(request, cancellationToken);
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    public async Task<string> GetAutocomplete(
        [Description("Search term to match against product name or code")]
        string? searchTerm = null,
        [Description("Maximum number of results to return (default: 20)")]
        int limit = 20,
        [Description("Filter by product types")]
        ProductType[]? productTypes = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GetCatalogListRequest
        {
            SearchTerm = searchTerm,
            PageSize = limit,
            PageNumber = 1,
            ProductTypes = productTypes
        };

        var response = await _mediator.Send(request, cancellationToken);
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    public async Task<string> GetProductUsage(
        [Description("Product code (e.g., 'AKL001')")]
        string productCode,
        CancellationToken cancellationToken = default)
    {
        var request = new GetProductUsageRequest { ProductCode = productCode };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            throw new McpException($"[{response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR"}] {response.FullError()}");
        }

        return JsonSerializer.Serialize(response);
    }

    [McpServerTool]
    public async Task<string> GetWarehouseStatistics(CancellationToken cancellationToken = default)
    {
        var request = new GetWarehouseStatisticsRequest();
        var response = await _mediator.Send(request, cancellationToken);
        return JsonSerializer.Serialize(response);
    }
}
