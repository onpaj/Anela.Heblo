using MediatR;
using Anela.Heblo.Application.features.catalog.contracts;
using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Xcc.Persistance;
using System.Linq.Expressions;

namespace Anela.Heblo.Application.features.catalog.Application;

public class GetCatalogListHandler : IRequestHandler<GetCatalogListRequest, GetCatalogListResponse>
{
    private readonly ICatalogRepository _catalogRepository;

    public GetCatalogListHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task<GetCatalogListResponse> Handle(GetCatalogListRequest request, CancellationToken cancellationToken)
    {
        // Build filter expression
        Expression<Func<CatalogAggregate, bool>> filter = x => true;

        if (request.Type.HasValue)
        {
            var typeValue = request.Type.Value;
            filter = x => x.Type == typeValue;
        }

        // Get all filtered items (repository doesn't support paging directly)
        var allItems = await _catalogRepository.FindAsync(filter, cancellationToken);

        // Apply sorting
        var query = allItems.AsQueryable();

        query = request.SortBy?.ToLower() switch
        {
            "productcode" => request.SortDescending ? query.OrderByDescending(x => x.ProductCode) : query.OrderBy(x => x.ProductCode),
            "productname" => request.SortDescending ? query.OrderByDescending(x => x.ProductName) : query.OrderBy(x => x.ProductName),
            "type" => request.SortDescending ? query.OrderByDescending(x => x.Type) : query.OrderBy(x => x.Type),
            "location" => request.SortDescending ? query.OrderByDescending(x => x.Location) : query.OrderBy(x => x.Location),
            _ => query.OrderBy(x => x.ProductCode) // Default sort by ProductCode
        };

        // Count total items
        var totalCount = query.Count();

        // Apply paging
        var items = query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(MapToDto)
            .ToList();

        return new GetCatalogListResponse
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }

    private static CatalogItemDto MapToDto(CatalogAggregate entity)
    {
        return new CatalogItemDto
        {
            ProductCode = entity.ProductCode,
            ProductName = entity.ProductName,
            Type = entity.Type,
            Stock = new StockDto
            {
                Eshop = entity.Stock.Eshop,
                Erp = entity.Stock.Erp,
                Transport = entity.Stock.Transport,
                Reserve = entity.Stock.Reserve,
                Available = entity.Stock.Available
            },
            Properties = new PropertiesDto
            {
                OptimalStockDaysSetup = entity.Properties.OptimalStockDaysSetup,
                StockMinSetup = entity.Properties.StockMinSetup,
                BatchSize = entity.Properties.BatchSize,
                SeasonMonths = entity.Properties.SeasonMonths
            },
            Location = entity.Location,
            MinimalOrderQuantity = entity.MinimalOrderQuantity,
            MinimalManufactureQuantity = entity.MinimalManufactureQuantity
        };
    }
}