using MediatR;
using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog;

public class GetProductMarginsHandler : IRequestHandler<GetProductMarginsRequest, GetProductMarginsResponse>
{
    private readonly ICatalogRepository _catalogRepository;

    public GetProductMarginsHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
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

        query = query.Where(x => x.Type == ProductType.Product);

        var totalCount = query.Count();

        var items = query
            .OrderBy(x => x.ProductCode)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList()
            .Select(x => new ProductMarginDto
            {
                ProductCode = x.ProductCode,
                ProductName = x.ProductName,
                PriceWithVat = x.EshopPrice?.PriceWithVat,
                PurchasePrice = x.ErpPrice?.PurchasePrice,
                AverageCost = GenerateMockAverageCost(x.ErpPrice?.PurchasePrice),
                Cost30Days = GenerateMockCost30Days(x.ErpPrice?.PurchasePrice),
                AverageMargin = CalculateMockMargin(x.EshopPrice?.PriceWithVat, x.ErpPrice?.PurchasePrice, true),
                Margin30Days = CalculateMockMargin(x.EshopPrice?.PriceWithVat, x.ErpPrice?.PurchasePrice, false)
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

    private static decimal? GenerateMockAverageCost(decimal? baseCost)
    {
        if (baseCost == null) return null;
        var random = new Random(baseCost.GetHashCode());
        var variation = (decimal)(random.NextDouble() * 0.2 - 0.1);
        return baseCost.Value * (1 + variation);
    }

    private static decimal? GenerateMockCost30Days(decimal? baseCost)
    {
        if (baseCost == null) return null;
        var random = new Random(baseCost.GetHashCode() + 1);
        var variation = (decimal)(random.NextDouble() * 0.3 - 0.15);
        return baseCost.Value * (1 + variation);
    }

    private static decimal? CalculateMockMargin(decimal? price, decimal? cost, bool isAverage)
    {
        if (price == null || cost == null || cost == 0) return null;
        
        var actualCost = isAverage ? GenerateMockAverageCost(cost) : GenerateMockCost30Days(cost);
        if (actualCost == null || actualCost == 0) return null;
        
        var margin = ((price.Value - actualCost.Value) / price.Value) * 100;
        return Math.Round(margin, 2);
    }
}