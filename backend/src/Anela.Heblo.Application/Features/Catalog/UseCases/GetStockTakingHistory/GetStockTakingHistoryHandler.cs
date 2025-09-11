using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using AutoMapper;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingHistory;

public class GetStockTakingHistoryHandler : IRequestHandler<GetStockTakingHistoryRequest, GetStockTakingHistoryResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IMapper _mapper;

    public GetStockTakingHistoryHandler(ICatalogRepository catalogRepository, IMapper mapper)
    {
        _catalogRepository = catalogRepository;
        _mapper = mapper;
    }

    public async Task<GetStockTakingHistoryResponse> Handle(GetStockTakingHistoryRequest request, CancellationToken cancellationToken)
    {
        // Get product by ProductCode
        var product = await _catalogRepository.GetByIdAsync(request.ProductCode, cancellationToken);
        
        // Check if product exists
        if (product == null)
        {
            return new GetStockTakingHistoryResponse(ErrorCodes.ProductNotFound, 
                new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
        }
        
        // Get stock taking history for the product
        var query = product.StockTakingHistory.AsQueryable();

        query = request.SortBy?.ToLower() switch
        {
            "date" => request.SortDescending ? query.OrderByDescending(x => x.Date) : query.OrderBy(x => x.Date),
            "code" => request.SortDescending ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code),
            "type" => request.SortDescending ? query.OrderByDescending(x => x.Type) : query.OrderBy(x => x.Type),
            "amountnew" => request.SortDescending ? query.OrderByDescending(x => x.AmountNew) : query.OrderBy(x => x.AmountNew),
            "amountold" => request.SortDescending ? query.OrderByDescending(x => x.AmountOld) : query.OrderBy(x => x.AmountOld),
            "user" => request.SortDescending ? query.OrderByDescending(x => x.User) : query.OrderBy(x => x.User),
            _ => query.OrderByDescending(x => x.Date) // Default sort by Date descending (newest first)
        };

        // Count total items
        var totalCount = query.Count();

        // Apply paging
        var pagedItems = query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        // Map to DTOs using AutoMapper
        var items = _mapper.Map<List<StockTakingHistoryItemDto>>(pagedItems);

        return new GetStockTakingHistoryResponse
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}