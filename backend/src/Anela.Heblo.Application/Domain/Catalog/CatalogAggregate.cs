using Anela.Heblo.Application.Domain.Catalog.Attributes;
using Anela.Heblo.Application.Domain.Catalog.ConsumedMaterials;
using Anela.Heblo.Application.Domain.Catalog.PurchaseHistory;
using Anela.Heblo.Application.Domain.Catalog.Sales;
using Anela.Heblo.Application.Domain.Catalog.Stock;
using Anela.Heblo.Application.Domain.Purchase;
using Anela.Heblo.Xcc;
using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Application.Domain.Catalog;

public class CatalogAggregate : Entity<string>
{
    public string ProductCode { get => Id; set => Id = value; }
    public string ProductName { get; set; }

    public int ErpId { get; set; }

    public ProductType Type { get; set; } = Catalog.ProductType.UNDEFINED;

    public StockData Stock { get; set; } = new ();
    public CatalogProperties Properties { get; set; } = new();
    
    public List<StockTakingRecord> StockTakingHistory { get; set; } = new ();
    
    public string Location { get; set; } = string.Empty;

    public IList<CatalogSaleRecord> SalesHistory { get; set; } = new List<CatalogSaleRecord>();
    public IList<ConsumedMaterialRecord> ConsumedHistory { get; set; } = new List<ConsumedMaterialRecord>();
    public IReadOnlyList<CatalogPurchaseRecord> PurchaseHistory { get; set; } = new List<CatalogPurchaseRecord>();

    public IReadOnlyList<Supplier> Suppliers { get; set; } = new List<Supplier>();

    public string MinimalOrderQuantity { get; set; } = "";
    public double MinimalManufactureQuantity { get; set; } = 0;

    
    
    // Readonly PROPS
    public string? PrimarySupplier => Suppliers.FirstOrDefault(f => f.IsPrimary)?.Name;
    public bool IsSameFamily(CatalogAggregate product) => product.ProductFamily == this.ProductFamily;
    public bool IsSameType(CatalogAggregate product) => product.ProductType == this.ProductType;
    public string? ProductFamily => ProductCode?.Left(Math.Min(6, ProductCode.Length));
    public string? ProductType => ProductCode?.Left(Math.Min(3, ProductCode.Length));
    public string? SizeCode => ProductCode?.Substring(Math.Min(6, ProductCode.Length));
    
    
    public bool IsInSeason(DateTime referenceTime) => Properties.SeasonMonths.Length > 0 && !Properties.SeasonMonths.Contains(referenceTime.Month);

    public bool IsUnderStocked => Stock.Available < Properties.StockMinSetup && IsMinStockConfigured;
    public bool IsMinStockConfigured => Properties.StockMinSetup > 0;
    public bool IsOptimalStockConfigured => Properties.OptimalStockDaysSetup > 0;
    public DateTime? LastStockTaking => StockTakingHistory.LastOrDefault()?.Date;
    public bool HasExpiration { get; set; }
    public bool HasLots { get; set; }
    public double Volume { get; set; }
    public double Weight { get; set; }

    public double GetConsumed(DateTime dateFrom, DateTime dateTo) => ConsumedHistory
        .Where(w => w.Date >= dateFrom && w.Date <= dateTo)
        .Sum(s => s.Amount);

    public CatalogSalesSummary GetSales(DateTime dateFrom, DateTime dateTo)
    {
        var sales = SalesHistory
            .Where(w => w.Date >= dateFrom && w.Date <= dateTo)
            .ToList();
        var days = (dateTo - dateFrom).TotalDays;
        return new CatalogSalesSummary()
        {
            B2B = sales.Sum(s => s.SumB2B),
            B2C = sales.Sum(s => s.SumB2C),
            AmountB2B = sales.Sum(s => s.AmountB2B),
            AmountB2C = sales.Sum(s => s.AmountB2C),
            DailyB2B = (double)sales.Sum(s => s.SumB2B) / days,
            DailyB2C = (double)sales.Sum(s => s.SumB2C) / days,
            DateFrom = dateFrom,
            DateTo = dateTo,
        };
    }
    
    public double GetTotalSold(DateTime dateFrom, DateTime dateTo) => SalesHistory
        .Where(w => w.Date >= dateFrom && w.Date <= dateTo)
        .Sum(s => s.AmountB2B + s.AmountB2C);

}