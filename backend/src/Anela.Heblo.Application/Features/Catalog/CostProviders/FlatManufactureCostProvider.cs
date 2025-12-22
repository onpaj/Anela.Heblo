using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.CostProviders;

/// <summary>
/// Flat manufacture cost provider (M1_A) - Distributes manufacturing costs across products using ManufactureDifficulty.
/// Business logic layer with cache fallback.
/// </summary>
public class FlatManufactureCostProvider : IFlatManufactureCostProvider
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private readonly IFlatManufactureCostCache _cache;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILedgerService _ledgerService;
    private readonly ILogger<FlatManufactureCostProvider> _logger;
    private readonly CostCacheOptions _options;

    public FlatManufactureCostProvider(
        IFlatManufactureCostCache cache,
        ICatalogRepository catalogRepository,
        ILedgerService ledgerService,
        ILogger<FlatManufactureCostProvider> logger,
        IOptions<CostCacheOptions> options)
    {
        _cache = cache;
        _catalogRepository = catalogRepository;
        _ledgerService = ledgerService;
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
            _logger.LogWarning("FlatManufactureCostCache not hydrated yet");
            return new Dictionary<string, List<MonthlyCost>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting flat manufacture costs");
            throw;
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await RefreshLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("FlatManufactureCostCache refresh already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting FlatManufactureCostCache refresh");

            var data = await ComputeAllCostsAsync(ct);
            await _cache.SetCachedDataAsync(data, ct);

            _logger.LogInformation(
                "FlatManufactureCostCache refreshed successfully: {ProductCount} products",
                data.ProductCosts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh FlatManufactureCostCache");
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

        var products = (await _catalogRepository.GetAllAsync(ct)).ToList();
        var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-_options.HistoricalDataYears));
        var dateTo = DateOnly.FromDateTime(DateTime.UtcNow);

        var costsFrom = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var costsTo = new DateTime(dateTo.Year, dateTo.Month, DateTime.DaysInMonth(dateTo.Year, dateTo.Month), 23, 59, 59);

        var months = new List<DateTime>();
        var newDate = costsFrom;
        while (newDate <= costsTo)
        {
            months.Add(newDate);
            newDate = newDate.AddMonths(1);
        }

        var productCosts = new Dictionary<string, List<MonthlyCost>>();

        var manufacturingCosts = await _ledgerService.GetDirectCosts(
            costsFrom,
            costsTo,
            "VYROBA",
            ct);

        var totalCost = manufacturingCosts.Sum(c => c.Cost);

        // Calculate weighted manufacture points for each product (amount × difficulty)
        var manufacturedWeightedTotals = new Dictionary<CatalogAggregate, decimal>();
        foreach (var product in products)
        {
            var weightedPoints = product.ManufactureHistory
                .Where(w => w.Date >= costsFrom && w.Date <= costsTo)
                .Sum(s =>
                {
                    var difficulty = product.ManufactureDifficultySettings.GetDifficultyForDate(s.Date)?.DifficultyValue ?? 1;
                    return (decimal)(s.Amount * difficulty);
                });
            manufacturedWeightedTotals[product] = weightedPoints;
        }

        var totalWeightedPoints = manufacturedWeightedTotals.Values.Sum();

        // Handle division by zero - if no manufacture history, return zero costs
        if (totalWeightedPoints == 0)
        {
            _logger.LogWarning("No manufacture history found for period {DateFrom} to {DateTo}", dateFrom, dateTo);

            foreach (var product in products)
            {
                if (string.IsNullOrEmpty(product.ProductCode))
                    continue;

                productCosts[product.ProductCode] = months.Select(m => new MonthlyCost(m, 0m)).ToList();
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

        var costPerPoint = totalCost / totalWeightedPoints;

        foreach (var product in products)
        {
            if (string.IsNullOrEmpty(product.ProductCode))
                continue;

            var productWeightedPoints = manufacturedWeightedTotals[product];
            var productCostPerMonth = productWeightedPoints * costPerPoint / months.Count;

            productCosts[product.ProductCode] = months.Select(m => new MonthlyCost(m, productCostPerMonth)).ToList();
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