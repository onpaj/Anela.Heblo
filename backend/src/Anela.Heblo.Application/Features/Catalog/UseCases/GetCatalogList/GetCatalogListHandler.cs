using System.Linq.Expressions;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Xcc;
using AutoMapper;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogList;

public class GetCatalogListHandler : IRequestHandler<GetCatalogListRequest, GetCatalogListResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IMapper _mapper;

    public GetCatalogListHandler(ICatalogRepository catalogRepository, IMapper mapper)
    {
        _catalogRepository = catalogRepository;
        _mapper = mapper;
    }

    public async Task<GetCatalogListResponse> Handle(GetCatalogListRequest request, CancellationToken cancellationToken)
    {
        // Build filter expression
        Expression<Func<CatalogAggregate, bool>> filter = x => true;

        // Support for multiple product types (for batch planning)
        if (request.ProductTypes != null && request.ProductTypes.Length > 0)
        {
            filter = filter.And(x => request.ProductTypes.Contains(x.Type));
        }

        // Autocomplete search with OR logic (SearchTerm in ProductName OR ProductCode)
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var searchTerm = request.SearchTerm.Trim().ToLowerInvariant();
            filter = filter.And(x =>
                x.ProductName.ToLowerInvariant().Contains(searchTerm) ||
                x.ProductCode.ToLowerInvariant().Contains(searchTerm));
        }

        // Individual filters (for other use cases)
        if (!string.IsNullOrWhiteSpace(request.ProductName))
        {
            var productName = request.ProductName.Trim();
            filter = filter.And(x => x.ProductName.ToLowerInvariant().Contains(productName.ToLowerInvariant()));
        }

        if (!string.IsNullOrWhiteSpace(request.ProductCode))
        {
            var productCode = request.ProductCode.Trim();
            filter = filter.And(x => x.ProductCode.ToLowerInvariant().Contains(productCode.ToLowerInvariant()));
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
            "available" => request.SortDescending ? query.OrderByDescending(x => x.Stock.Available) : query.OrderBy(x => x.Stock.Available),
            "transport" => request.SortDescending ? query.OrderByDescending(x => x.Stock.Transport) : query.OrderBy(x => x.Stock.Transport),
            "reserve" => request.SortDescending ? query.OrderByDescending(x => x.Stock.Reserve) : query.OrderBy(x => x.Stock.Reserve),
            "erp" => request.SortDescending ? query.OrderByDescending(x => x.Stock.Erp) : query.OrderBy(x => x.Stock.Erp),
            "eshop" => request.SortDescending ? query.OrderByDescending(x => x.Stock.Eshop) : query.OrderBy(x => x.Stock.Eshop),
            "lastinventorydays" => ApplyLastInventoryDaysSorting(query, request.SortDescending),
            _ => query.OrderBy(x => x.ProductCode) // Default sort by ProductCode
        };

        // Count total items
        var totalCount = query.Count();

        // Apply paging
        var pagedItems = query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        // Map to DTOs using AutoMapper
        var items = _mapper.Map<List<CatalogItemDto>>(pagedItems);

        return new GetCatalogListResponse
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }

    private static IQueryable<CatalogAggregate> ApplyLastInventoryDaysSorting(IQueryable<CatalogAggregate> query, bool sortDescending)
    {
        // Sort by last inventory days with items WITHOUT inventory first
        // Items with no last stock taking should be first (sorted by location ascending)
        // Items with last stock taking should be sorted by days since last inventory

        if (sortDescending)
        {
            // Descending: Items WITHOUT inventory first, then items with inventory by oldest first (ascending = biggest days)
            return query
                .OrderBy(x => x.LastStockTaking.HasValue)         // Items WITHOUT inventory first (null values first)
                .ThenBy(x => x.Location)                          // Items without inventory sorted by location ascending
                .ThenBy(x => x.LastStockTaking);                  // Items with inventory: oldest first (ascending = biggest days)
        }
        else
        {
            // Ascending: Items with inventory by newest first (descending = smallest days), then items WITHOUT inventory
            return query
                .OrderBy(x => !x.LastStockTaking.HasValue)        // Items WITH inventory first
                .ThenByDescending(x => x.LastStockTaking)         // Items with inventory: newest first (descending = smallest days)
                .ThenBy(x => x.Location);                         // Items without inventory sorted by location ascending
        }
    }
}