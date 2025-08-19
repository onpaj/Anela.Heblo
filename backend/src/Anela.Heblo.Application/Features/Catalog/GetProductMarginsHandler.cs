using MediatR;
using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog;

public class GetProductMarginsHandler : IRequestHandler<GetProductMarginsRequest, GetProductMarginsResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILedgerService _ledgerService;
    private readonly TimeProvider _timeProvider;

    public GetProductMarginsHandler(
        ICatalogRepository catalogRepository,
        ILedgerService ledgerService,
        TimeProvider timeProvider
        )
    {
        _catalogRepository = catalogRepository;
        _ledgerService = ledgerService;
        _timeProvider = timeProvider;
    }

    public async Task<GetProductMarginsResponse> Handle(GetProductMarginsRequest request, CancellationToken cancellationToken)
    {
        var allItems = await _catalogRepository.GetAllAsync(cancellationToken);
        var query = allItems.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.ProductCode))
        {
            query = query.Where(x => x.ProductCode.Contains(request.ProductCode, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.ProductName))
        {
            query = query.Where(x => x.ProductName.Contains(request.ProductName, StringComparison.OrdinalIgnoreCase));
        }

        query = query.Where(x => x.Type == ProductType.Product || x.Type == ProductType.Goods);

        var totalCount = query.Count();

        // Apply sorting
        query = ApplySorting(query, request.SortBy, request.SortDescending);

        var items = query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList()
            .Select(x => new ProductMarginDto
            {
                ProductCode = x.ProductCode,
                ProductName = x.ProductName,
                PriceWithoutVat = x.EshopPrice?.PriceWithoutVat,
                PurchasePrice = x.ErpPrice?.PurchasePrice,
                ManufactureDifficulty = x.ManufactureDifficulty,
                MaterialCost = x.ErpPrice?.PurchasePrice ?? 0,
                ManufactureCost = CalculateManufactureCost(request.DateFrom ?? _timeProvider.GetUtcNow().AddYears(-1), request.DateTo ?? _timeProvider.GetUtcNow(), x.ManufactureDifficulty),
                AverageMargin = CalculateMargin(x.EshopPrice?.PriceWithoutVat, CalculateTotalCost(x.ErpPrice?.PurchasePrice, CalculateManufactureCost(request.DateFrom ?? _timeProvider.GetUtcNow().AddYears(-1), request.DateTo ?? _timeProvider.GetUtcNow(), x.ManufactureDifficulty)))
            })
            .ToList();

        return new GetProductMarginsResponse
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }

    private decimal? CalculateManufactureCost(DateTimeOffset requestDateFrom, DateTimeOffset requestDateTo, double argManufactureDifficulty)
    {
        // TODO: Implementovat async výpočet výrobních nákladů pomocí _ledgerService.GetTotalPersonalCosts
        // Pro nyní vrátíme null - implementace bude dokončena později
        return null;
    }

    private static decimal? CalculateTotalCost(decimal? materialCost, decimal? manufactureCost)
    {
        return (materialCost ?? 0) + (manufactureCost ?? 0);
    }

    private static decimal? CalculateMargin(decimal? price, decimal? cost)
    {
        if (price == null || cost == null || price == 0) return null;

        var margin = ((price.Value - cost.Value) / price.Value) * 100;

        return Math.Round(margin, 2);
    }

    private static IQueryable<CatalogAggregate> ApplySorting(IQueryable<CatalogAggregate> query, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            return query.OrderBy(x => x.ProductCode);
        }

        return sortBy.ToLower() switch
        {
            "productcode" => sortDescending ? query.OrderByDescending(x => x.ProductCode) : query.OrderBy(x => x.ProductCode),
            "productname" => sortDescending ? query.OrderByDescending(x => x.ProductName) : query.OrderBy(x => x.ProductName),
            "pricewithoutVat" => sortDescending
                ? query.OrderByDescending(x => x.EshopPrice != null ? x.EshopPrice.PriceWithoutVat : (decimal?)null)
                : query.OrderBy(x => x.EshopPrice != null ? x.EshopPrice.PriceWithoutVat : (decimal?)null),
            "purchaseprice" => sortDescending
                ? query.OrderByDescending(x => x.ErpPrice != null ? x.ErpPrice.PurchasePrice : (decimal?)null)
                : query.OrderBy(x => x.ErpPrice != null ? x.ErpPrice.PurchasePrice : (decimal?)null),
            _ => query.OrderBy(x => x.ProductCode)
        };
    }
}