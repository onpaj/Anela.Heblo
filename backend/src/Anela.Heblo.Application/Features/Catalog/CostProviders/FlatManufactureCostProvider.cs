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
    private readonly IManufactureHistoryClient _manufactureHistoryClient;
    private readonly IManufactureDifficultyRepository _difficultyRepository;
    private readonly ILogger<FlatManufactureCostProvider> _logger;
    private readonly CostCacheOptions _options;

    public FlatManufactureCostProvider(
        IFlatManufactureCostCache cache,
        ICatalogRepository catalogRepository,
        ILedgerService ledgerService,
        IManufactureHistoryClient manufactureHistoryClient,
        IManufactureDifficultyRepository difficultyRepository,
        ILogger<FlatManufactureCostProvider> logger,
        IOptions<CostCacheOptions> options)
    {
        _cache = cache;
        _catalogRepository = catalogRepository;
        _ledgerService = ledgerService;
        _manufactureHistoryClient = manufactureHistoryClient;
        _difficultyRepository = difficultyRepository;
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
            return new  Dictionary<string, List<MonthlyCost>>();
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

        var products = await _catalogRepository.GetAllAsync(ct);
        var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-_options.HistoricalDataYears));
        var dateTo = DateOnly.FromDateTime(DateTime.UtcNow);

        var productCosts = new Dictionary<string, List<MonthlyCost>>();

        foreach (var product in products)
        {
            if (string.IsNullOrEmpty(product.ProductCode))
                continue;

            var monthlyCosts = await CalculateFlatManufacturingCostsAsync(product, dateFrom, dateTo, ct);
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

            var monthlyCosts = await CalculateFlatManufacturingCostsAsync(product, from, to, ct);
            productCosts[product.ProductCode] = monthlyCosts;
        }

        return productCosts;
    }

    /// <summary>
    /// Calculates flat manufacturing costs (M1_A) for a product over a date range.
    /// Uses rolling window approach with ManufactureDifficulty weighting.
    /// </summary>
    public async Task<List<MonthlyCost>> CalculateFlatManufacturingCostsAsync(
        CatalogAggregate product,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(product.ProductCode))
        {
            return new List<MonthlyCost>();
        }

        // Step 1: Get total manufacturing costs for the period (VYROBA department)
        var costsFrom = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var costsTo = new DateTime(dateTo.Year, dateTo.Month, DateTime.DaysInMonth(dateTo.Year, dateTo.Month), 23, 59, 59);

        var manufacturingCosts = await _ledgerService.GetDirectCosts(
            costsFrom,
            costsTo,
            "VYROBA",
            ct);

        // Step 2: Get manufacture history for ALL products in the period
        var allManufactureHistory = await _manufactureHistoryClient.GetHistoryAsync(
            costsFrom,
            costsTo,
            null, // null = all products
            ct);

        if (!allManufactureHistory.Any())
        {
            _logger.LogWarning(
                "No manufacture history found for period {DateFrom} to {DateTo}",
                dateFrom,
                dateTo);
            return new List<MonthlyCost>();
        }

        // Step 3: Group costs and history by month
        var costsByMonth = manufacturingCosts
            .GroupBy(c => new DateTime(c.Date.Year, c.Date.Month, 1))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Cost));

        var historyByMonth = allManufactureHistory
            .GroupBy(h => new DateTime(h.Date.Year, h.Date.Month, 1))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Step 4: Calculate cost per manufacturing point for each month
        var monthlyCosts = new List<MonthlyCost>();
        var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (currentMonth <= endMonth)
        {
            if (!costsByMonth.TryGetValue(currentMonth, out var monthCosts) || monthCosts == 0)
            {
                // No costs for this month - zero cost
                monthlyCosts.Add(new MonthlyCost(currentMonth, 0m));
                currentMonth = currentMonth.AddMonths(1);
                continue;
            }

            if (!historyByMonth.TryGetValue(currentMonth, out var monthHistory) || !monthHistory.Any())
            {
                // No production in this month - zero cost
                monthlyCosts.Add(new MonthlyCost(currentMonth, 0m));
                currentMonth = currentMonth.AddMonths(1);
                continue;
            }

            // Calculate total weighted manufacturing points for this month (all products)
            var totalWeightedPoints = 0.0;
            foreach (var record in monthHistory)
            {
                var difficulty = await GetHistoricalDifficultyAsync(record.ProductCode, record.Date, ct);
                totalWeightedPoints += record.Amount * difficulty;
            }

            if (totalWeightedPoints == 0)
            {
                monthlyCosts.Add(new MonthlyCost(currentMonth, 0m));
                currentMonth = currentMonth.AddMonths(1);
                continue;
            }

            // Calculate cost per point for this month
            var costPerPoint = monthCosts / (decimal)totalWeightedPoints;

            // Calculate cost for this specific product
            var productDifficulty = await GetHistoricalDifficultyAsync(product.ProductCode, currentMonth, ct);
            var productCost = costPerPoint * productDifficulty;

            monthlyCosts.Add(new MonthlyCost(currentMonth, productCost));
            currentMonth = currentMonth.AddMonths(1);
        }

        return monthlyCosts;
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

    /// <summary>
    /// Default difficulty value when no setting exists for a product.
    /// </summary>
    private const int DefaultDifficultyValue = 1;

    /// <summary>
    /// Gets the historical manufacturing difficulty for a product at a specific date.
    /// Returns DefaultDifficultyValue if no setting exists.
    /// </summary>
    public async Task<int> GetHistoricalDifficultyAsync(string productCode, DateTime referenceDate, CancellationToken ct = default)
    {
        var setting = await _difficultyRepository.FindAsync(productCode, referenceDate, ct);
        return setting?.DifficultyValue ?? DefaultDifficultyValue;
    }
}