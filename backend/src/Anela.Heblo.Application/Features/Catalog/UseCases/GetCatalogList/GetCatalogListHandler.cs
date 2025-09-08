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

        if (request.Type.HasValue)
        {
            var typeValue = request.Type.Value;
            filter = filter.And(x => x.Type == typeValue);
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
            "reserve" => request.SortDescending ? query.OrderByDescending(x => x.Stock.Reserve) : query.OrderBy(x => x.Stock.Reserve),
            "erp" => request.SortDescending ? query.OrderByDescending(x => x.Stock.Erp) : query.OrderBy(x => x.Stock.Erp),
            "eshop" => request.SortDescending ? query.OrderByDescending(x => x.Stock.Eshop) : query.OrderBy(x => x.Stock.Eshop),
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
}