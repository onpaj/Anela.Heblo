using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.CostProviders;

/// <summary>
/// Material cost source (M0) - calculates costs based on product type.
/// For Set/Product/SemiProduct: uses manufacture history with temporal carry-forward.
/// For other types: uses PurchasePriceWithVat.
/// </summary>
public class ManufactureBasedMaterialCostProvider : IMaterialCostProvider
{
    private static readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly IMaterialCostCache _cache;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<ManufactureBasedMaterialCostProvider> _logger;
    private readonly CostCacheOptions _options;

    public ManufactureBasedMaterialCostProvider(
        IMaterialCostCache cache,
        ICatalogRepository catalogRepository,
        ILogger<ManufactureBasedMaterialCostProvider> logger,
        IOptions<CostCacheOptions> options)
    {
        _cache = cache;
        _catalogRepository = catalogRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(
        List<string>? productCodes = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheData = await _cache.GetCachedDataAsync(cancellationToken);

            if (cacheData.IsHydrated)
            {
                return FilterByProductCodes(cacheData.ProductCosts, productCodes);
            }

            // Fallback - compute directly (cache not hydrated yet)
            _logger.LogWarning("MaterialCostCache not hydrated, computing costs directly");
            return await ComputeCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting material costs");
            throw;
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await _refreshLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("MaterialCostCache refresh already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting MaterialCostCache refresh");

            var data = await ComputeAllCostsAsync(ct);
            await _cache.SetCachedDataAsync(data, ct);

            _logger.LogInformation(
                "MaterialCostCache refreshed successfully: {ProductCount} products, {DateRange}",
                data.ProductCosts.Count,
                $"{data.DataFrom} to {data.DataTo}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh MaterialCostCache");
            throw;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<CostCacheData> ComputeAllCostsAsync(CancellationToken ct)
    {
        await _catalogRepository.WaitForCurrentMergeAsync(ct);

        var products = await _catalogRepository.GetAllAsync(ct);
        var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-_options.HistoricalDataYears));
        var dateTo = DateOnly.FromDateTime(DateTime.UtcNow);

        var productCosts = new Dictionary<string, List<MonthlyCost>>();

        foreach (var product in products)
        {
            if (string.IsNullOrEmpty(product.ProductCode))
                continue;

            var monthlyCosts = CalculateMaterialCosts(product, dateFrom, dateTo);
            productCosts[product.ProductCode] = monthlyCosts;
        }

        return new CostCacheData
        {
            ProductCosts = productCosts,
            LastUpdated = DateTime.UtcNow,
            DataFrom = dateFrom,
            DataTo = dateTo,
            IsHydrated = true
        };
    }

    private async Task<Dictionary<string, List<MonthlyCost>>> ComputeCostsAsync(
        List<string>? productCodes,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct)
    {
        await _catalogRepository.WaitForCurrentMergeAsync(ct);
        var products = await _catalogRepository.GetAllAsync(ct);

        var from = dateFrom ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-_options.HistoricalDataYears));
        var to = dateTo ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var productCosts = new Dictionary<string, List<MonthlyCost>>();

        foreach (var product in products)
        {
            if (string.IsNullOrEmpty(product.ProductCode))
                continue;

            if (productCodes != null && !productCodes.Contains(product.ProductCode))
                continue;

            var monthlyCosts = CalculateMaterialCosts(product, from, to);
            productCosts[product.ProductCode] = monthlyCosts;
        }

        return productCosts;
    }

    private List<MonthlyCost> CalculateMaterialCosts(CatalogAggregate product, DateOnly dateFrom, DateOnly dateTo)
    {
        // Route based on product type
        if (IsManufacturedProduct(product.Type))
        {
            return CalculateFromManufactureHistory(product, dateFrom, dateTo);
        }
        else
        {
            return CalculateFromPurchasePriceWithVat(product, dateFrom, dateTo);
        }
    }

    private bool IsManufacturedProduct(ProductType type)
    {
        return type == ProductType.Set
            || type == ProductType.Product
            || type == ProductType.SemiProduct;
    }

    private List<MonthlyCost> CalculateFromManufactureHistory(
        CatalogAggregate product,
        DateOnly dateFrom,
        DateOnly dateTo)
    {
        var costs = new List<MonthlyCost>();

        // If no manufacture history, fallback to purchase price
        if (product.ManufactureHistory == null || !product.ManufactureHistory.Any())
        {
            return CalculateFromPurchasePriceWithVat(product, dateFrom, dateTo);
        }

        // Group manufacture records by month and calculate weighted average
        var monthlyManufactures = product.ManufactureHistory
            .GroupBy(m => new { m.Date.Year, m.Date.Month })
            .Select(g => new
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                WeightedAvgPrice = g.Sum(m => m.PricePerPiece * (decimal)m.Amount) / (decimal)g.Sum(m => m.Amount)
            })
            .OrderBy(m => m.Year)
            .ThenBy(m => m.Month)
            .ToList();

        if (!monthlyManufactures.Any())
        {
            return CalculateFromPurchasePriceWithVat(product, dateFrom, dateTo);
        }

        // Build a dictionary of month -> price for easy lookup
        var manufacturePrices = monthlyManufactures.ToDictionary(
            m => new DateTime(m.Year, m.Month, 1),
            m => m.WeightedAvgPrice
        );

        // Iterate through all months in the date range
        var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);
        decimal? lastKnownPrice = null;

        while (currentMonth <= endMonth)
        {
            if (manufacturePrices.TryGetValue(currentMonth, out var monthPrice))
            {
                // Month with manufacture - use weighted average
                costs.Add(new MonthlyCost(currentMonth, monthPrice));
                lastKnownPrice = monthPrice;
            }
            else if (lastKnownPrice.HasValue)
            {
                // No manufacture this month - carry forward last known price
                costs.Add(new MonthlyCost(currentMonth, lastKnownPrice.Value));
            }
            else
            {
                // No manufacture yet in this period - check if there's a future manufacture to backfill
                var futureManufacture = manufacturePrices.Keys
                    .Where(m => m > currentMonth)
                    .OrderBy(m => m)
                    .FirstOrDefault();

                if (futureManufacture != default)
                {
                    // Use the first future manufacture price
                    var backfillPrice = manufacturePrices[futureManufacture];
                    costs.Add(new MonthlyCost(currentMonth, backfillPrice));
                }
                // else: no cost for this month (no manufacture before or after)
            }

            currentMonth = currentMonth.AddMonths(1);
        }

        return costs;
    }

    private List<MonthlyCost> CalculateFromPurchasePriceWithVat(
        CatalogAggregate product,
        DateOnly dateFrom,
        DateOnly dateTo)
    {
        var costs = new List<MonthlyCost>();

        if (!product.PurchasePriceWithVat.HasValue || product.PurchasePriceWithVat.Value <= 0)
        {
            return costs; // No purchase price - return empty
        }

        var purchasePrice = product.PurchasePriceWithVat.Value;
        var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (currentMonth <= endMonth)
        {
            costs.Add(new MonthlyCost(currentMonth, purchasePrice));
            currentMonth = currentMonth.AddMonths(1);
        }

        return costs;
    }

    private static Dictionary<string, List<MonthlyCost>> FilterByProductCodes(
        Dictionary<string, List<MonthlyCost>> allCosts,
        List<string>? productCodes)
    {
        if (productCodes == null || !productCodes.Any())
            return allCosts;

        return allCosts
            .Where(kvp => productCodes.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}
