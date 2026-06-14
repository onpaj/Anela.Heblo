using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureStockTakingHistory;

public class GetManufactureStockTakingHistoryHandler
    : IRequestHandler<GetManufactureStockTakingHistoryRequest, GetManufactureStockTakingHistoryResponse>
{
    private readonly IManufactureCatalogSource _catalogSource;

    public GetManufactureStockTakingHistoryHandler(IManufactureCatalogSource catalogSource)
    {
        _catalogSource = catalogSource;
    }

    public async Task<GetManufactureStockTakingHistoryResponse> Handle(
        GetManufactureStockTakingHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var product = await _catalogSource.GetByIdAsync(request.ProductCode, cancellationToken);
        if (product is null)
        {
            return new GetManufactureStockTakingHistoryResponse(
                ErrorCodes.ProductNotFound,
                new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
        }

        IEnumerable<StockTakingRecord> query = product.StockTakingHistory;
        query = request.SortBy?.ToLower() switch
        {
            "code" => request.SortDescending ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code),
            "type" => request.SortDescending ? query.OrderByDescending(x => x.Type) : query.OrderBy(x => x.Type),
            "amountnew" => request.SortDescending ? query.OrderByDescending(x => x.AmountNew) : query.OrderBy(x => x.AmountNew),
            "amountold" => request.SortDescending ? query.OrderByDescending(x => x.AmountOld) : query.OrderBy(x => x.AmountOld),
            "user" => request.SortDescending ? query.OrderByDescending(x => x.User) : query.OrderBy(x => x.User),
            _ => request.SortDescending ? query.OrderByDescending(x => x.Date) : query.OrderBy(x => x.Date),
        };

        var materialised = query.ToList();
        var pagedItems = materialised
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new ManufactureStockTakingHistoryItemDto
            {
                Id = r.Id,
                Type = r.Type,
                Code = r.Code,
                AmountNew = r.AmountNew,
                AmountOld = r.AmountOld,
                Date = r.Date,
                User = r.User,
                Error = r.Error,
            })
            .ToList();

        return new GetManufactureStockTakingHistoryResponse
        {
            Items = pagedItems,
            TotalCount = materialised.Count,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
        };
    }
}
