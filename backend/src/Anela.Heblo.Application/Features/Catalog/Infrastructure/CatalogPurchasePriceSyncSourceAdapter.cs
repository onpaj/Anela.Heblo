using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;

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
    private const int MaxLoggedExcludedCodes = 10;

    private readonly ICatalogRepository _catalogRepository;
    private readonly IProductPriceErpClient _priceClient;
    private readonly IErpStockClient _stockClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogPurchasePriceSyncSourceAdapter> _logger;

    public CatalogPurchasePriceSyncSourceAdapter(
        ICatalogRepository catalogRepository,
        IProductPriceErpClient priceClient,
        IErpStockClient stockClient,
        TimeProvider timeProvider,
        ILogger<CatalogPurchasePriceSyncSourceAdapter> logger)
    {
        _catalogRepository = catalogRepository;
        _priceClient = priceClient;
        _stockClient = stockClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PurchasePriceSyncCandidate>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        var items = (await _catalogRepository.GetAllAsync(cancellationToken))
            .Where(a => a.Type is ProductType.Material or ProductType.Goods)
            .ToList();

        var prices = FirstByCode(await _priceClient.GetAllAsync(forceReload: true, cancellationToken), p => p.ProductCode);

        var today = _timeProvider.GetUtcNow().Date;
        var materialStock = FirstByCode(
            await StockToDateOrThrowAsync(MaterialWarehouseId, today, cancellationToken), s => s.ProductCode);
        var goodsStock = FirstByCode(
            await StockToDateOrThrowAsync(ProductsWarehouseId, today, cancellationToken), s => s.ProductCode);

        var candidates = new List<PurchasePriceSyncCandidate>(items.Count);
        var excludedCodes = new List<string>();
        foreach (var item in items)
        {
            if (!prices.TryGetValue(item.ProductCode, out var price) || price.ErpItemId <= 0)
            {
                excludedCodes.Add(item.ProductCode);
                continue;
            }

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

        if (excludedCodes.Count > 0)
        {
            _logger.LogInformation(
                "Purchase price sync excluded {Count} Material/Goods items without a ceník row or with ErpItemId <= 0 (e.g. {ProductCodes})",
                excludedCodes.Count, string.Join(", ", excludedCodes.Take(MaxLoggedExcludedCodes)));
        }

        return candidates;
    }

    /// <summary>
    /// Rem.FlexiBeeSDK's StockToDateClient swallows non-2xx responses and returns an empty list,
    /// so an empty result here is indistinguishable from a failed read. MATERIAL and ZBOZI are
    /// never legitimately empty, so treat zero rows as a failure and refuse to sync on missing data.
    /// </summary>
    private async Task<IReadOnlyList<ErpStock>> StockToDateOrThrowAsync(int warehouseId, DateTime date, CancellationToken cancellationToken)
    {
        var rows = await _stockClient.StockToDateAsync(date, warehouseId, cancellationToken);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                $"Flexi stock-to-date for warehouse {warehouseId} on {date:yyyy-MM-dd} returned no rows; " +
                "refusing to sync purchase prices on missing stock data.");
        }

        return rows;
    }

    private static Dictionary<string, T> FirstByCode<T>(IEnumerable<T> rows, Func<T, string> code) =>
        rows.Where(r => !string.IsNullOrEmpty(code(r)))
            .GroupBy(code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
}
