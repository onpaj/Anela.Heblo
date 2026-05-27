using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class PurchaseMaterialCatalogAdapter : IMaterialCatalogService
{
    private readonly ICatalogRepository _catalogRepository;

    public PurchaseMaterialCatalogAdapter(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task<MaterialInfo?> GetByIdAsync(string productCode, CancellationToken cancellationToken)
    {
        var aggregate = await _catalogRepository.GetByIdAsync(productCode, cancellationToken);
        return aggregate is null ? null : ToMaterialInfo(aggregate);
    }

    public async Task<IReadOnlyDictionary<string, MaterialInfo>> GetByIdsAsync(
        IEnumerable<string> productCodes,
        CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetByIdsAsync(productCodes, cancellationToken);

        var result = new Dictionary<string, MaterialInfo>(aggregates.Count, StringComparer.Ordinal);
        foreach (var (id, aggregate) in aggregates)
        {
            result[id] = ToMaterialInfo(aggregate);
        }

        return result;
    }

    public async Task<IReadOnlyList<MaterialInfo>> GetAllAsync(CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetAllAsync(cancellationToken);
        return aggregates.Select(ToMaterialInfo).ToList();
    }

    public async Task<IReadOnlyList<MaterialStockSnapshot>> GetStockAnalysisSnapshotsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetAllAsync(cancellationToken);

        return aggregates
            .Where(IsAnalysable)
            .Select(item => ToStockSnapshot(item, fromUtc, toUtc))
            .ToList();
    }

    public async Task<IReadOnlyList<MaterialBomReference>> GetMaterialsWithBomAsync(
        CancellationToken cancellationToken)
    {
        var aggregates = await _catalogRepository.GetAllAsync(cancellationToken);

        return aggregates
            .Where(item => item.HasBoM && item.BoMId.HasValue)
            .Select(item => new MaterialBomReference
            {
                ProductCode = item.ProductCode,
                BoMId = item.BoMId!.Value,
            })
            .ToList();
    }

    private static bool IsAnalysable(CatalogAggregate item) =>
        item.Type == ProductType.Material || item.Type == ProductType.Goods;

    private static MaterialInfo ToMaterialInfo(CatalogAggregate aggregate) => new()
    {
        ProductCode = aggregate.ProductCode,
        ProductName = aggregate.ProductName,
        Note = aggregate.Note,
        HasBoM = aggregate.HasBoM,
        BoMId = aggregate.BoMId,
    };

    private static MaterialStockSnapshot ToStockSnapshot(
        CatalogAggregate item,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var consumption = item.Type == ProductType.Material
            ? item.GetConsumed(fromUtc, toUtc)
            : item.GetTotalSold(fromUtc, toUtc);

        return new MaterialStockSnapshot
        {
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            ProductNameNormalized = item.ProductNameNormalized,
            ProductType = MapProductType(item.Type),
            SupplierName = item.SupplierName,
            MinimalOrderQuantity = item.MinimalOrderQuantity,
            IsMinStockConfigured = item.IsMinStockConfigured,
            IsOptimalStockConfigured = item.IsOptimalStockConfigured,
            Stock = new MaterialStockLevels
            {
                Available = item.Stock.Available,
                Ordered = item.Stock.Ordered,
                EffectiveStock = item.Stock.EffectiveStock,
            },
            StockMinSetup = item.Properties.StockMinSetup,
            OptimalStockDaysSetup = item.Properties.OptimalStockDaysSetup,
            ConsumptionInPeriod = consumption,
            LastPurchase = ToLastPurchase(item.PurchaseHistory),
        };
    }

    private static MaterialPurchaseSnapshot? ToLastPurchase(IReadOnlyList<CatalogPurchaseRecord> history)
    {
        var latest = history
            .OrderByDescending(p => p.Date)
            .FirstOrDefault();

        if (latest is null)
            return null;

        return new MaterialPurchaseSnapshot
        {
            Date = latest.Date,
            SupplierName = latest.SupplierName ?? string.Empty,
            Amount = (decimal)latest.Amount,
            UnitPrice = latest.PricePerPiece,
            TotalPrice = latest.PriceTotal,
        };
    }

    private static MaterialProductType MapProductType(ProductType type) => type switch
    {
        ProductType.Material => MaterialProductType.Material,
        ProductType.Goods => MaterialProductType.Goods,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type),
            type,
            "Adapter only projects Material and Goods. IsAnalysable filter must be applied first."),
    };
}
