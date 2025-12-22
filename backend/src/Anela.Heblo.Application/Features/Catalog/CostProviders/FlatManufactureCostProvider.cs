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

    private const string ManufacturingCostCenter = "VYROBA";

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
        var (dateFrom, dateTo, costsFrom, costsTo) = GetDateRange();
        var months = GenerateMonthRange(costsFrom, costsTo);

        var manufacturingCosts = await _ledgerService.GetDirectCosts(
            costsFrom,
            costsTo,
            ManufacturingCostCenter,
            ct);
        var totalCost = (double)manufacturingCosts.Sum(c => c.Cost);

        var weightedTotals = CalculateWeightedManufactureTotals(products, costsFrom, costsTo);
        var totalWeightedPoints = weightedTotals.Values.Sum(s => s.WeightedManufactured);

        if (totalWeightedPoints == 0)
        {
            _logger.LogWarning("No manufacture history found for period {DateFrom} to {DateTo}", dateFrom, dateTo);
            return CreateCostCacheData(CreateEmptyProductCosts(products, months), dateFrom, dateTo);
        }

        var costPerPoint = totalCost / totalWeightedPoints;
        var productCosts = CalculateProductCosts(products, weightedTotals, costPerPoint, months);

        return CreateCostCacheData(productCosts, dateFrom, dateTo);
    }

    private (DateOnly dateFrom, DateOnly dateTo, DateTime costsFrom, DateTime costsTo) GetDateRange()
    {
        var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-_options.HistoricalDataYears));
        var dateTo = DateOnly.FromDateTime(DateTime.UtcNow);

        var costsFrom = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var costsTo = new DateTime(dateTo.Year, dateTo.Month, DateTime.DaysInMonth(dateTo.Year, dateTo.Month), 23, 59, 59);

        return (dateFrom, dateTo, costsFrom, costsTo);
    }

    private static List<DateTime> GenerateMonthRange(DateTime from, DateTime to)
    {
        var months = new List<DateTime>();
        var current = from;
        while (current <= to)
        {
            months.Add(current);
            current = current.AddMonths(1);
        }
        return months;
    }

    private static Dictionary<CatalogAggregate, ManufactureSummary> CalculateWeightedManufactureTotals(
        List<CatalogAggregate> products,
        DateTime from,
        DateTime to)
    {
        var weightedTotals = new Dictionary<CatalogAggregate, ManufactureSummary>();

        foreach (var product in products)
        {
            var history = product.ManufactureHistory
                .Where(w => w.Date >= from && w.Date <= to)
                .ToList(); // Materialize to avoid multiple enumeration

            var weightedPoints = history.Sum(s =>
            {
                var difficulty = product.ManufactureDifficultySettings.GetDifficultyForDate(s.Date)?.DifficultyValue ?? 1;
                return (decimal)(s.Amount * difficulty);
            });

            var manufactured = history.Sum(s => s.Amount);

            weightedTotals[product] = new ManufactureSummary
            {
                WeightedManufactured = (double)weightedPoints,
                Manufactured = manufactured
            };
        }

        return weightedTotals;
    }

    private static Dictionary<string, List<MonthlyCost>> CreateEmptyProductCosts(
        IEnumerable<CatalogAggregate> products,
        List<DateTime> months)
    {
        var productCosts = new Dictionary<string, List<MonthlyCost>>();

        foreach (var product in products)
        {
            if (string.IsNullOrEmpty(product.ProductCode))
                continue;

            productCosts[product.ProductCode] = months.Select(m => new MonthlyCost(m, 0m)).ToList();
        }

        return productCosts;
    }

    private static Dictionary<string, List<MonthlyCost>> CalculateProductCosts(
        List<CatalogAggregate> products,
        Dictionary<CatalogAggregate, ManufactureSummary> weightedTotals,
        double costPerPoint,
        List<DateTime> months)
    {
        var productCosts = new Dictionary<string, List<MonthlyCost>>();

        foreach (var product in products)
        {
            if (string.IsNullOrEmpty(product.ProductCode))
                continue;

            var productWeightedPoints = weightedTotals[product];

            decimal productCostPerPiece = 0;
            if (productWeightedPoints.Manufactured > 0)
                productCostPerPiece = (decimal)(productWeightedPoints.WeightedManufactured * costPerPoint / productWeightedPoints.Manufactured);

            productCosts[product.ProductCode] = months.Select(m => new MonthlyCost(m, productCostPerPiece)).ToList();
        }

        return productCosts;
    }

    private static CostCacheData CreateCostCacheData(
        Dictionary<string, List<MonthlyCost>> productCosts,
        DateOnly dataFrom,
        DateOnly dataTo)
    {
        return new CostCacheData
        {
            ProductCosts = productCosts,
            LastUpdated = DateTime.UtcNow,
            DataFrom = dataFrom,
            DataTo = dataTo,
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

internal class ManufactureSummary
{
    public double WeightedManufactured { get; set; }
    public double Manufactured { get; set; }
}