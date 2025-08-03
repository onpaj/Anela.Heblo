using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Xcc;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Catalog;

public class CatalogAggregate : Entity<string>
{
    public string ProductCode { get => Id; set => Id = value; }
    public string ProductName { get; set; }

    public int ErpId { get; set; }

    public ProductType Type { get; set; } = Catalog.ProductType.UNDEFINED;

    public StockData Stock { get; set; } = new();
    public CatalogProperties Properties { get; set; } = new();

    public ProductPriceEshop? EshopPrice { get; set; }
    public ProductPriceErp? ErpPrice { get; set; }

    public List<StockTakingRecord> StockTakingHistory { get; set; } = new();

    public string Location { get; set; } = string.Empty;

    private IList<CatalogSaleRecord> _salesHistory = new List<CatalogSaleRecord>();
    private IList<ConsumedMaterialRecord> _consumedHistory = new List<ConsumedMaterialRecord>();
    private IReadOnlyList<CatalogPurchaseRecord> _purchaseHistory = new List<CatalogPurchaseRecord>();

    public IList<CatalogSaleRecord> SalesHistory
    {
        get => _salesHistory;
        set
        {
            _salesHistory = value;
            UpdateSaleHistorySummary();
        }
    }

    public IList<ConsumedMaterialRecord> ConsumedHistory
    {
        get => _consumedHistory;
        set
        {
            _consumedHistory = value;
            UpdateConsumedHistorySummary();
        }
    }

    public IReadOnlyList<CatalogPurchaseRecord> PurchaseHistory
    {
        get => _purchaseHistory;
        set
        {
            _purchaseHistory = value;
            UpdatePurchaseHistorySummary();
        }
    }

    public SaleHistorySummary SaleHistorySummary { get; set; } = new();
    public ConsumedHistorySummary ConsumedHistorySummary { get; set; } = new();
    public PurchaseHistorySummary PurchaseHistorySummary { get; set; } = new();

    public List<string> SupplierNames { get; set; } = new List<string>();

    public string MinimalOrderQuantity { get; set; } = "";
    public double MinimalManufactureQuantity { get; set; } = 0;



    // Readonly PROPS
    public string? PrimarySupplier => SupplierNames.FirstOrDefault();
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

    // Price convenience properties
    public decimal? CurrentSellingPrice => EshopPrice?.PriceWithVat ?? ErpPrice?.PriceWithoutVat;
    public decimal? CurrentPurchasePrice => EshopPrice?.PurchasePrice ?? ErpPrice?.PurchasePrice;
    public decimal? SellingPriceWithVat => EshopPrice?.PriceWithVat ?? ErpPrice?.PriceWithVat;
    public decimal? PurchasePriceWithVat => ErpPrice?.PurchasePriceWithVat;

    public double GetConsumed(DateTime dateFrom, DateTime dateTo) => ConsumedHistory
        .Where(w => w.Date >= dateFrom && w.Date <= dateTo)
        .Sum(s => s.Amount);

    public double GetTotalSold(DateTime dateFrom, DateTime dateTo) => SalesHistory
        .Where(w => w.Date >= dateFrom && w.Date <= dateTo)
        .Sum(s => s.AmountB2B + s.AmountB2C);

    public void UpdateSaleHistorySummary()
    {
        var monthlyData = new Dictionary<string, MonthlySalesSummary>();

        var groupedSales = SalesHistory
            .GroupBy(s => new { s.Date.Year, s.Date.Month })
            .ToList();

        foreach (var group in groupedSales)
        {
            var monthKey = $"{group.Key.Year:D4}-{group.Key.Month:D2}";
            monthlyData[monthKey] = new MonthlySalesSummary
            {
                Year = group.Key.Year,
                Month = group.Key.Month,
                TotalB2B = group.Sum(s => s.SumB2B),
                TotalB2C = group.Sum(s => s.SumB2C),
                AmountB2B = group.Sum(s => s.AmountB2B),
                AmountB2C = group.Sum(s => s.AmountB2C),
                TransactionCount = group.Count()
            };
        }

        SaleHistorySummary.MonthlyData = monthlyData;
        SaleHistorySummary.LastUpdated = DateTime.UtcNow;
    }

    public void UpdatePurchaseHistorySummary()
    {
        var monthlyData = new Dictionary<string, MonthlyPurchaseSummary>();

        var groupedPurchases = PurchaseHistory
            .GroupBy(p => new { p.Date.Year, p.Date.Month })
            .ToList();

        foreach (var group in groupedPurchases)
        {
            var monthKey = $"{group.Key.Year:D4}-{group.Key.Month:D2}";

            var supplierBreakdown = group
                .GroupBy(p => p.SupplierName ?? "Unknown")
                .ToDictionary(
                    sg => sg.Key,
                    sg => new SupplierPurchaseSummary
                    {
                        SupplierName = sg.Key,
                        Amount = sg.Sum(p => p.Amount),
                        Cost = sg.Sum(p => p.PriceTotal),
                        PurchaseCount = sg.Count()
                    });

            monthlyData[monthKey] = new MonthlyPurchaseSummary
            {
                Year = group.Key.Year,
                Month = group.Key.Month,
                TotalAmount = group.Sum(p => p.Amount),
                TotalCost = group.Sum(p => p.PriceTotal),
                AveragePricePerPiece = group.Count() > 0 ? group.Average(p => p.PricePerPiece) : 0,
                PurchaseCount = group.Count(),
                SupplierBreakdown = supplierBreakdown
            };
        }

        PurchaseHistorySummary.MonthlyData = monthlyData;
        PurchaseHistorySummary.LastUpdated = DateTime.UtcNow;
    }

    public void UpdateConsumedHistorySummary()
    {
        var monthlyData = new Dictionary<string, MonthlyConsumedSummary>();

        var groupedConsumed = ConsumedHistory
            .GroupBy(c => new { c.Date.Year, c.Date.Month })
            .ToList();

        foreach (var group in groupedConsumed)
        {
            var monthKey = $"{group.Key.Year:D4}-{group.Key.Month:D2}";
            monthlyData[monthKey] = new MonthlyConsumedSummary
            {
                Year = group.Key.Year,
                Month = group.Key.Month,
                TotalAmount = group.Sum(c => c.Amount),
                ConsumptionCount = group.Count()
            };
        }

        ConsumedHistorySummary.MonthlyData = monthlyData;
        ConsumedHistorySummary.LastUpdated = DateTime.UtcNow;
    }

    public void UpdateAllSummaries()
    {
        UpdateSaleHistorySummary();
        UpdatePurchaseHistorySummary();
        UpdateConsumedHistorySummary();
    }

}