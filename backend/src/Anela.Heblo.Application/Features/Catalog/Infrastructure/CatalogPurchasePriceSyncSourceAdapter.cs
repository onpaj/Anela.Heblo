using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Builds the nightly purchase price sync candidates: Material and Goods catalog items that
/// have a ceník row, with their current <c>nakupCena</c> (fresh ceník read) and today's
/// <c>prumCena</c> from the item's own warehouse.
/// </summary>
internal sealed class CatalogPurchasePriceSyncSourceAdapter : IPurchasePriceSyncSource
{
    // Flexi warehouse ids — same values as FlexiStockClient / FinancialOverviewStockValueAdapter.
    private const int MaterialWarehouseId = 5; // MATERIAL
    private const int ProductsWarehouseId = 4; // ZBOZI (products and goods)

    private readonly ICatalogRepository _catalogRepository;
    private readonly IProductPriceErpClient _priceClient;
    private readonly IErpStockClient _stockClient;
    private readonly TimeProvider _timeProvider;

    public CatalogPurchasePriceSyncSourceAdapter(
        ICatalogRepository catalogRepository,
        IProductPriceErpClient priceClient,
        IErpStockClient stockClient,
        TimeProvider timeProvider)
    {
        _catalogRepository = catalogRepository;
        _priceClient = priceClient;
        _stockClient = stockClient;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<PurchasePriceSyncCandidate>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        var items = (await _catalogRepository.GetAllAsync(cancellationToken))
            .Where(a => a.Type is ProductType.Material or ProductType.Goods)
            .ToList();

        var prices = FirstByCode(await _priceClient.GetAllAsync(forceReload: true, cancellationToken), p => p.ProductCode);

        var today = _timeProvider.GetUtcNow().Date;
        var materialStock = FirstByCode(
            await _stockClient.StockToDateAsync(today, MaterialWarehouseId, cancellationToken), s => s.ProductCode);
        var goodsStock = FirstByCode(
            await _stockClient.StockToDateAsync(today, ProductsWarehouseId, cancellationToken), s => s.ProductCode);

        var candidates = new List<PurchasePriceSyncCandidate>(items.Count);
        foreach (var item in items)
        {
            if (!prices.TryGetValue(item.ProductCode, out var price) || price.ErpItemId <= 0)
                continue;

            var isMaterial = item.Type == ProductType.Material;
            var stock = isMaterial ? materialStock : goodsStock;

            candidates.Add(new PurchasePriceSyncCandidate
            {
                ProductCode = item.ProductCode,
                ProductType = isMaterial ? MaterialProductType.Material : MaterialProductType.Goods,
                ErpItemId = price.ErpItemId,
                CurrentPurchasePrice = price.PurchasePrice,
                StockPrice = stock.TryGetValue(item.ProductCode, out var stockRow) ? stockRow.Price : null,
            });
        }

        return candidates;
    }

    private static Dictionary<string, T> FirstByCode<T>(IEnumerable<T> rows, Func<T, string> code) =>
        rows.Where(r => !string.IsNullOrEmpty(code(r)))
            .GroupBy(code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
}
