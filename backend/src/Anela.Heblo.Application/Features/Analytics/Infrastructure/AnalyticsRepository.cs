using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Services;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Analytics.Infrastructure;

/// <summary>
/// 🔒 PERFORMANCE FIX: Analytics repository with streaming capabilities
/// Prevents memory overload by streaming data instead of loading everything at once
/// </summary>
public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ApplicationDbContext _dbContext;
    private readonly IMarginCalculationService _marginCalculationService;

    public AnalyticsRepository(ICatalogRepository catalogRepository, ApplicationDbContext dbContext, IMarginCalculationService marginCalculationService)
    {
        _catalogRepository = catalogRepository;
        _dbContext = dbContext;
        _marginCalculationService = marginCalculationService;
    }

    /// <summary>
    /// Streams products with sales to avoid memory overload
    /// Converts heavy CatalogAggregate to lightweight AnalyticsProduct
    /// </summary>
    public async IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        ProductType[] productTypes,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const int batchSize = 100;

        // Get total count for batching
        var allProducts = await _catalogRepository.GetProductsWithSalesInPeriod(fromDate, toDate, productTypes, cancellationToken);

        // Process in batches to reduce memory usage
        for (int i = 0; i < allProducts.Count; i += batchSize)
        {
            var batch = allProducts.Skip(i).Take(batchSize);

            foreach (var product in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Convert to lightweight analytics model
                // Calculate current month margin using IMarginCalculationService
                var marginData = await _marginCalculationService.GetMarginAsync(
                    product, 
                    DateOnly.FromDateTime(fromDate), 
                    DateOnly.FromDateTime(toDate), 
                    cancellationToken);
                
                // Use M0 margin (material + manufacturing) as equivalent to legacy MarginAmount
                var latestMargin = marginData.MonthlyData.LastOrDefault();
                var marginAmount = latestMargin?.M0.Amount ?? 0;
                var materialCost = latestMargin?.CostsForMonth.MaterialCost ?? 0;
                var handlingCost = latestMargin?.CostsForMonth.ManufacturingCost ?? 0;
                
                // Get latest purchase price from purchase history
                var latestPurchase = product.PurchaseHistory?.OrderByDescending(p => p.Date).FirstOrDefault();
                var purchasePrice = latestPurchase?.PricePerPiece ?? 0;

                yield return new AnalyticsProduct
                {
                    ProductCode = product.ProductCode,
                    ProductName = product.ProductName,
                    Type = product.Type,
                    ProductFamily = product.ProductFamily,
                    ProductCategory = product.ProductCategory,
                    MarginAmount = marginAmount,
                    
                    // M0-M3 margin amounts
                    M0Amount = latestMargin?.M0.Amount ?? 0,
                    M1Amount = latestMargin?.M1.Amount ?? 0,
                    M2Amount = latestMargin?.M2.Amount ?? 0,
                    M3Amount = latestMargin?.M3.Amount ?? 0,
                    
                    // M0-M3 margin percentages
                    M0Percentage = latestMargin?.M0.Percentage ?? 0,
                    M1Percentage = latestMargin?.M1.Percentage ?? 0,
                    M2Percentage = latestMargin?.M2.Percentage ?? 0,
                    M3Percentage = latestMargin?.M3.Percentage ?? 0,
                    
                    // Pricing
                    SellingPrice = product.EshopPrice?.PriceWithoutVat ?? 0,
                    EshopPriceWithoutVat = product.EshopPrice?.PriceWithoutVat,
                    PurchasePrice = purchasePrice,
                    
                    // Cost components
                    MaterialCost = materialCost,
                    HandlingCost = handlingCost,
                    SalesHistory = product.SalesHistory
                        .Where(s => s.Date >= fromDate && s.Date <= toDate)
                        .Select(s => new SalesDataPoint
                        {
                            Date = s.Date,
                            AmountB2B = s.AmountB2B,
                            AmountB2C = s.AmountB2C
                        })
                        .ToList()
                };
            }

            // Allow garbage collection between batches
            GC.Collect();
        }
    }

    /// <summary>
    /// Gets aggregated margin data with optimized query (future implementation)
    /// </summary>
    public async Task<Dictionary<string, decimal>> GetGroupMarginTotalsAsync(
        DateTime fromDate,
        DateTime toDate,
        ProductType[] productTypes,
        ProductGroupingMode groupingMode,
        CancellationToken cancellationToken = default)
    {
        // TODO: This would be optimized SQL query in real database implementation
        // For now, use existing logic but avoid loading full objects
        var groupTotals = new Dictionary<string, decimal>();

        await foreach (var product in StreamProductsWithSalesAsync(fromDate, toDate, productTypes, cancellationToken))
        {
            if (product.MarginAmount <= 0)
                continue;

            var groupKey = GetGroupKey(product, groupingMode);
            var totalSold = product.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C);
            var marginContribution = (decimal)totalSold * product.MarginAmount;

            if (!groupTotals.ContainsKey(groupKey))
                groupTotals[groupKey] = 0;

            groupTotals[groupKey] += marginContribution;
        }

        return groupTotals;
    }

    public async Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var product = await _catalogRepository.GetByIdAsync(productId, cancellationToken);
        if (product == null)
            return null;

        // Convert to analytics product with filtered sales data
        // Calculate margin using IMarginCalculationService
        var marginData = await _marginCalculationService.GetMarginAsync(
            product, 
            DateOnly.FromDateTime(fromDate), 
            DateOnly.FromDateTime(toDate), 
            cancellationToken);
        
        // Use M0 margin (material + manufacturing) as equivalent to legacy MarginAmount
        var latestMargin = marginData.MonthlyData.LastOrDefault();
        var marginAmount = latestMargin?.M0.Amount ?? 0;
        var materialCost = latestMargin?.CostsForMonth.MaterialCost ?? 0;
        var handlingCost = latestMargin?.CostsForMonth.ManufacturingCost ?? 0;
        
        // Get latest purchase price from purchase history
        var latestPurchase = product.PurchaseHistory?.OrderByDescending(p => p.Date).FirstOrDefault();
        var purchasePrice = latestPurchase?.PricePerPiece ?? 0;

        return new AnalyticsProduct
        {
            ProductCode = product.ProductCode,
            ProductName = product.ProductName,
            Type = product.Type,
            ProductFamily = product.ProductFamily,
            ProductCategory = product.ProductCategory,
            MarginAmount = marginAmount,
            
            // M0-M3 margin amounts
            M0Amount = latestMargin?.M0.Amount ?? 0,
            M1Amount = latestMargin?.M1.Amount ?? 0,
            M2Amount = latestMargin?.M2.Amount ?? 0,
            M3Amount = latestMargin?.M3.Amount ?? 0,
            
            // M0-M3 margin percentages
            M0Percentage = latestMargin?.M0.Percentage ?? 0,
            M1Percentage = latestMargin?.M1.Percentage ?? 0,
            M2Percentage = latestMargin?.M2.Percentage ?? 0,
            M3Percentage = latestMargin?.M3.Percentage ?? 0,
            
            // Pricing
            SellingPrice = product.EshopPrice?.PriceWithoutVat ?? 0,
            EshopPriceWithoutVat = product.EshopPrice?.PriceWithoutVat,
            PurchasePrice = purchasePrice,
            
            // Cost components
            MaterialCost = materialCost,
            HandlingCost = handlingCost,
            SalesHistory = product.SalesHistory
                .Select(s => new SalesDataPoint
                {
                    Date = s.Date,
                    AmountB2B = s.AmountB2B,
                    AmountB2C = s.AmountB2C
                })
                .ToList()
        };
    }

    private string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode)
    {
        return groupingMode switch
        {
            ProductGroupingMode.Products => product.ProductCode,
            ProductGroupingMode.ProductFamily => product.ProductFamily ?? "Unknown",
            ProductGroupingMode.ProductCategory => product.ProductCategory ?? "Unknown",
            _ => product.ProductCode
        };
    }


    /// <summary>
    /// Gets daily invoice import statistics for monitoring purposes
    /// </summary>
    public async Task<List<DailyInvoiceCount>> GetInvoiceImportStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default)
    {
        // PostgreSQL timestamp without time zone: work with UTC dates but store as Unspecified
        // Convert input dates to UTC if needed, then to Unspecified for PostgreSQL compatibility
        if (startDate.Kind != DateTimeKind.Utc)
            startDate = startDate.ToUniversalTime();
        if (endDate.Kind != DateTimeKind.Utc)
            endDate = endDate.ToUniversalTime();

        // Convert to Unspecified for PostgreSQL timestamp without time zone queries
        var startDateUnspecified = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
        var endDateUnspecified = DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified);

        var results = new List<DailyInvoiceCount>();

        if (dateType == ImportDateType.InvoiceDate)
        {
            var rawResults = await _dbContext.IssuedInvoices
                .Where(i => i.InvoiceDate >= startDateUnspecified && i.InvoiceDate <= endDateUnspecified)
                .GroupBy(i => new { Year = i.InvoiceDate.Year, Month = i.InvoiceDate.Month, Day = i.InvoiceDate.Day })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Day = g.Key.Day,
                    Count = g.Count()
                })
                .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                .ToListAsync(cancellationToken);

            results = rawResults.Select(r => new DailyInvoiceCount
            {
                Date = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                Count = r.Count,
                IsBelowThreshold = false
            }).ToList();
        }
        else
        {
            var rawResults = await _dbContext.IssuedInvoices
                .Where(i => i.LastSyncTime.HasValue &&
                           i.LastSyncTime.Value >= startDateUnspecified &&
                           i.LastSyncTime.Value <= endDateUnspecified)
                .GroupBy(i => new { Year = i.LastSyncTime.Value.Year, Month = i.LastSyncTime.Value.Month, Day = i.LastSyncTime.Value.Day })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Day = g.Key.Day,
                    Count = g.Count()
                })
                .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                .ToListAsync(cancellationToken);

            results = rawResults.Select(r => new DailyInvoiceCount
            {
                Date = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                Count = r.Count,
                IsBelowThreshold = false
            }).ToList();
        }

        // Fill in missing dates with zero counts - work with UTC dates for consistency
        var filledResults = new List<DailyInvoiceCount>();
        var currentDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var endDateOnly = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

        while (currentDate <= endDateOnly)
        {
            var existingResult = results.FirstOrDefault(r => r.Date.Date == currentDate.Date);
            if (existingResult != null)
            {
                filledResults.Add(existingResult);
            }
            else
            {
                filledResults.Add(new DailyInvoiceCount
                {
                    Date = currentDate,
                    Count = 0,
                    IsBelowThreshold = false
                });
            }

            currentDate = currentDate.AddDays(1);
        }

        return filledResults;
    }
}