using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.Repositories;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Repositories;

/// <summary>
/// Material cost source (M0) - calculates costs from purchase history or manufacturing BoM.
/// Business logic layer with cache fallback.
/// </summary>
public class PurchasePriceOnlyMaterialCostSource : IMaterialCostSource
{
    private static readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly IMaterialCostCache _cache;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<PurchasePriceOnlyMaterialCostSource> _logger;
    private readonly CostCacheOptions _options;

    public PurchasePriceOnlyMaterialCostSource(
        IMaterialCostCache cache,
        ICatalogRepository catalogRepository,
        ILogger<PurchasePriceOnlyMaterialCostSource> logger,
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

    public async Task RefreshCacheAsync(CancellationToken ct = default)
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
        // Returns purchase price only (per spec section 2.1)
        var costs = new List<MonthlyCost>();

        if (product.PurchaseHistory == null || !product.PurchaseHistory.Any())
            return costs;

        // Calculate average purchase price per month
        var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (currentMonth <= endMonth)
        {
            var monthPurchases = product.PurchaseHistory
                .Where(p => p.Date.Year == currentMonth.Year && p.Date.Month == currentMonth.Month)
                .ToList();

            if (monthPurchases.Any())
            {
                var avgPrice = monthPurchases.Average(p => p.PricePerPiece);
                costs.Add(new MonthlyCost(currentMonth, avgPrice));
            }

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