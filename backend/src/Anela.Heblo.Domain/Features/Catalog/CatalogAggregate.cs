using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
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

    public decimal? PriceWithVat => GetSafeProductPrice();
    public decimal? PriceWithoutVat => GetSafeProductPrideWithoutVat();
    
    public bool PriceIsFromEshop => EshopPrice?.PriceWithVat is not 0;

    private decimal? GetSafeProductPrideWithoutVat()
    {
        // Safe price extraction
        if (EshopPrice?.PriceWithoutVat is > 0)
        {
            return EshopPrice.PriceWithoutVat.Value;
        }

        if (ErpPrice?.PriceWithoutVat is > 0)
        {
            return ErpPrice.PriceWithoutVat;
        }

        return null;
    }
    
    private decimal? GetSafeProductPrice()
    {
        // Safe price extraction
        if (EshopPrice?.PriceWithVat is > 0)
        {
            return EshopPrice.PriceWithVat.Value;
        }

        if (ErpPrice?.PriceWithVat is > 0)
        {
            return ErpPrice.PriceWithVat;
        }

        return null;
    }

    public bool HasBoM => ErpPrice?.HasBoM ?? false;

    public int? BoMId => ErpPrice?.BoMId;

    public List<StockTakingRecord> StockTakingHistory { get; set; } = new();

    public string Location { get; set; } = string.Empty;

    private IList<CatalogSaleRecord> _salesHistory = new List<CatalogSaleRecord>();
    private IList<ConsumedMaterialRecord> _consumedHistory = new List<ConsumedMaterialRecord>();
    private IReadOnlyList<CatalogPurchaseRecord> _purchaseHistory = new List<CatalogPurchaseRecord>();
    private IReadOnlyList<ManufactureHistoryRecord> _manufactureHistory = new List<ManufactureHistoryRecord>();

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

    public IReadOnlyList<ManufactureHistoryRecord> ManufactureHistory
    {
        get => _manufactureHistory;
        set
        {
            _manufactureHistory = value;
            // No summary update needed for manufacture history yet
        }
    }

    public SaleHistorySummary SaleHistorySummary { get; set; } = new();
    public ConsumedHistorySummary ConsumedHistorySummary { get; set; } = new();
    public PurchaseHistorySummary PurchaseHistorySummary { get; set; } = new();

    public string MinimalOrderQuantity { get; set; } = "";
    public double MinimalManufactureQuantity { get; set; } = 0;
    public double? ManufactureDifficulty => ManufactureDifficultySettings.ManufactureDifficulty;

    public List<ManufactureCost> ManufactureCostHistory { get; set; } = new();

    // Manufacture difficulty settings - historical data and current value
    public ManufactureDifficultyConfiguration ManufactureDifficultySettings { get; set; } = new();

    // Margin properties - calculated after ManufactureCostHistory is populated
    public decimal MarginPercentage { get; set; } = 0;
    public decimal MarginAmount { get; set; } = 0;

    // Readonly PROPS
    public bool IsSameFamily(CatalogAggregate product) => product.ProductFamily == this.ProductFamily;
    public bool IsSameCategory(CatalogAggregate product) => product.ProductCategory == this.ProductCategory;
    public string? ProductFamily => ProductCode?.Left(Math.Min(6, ProductCode.Length));
    public string? ProductCategory => ProductCode?.Left(Math.Min(3, ProductCode.Length));
    public string? SizeCode => ProductCode?.Substring(Math.Min(6, ProductCode.Length));


    public bool IsInSeason(DateTime referenceTime) => Properties.SeasonMonths.Length > 0 && !Properties.SeasonMonths.Contains(referenceTime.Month);

    public bool IsUnderStocked => Stock.Available < Properties.StockMinSetup && IsMinStockConfigured;
    public bool IsMinStockConfigured => Properties.StockMinSetup > 0;
    public bool IsOptimalStockConfigured => Properties.OptimalStockDaysSetup > 0;
    public DateTime? LastStockTaking => StockTakingHistory.OrderByDescending(o => o.Date).FirstOrDefault()?.Date;
    public bool HasExpiration { get; set; }
    public bool HasLots { get; set; }
    public double Volume { get; set; }
    public double? NetWeight { get; set; }
    public double? GrossWeight { get; set; }

    public string? Note { get; set; }

    // Price convenience properties
    public decimal? CurrentSellingPrice => EshopPrice?.PriceWithVat ?? ErpPrice?.PriceWithoutVat;
    public decimal? CurrentPurchasePrice => EshopPrice?.PurchasePrice ?? ErpPrice?.PurchasePrice;
    public decimal? SellingPriceWithVat => EshopPrice?.PriceWithVat ?? ErpPrice?.PriceWithVat;
    public decimal? PurchasePriceWithVat => ErpPrice?.PurchasePriceWithVat;
    public string? SupplierCode { get; set; }
    public string? SupplierName { get; set; }
    public string? DefaultImage { get; set; }
    public string? Image { get; set; }
    public double? Height { get; set; }
    public double? Width { get; set; }
    public double? Depth { get; set; }
    public bool AtypicalShipping { get; set; }

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

    public void UpdateMarginCalculation()
    {
        decimal averageTotalCost = 0;

        // First try to use manufacturing cost history for manufactured products
        if (ManufactureCostHistory.Count > 0)
        {
            averageTotalCost = ManufactureCostHistory
                .Average(record => record.Total);
        }
        // Fallback to ERP purchase price for purchased products
        else if (ErpPrice?.PurchasePrice > 0)
        {
            averageTotalCost = ErpPrice.PurchasePrice;
        }

        // If no cost data available, set margins to zero
        if (averageTotalCost == 0)
        {
            MarginPercentage = 0;
            MarginAmount = 0;
            return;
        }

        // Get selling price without VAT from eshop
        var sellingPrice = PriceWithoutVat ?? 0;

        if (sellingPrice > 0)
        {
            MarginAmount = sellingPrice - averageTotalCost;
            MarginPercentage = (MarginAmount / sellingPrice) * 100;
        }
        else
        {
            MarginPercentage = 0;
            MarginAmount = 0;
        }
    }

    /// <summary>
    /// Synchronizes stock taking results with the catalog aggregate.
    /// Updates stock levels, lots, and adds the record to history.
    /// </summary>
    /// <param name="stockTakingRecord">The stock taking record to process</param>
    public void SyncStockTaking(StockTakingRecord stockTakingRecord)
    {
        if (stockTakingRecord == null)
            throw new ArgumentNullException(nameof(stockTakingRecord));

        if (stockTakingRecord.Code != ProductCode)
            throw new ArgumentException($"Stock taking record code '{stockTakingRecord.Code}' does not match product code '{ProductCode}'", nameof(stockTakingRecord));

        // Add record to history
        StockTakingHistory.Add(stockTakingRecord);

        // Update appropriate stock level based on type
        var newStockLevel = (decimal)stockTakingRecord.AmountNew;

        switch (stockTakingRecord.Type)
        {
            case StockTakingType.Erp:
                Stock.Erp = newStockLevel;
                break;
            case StockTakingType.Eshop:
                Stock.Eshop = newStockLevel;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stockTakingRecord.Type), stockTakingRecord.Type, "Unknown stock taking type");
        }
    }

    /// <summary>
    /// Synchronizes stock taking results with lots information.
    /// Updates stock levels, replaces lots, and adds the record to history.
    /// </summary>
    /// <param name="stockTakingRecord">The stock taking record to process</param>
    /// <param name="updatedLots">The updated lots from stock taking</param>
    public void SyncStockTaking(StockTakingRecord stockTakingRecord, List<CatalogLot> updatedLots)
    {
        if (stockTakingRecord == null)
            throw new ArgumentNullException(nameof(stockTakingRecord));

        if (updatedLots == null)
            throw new ArgumentNullException(nameof(updatedLots));

        // First perform basic stock synchronization
        SyncStockTaking(stockTakingRecord);

        // Update lots information
        Stock.Lots.Clear();
        Stock.Lots.AddRange(updatedLots);

        // Verify that the sum of lots matches the new stock amount
        var totalLotAmount = updatedLots.Sum(lot => lot.Amount);
        var expectedAmount = (decimal)stockTakingRecord.AmountNew;

        if (Math.Abs(totalLotAmount - expectedAmount) > 0.01m) // Allow for small rounding differences
        {
            throw new InvalidOperationException(
                $"Total lot amount ({totalLotAmount}) does not match expected stock amount ({expectedAmount}) for product {ProductCode}");
        }
    }

}