using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.CostProviders;

/// <summary>
/// Sales/Marketing cost source (M2) - STUB implementation returning constant.
/// Business logic layer with cache fallback.
/// </summary>
public class SalesCostProvider : ISalesCostProvider
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private readonly ISalesCostCache _cache;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<SalesCostProvider> _logger;
    private readonly CostCacheOptions _options;

    public SalesCostProvider(
        ISalesCostCache cache,
        ICatalogRepository catalogRepository,
        ILogger<SalesCostProvider> logger,
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
            _logger.LogWarning("SalesCostCache not hydrated, computing costs directly");
            return await ComputeCostsAsync(productCodes, dateFrom, dateTo, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sales costs");
            throw;
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await RefreshLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("SalesCostCache refresh already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting SalesCostCache refresh");

            var data = await ComputeAllCostsAsync(ct);
            await _cache.SetCachedDataAsync(data, ct);

            _logger.LogInformation(
                "SalesCostCache refreshed successfully: {ProductCount} products",
                data.ProductCosts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh SalesCostCache");
            throw;
        }
        finally
        {
            RefreshLock.Release();
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

            var monthlyCosts = CalculateSalesCosts(product, dateFrom, dateTo);
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

            var monthlyCosts = CalculateSalesCosts(product, from, to);
            productCosts[product.ProductCode] = monthlyCosts;
        }

        return productCosts;
    }

    private List<MonthlyCost> CalculateSalesCosts(CatalogAggregate product, DateOnly dateFrom, DateOnly dateTo)
    {
        // STUB: Returns constant value of 15 (per spec section 2.4)
        var costs = new List<MonthlyCost>();

        var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (currentMonth <= endMonth)
        {
            costs.Add(new MonthlyCost(currentMonth, 15m));
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
