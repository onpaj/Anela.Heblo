using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Maps Catalog's CatalogAggregate to the Manufacture-owned ProductCatalogSnapshot (and back for
/// stock taking records). Enum mapping is explicit and throws on unknown values so a new Catalog
/// enum member fails loudly in tests instead of being silently misclassified.
/// </summary>
internal static class CatalogAggregateSnapshotMapper
{
    public static ProductCatalogSnapshot ToSnapshot(CatalogAggregate source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new ProductCatalogSnapshot
        {
            ProductCode = source.ProductCode,
            ProductName = source.ProductName,
            ProductNameNormalized = source.ProductNameNormalized,
            Type = ToKind(source.Type),
            MinimalManufactureQuantity = source.MinimalManufactureQuantity,
            NetWeight = source.NetWeight,
            HasLots = source.HasLots,
            ManufactureDifficulty = source.ManufactureDifficulty,
            Stock = new ProductStockSnapshot
            {
                Eshop = source.Stock.Eshop,
                Erp = source.Stock.Erp,
                Transport = source.Stock.Transport,
                Manufactured = source.Stock.Manufactured,
                Reserve = source.Stock.Reserve,
                Quarantine = source.Stock.Quarantine,
                Ordered = source.Stock.Ordered,
                Planned = source.Stock.Planned,
                PrimaryStockSource = ToKind(source.Stock.PrimaryStockSource),
            },
            Properties = new ProductPropertiesSnapshot
            {
                OptimalStockDaysSetup = source.Properties.OptimalStockDaysSetup,
                StockMinSetup = source.Properties.StockMinSetup,
                BatchSize = source.Properties.BatchSize,
                ExpirationMonths = source.Properties.ExpirationMonths,
                AllowedResiduePercentage = source.Properties.AllowedResiduePercentage,
            },
            SalesHistory = source.SalesHistory
                .Select(s => new ProductSalesRecordSnapshot { Date = s.Date, AmountB2B = s.AmountB2B, AmountB2C = s.AmountB2C })
                .ToList(),
            ConsumedHistory = source.ConsumedHistory
                .Select(c => new ProductConsumptionRecordSnapshot { Date = c.Date, Amount = c.Amount })
                .ToList(),
            ManufactureHistory = source.ManufactureHistory
                .Select(m => new ProductManufactureRecordSnapshot { Date = m.Date, Amount = m.Amount })
                .ToList(),
            StockTakingHistory = source.StockTakingHistory.Select(ToSnapshot).ToList(),
        };
    }

    public static ProductStockTakingSnapshot ToSnapshot(StockTakingRecord record) => new()
    {
        Id = record.Id,
        Type = ToKind(record.Type),
        Code = record.Code,
        AmountNew = record.AmountNew,
        AmountOld = record.AmountOld,
        Date = record.Date,
        User = record.User,
        Error = record.Error,
    };

    public static StockTakingRecord ToDomain(ProductStockTakingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new StockTakingRecord
        {
            Id = snapshot.Id,
            Type = snapshot.Type switch
            {
                StockTakingKind.Erp => StockTakingType.Erp,
                StockTakingKind.Eshop => StockTakingType.Eshop,
                _ => throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.Type, "Unknown stock taking kind"),
            },
            Code = snapshot.Code,
            AmountNew = snapshot.AmountNew,
            AmountOld = snapshot.AmountOld,
            Date = snapshot.Date,
            User = snapshot.User,
            Error = snapshot.Error,
        };
    }

    private static ProductKind ToKind(ProductType type) => type switch
    {
        ProductType.UNDEFINED => ProductKind.UNDEFINED,
        ProductType.Goods => ProductKind.Goods,
        ProductType.Material => ProductKind.Material,
        ProductType.SemiProduct => ProductKind.SemiProduct,
        ProductType.Product => ProductKind.Product,
        ProductType.Set => ProductKind.Set,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown product type"),
    };

    private static StockTakingKind ToKind(StockTakingType type) => type switch
    {
        StockTakingType.Erp => StockTakingKind.Erp,
        StockTakingType.Eshop => StockTakingKind.Eshop,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown stock taking type"),
    };

    private static StockSourceKind ToKind(StockSource source) => source switch
    {
        StockSource.Erp => StockSourceKind.Erp,
        StockSource.Eshop => StockSourceKind.Eshop,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown stock source"),
    };
}
