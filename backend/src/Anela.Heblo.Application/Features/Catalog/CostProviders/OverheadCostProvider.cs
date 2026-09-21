using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.CostProviders;

/// <summary>
/// Overhead cost provider (M3) - distributes the overhead pool across products by sold pieces.
///
/// The pool is CostPool.M3: accounts 51+52 minus VYROBA (which M1 carries) and minus
/// SKLAD+MARKETING (which M2 carries). It is a catch-all by design, so a cost centre added in
/// Flexi lands here instead of vanishing, and M1+M2+M3 always sum to the full ledger.
///
/// The denominator is deliberately the one SalesCostProvider uses - company-wide sold pieces,
/// synthetic bundle-component rows excluded - so M2 and M3 are directly comparable per piece.
/// </summary>
public class OverheadCostProvider : IOverheadCostProvider
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private readonly IOverheadCostCache _cache;
    private readonly IServiceProvider _serviceProvider;
    private readonly ICostPoolService _costPoolService;
    private readonly ILogger<OverheadCostProvider> _logger;
    private readonly DataSourceOptions _options;
    private readonly TimeProvider _timeProvider;

    // ICatalogRepository is resolved lazily via IServiceProvider, not injected directly:
    // CatalogRepository -> IMarginCalculationService -> IOverheadCostProvider -> ICatalogRepository
    // is a real constructor-time cycle. See ManufactureBasedMaterialCostProvider for the same fix.
    public OverheadCostProvider(
        IOverheadCostCache cache,
        IServiceProvider serviceProvider,
        ICostPoolService costPoolService,
        ILogger<OverheadCostProvider> logger,
        IOptions<DataSourceOptions> options,
        TimeProvider timeProvider)
    {
        _cache = cache;
        _serviceProvider = serviceProvider;
        _costPoolService = costPoolService;
        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider;
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
            _logger.LogWarning("OverheadCostCache not hydrated yet");
            return new Dictionary<string, List<MonthlyCost>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting overhead costs");
            throw;
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await RefreshLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("OverheadCostCache refresh already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting OverheadCostCache refresh");

            var data = await ComputeAllCostsAsync(ct);
            await _cache.SetCachedDataAsync(data, ct);

            _logger.LogInformation(
                "OverheadCostCache refreshed successfully: {ProductCount} products",
                data.ProductCosts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh OverheadCostCache");
            throw;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task<CostCacheData> ComputeAllCostsAsync(CancellationToken ct)
    {
        var catalogRepository = _serviceProvider.GetRequiredService<ICatalogRepository>();
        await catalogRepository.WaitForCurrentMergeAsync(ct);

        var products = (await catalogRepository.GetAllAsync(ct)).ToList();
        var (dateFrom, dateTo, costsFrom, costsTo) = GetDateRange();
        var months = GenerateMonthRange(costsFrom, costsTo);

        // Krok 1: Načíst režijní náklady (vše mimo VYROBA / SKLAD / MARKETING)
        var pools = await _costPoolService.GetMonthlyPoolsAsync(
            DateOnly.FromDateTime(costsFrom),
            DateOnly.FromDateTime(costsTo),
            ct);

        // Only M3. The service returns every pool for the window, and M1/M2 are already carried
        // by FlatManufactureCostProvider and SalesCostProvider - summing them here would charge
        // each product the same ledger twice.
        var totalCost = (double)pools
            .Where(p => p.Pool == CostPool.M3)
            .Sum(p => p.Amount);

        // CostPoolService emits a zero-amount row per month and pool, so an empty or silently failed
        // ledger read is indistinguishable downstream from a genuinely empty pool: every product ends
        // up with M3 == M2 and nothing says why. Say it here.
        if (totalCost == 0)
        {
            _logger.LogWarning(
                "Overhead cost pool (M3) is empty for period {DateFrom} to {DateTo} - every product will report zero overhead",
                dateFrom, dateTo);
        }

        // Krok 2: Spočítat celkový počet prodaných kusů
        var totalSoldPieces = CalculateTotalSoldPieces(products, costsFrom, costsTo);

        // Krok 3: Vypočítat náklad na kus
        if (totalSoldPieces == 0)
        {
            _logger.LogWarning("No sales history found for period {DateFrom} to {DateTo}", dateFrom, dateTo);
            return CreateCostCacheData(CreateEmptyProductCosts(products, months), dateFrom, dateTo);
        }

        var costPerPiece = totalCost / totalSoldPieces;

        // Krok 4: Vypočítat náklady pro každý produkt
        var productCosts = CalculateProductCosts(products, costPerPiece, months);

        return CreateCostCacheData(productCosts, dateFrom, dateTo);
    }

    private (DateOnly dateFrom, DateOnly dateTo, DateTime costsFrom, DateTime costsTo) GetDateRange()
    {
        // Read the clock once, through the injected provider: two reads can straddle midnight on
        // the 1st and put dateFrom and dateTo in different months, which would drift this
        // provider's window away from the margin history window that is aligned to it.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var dateFrom = DateOnly.FromDateTime(now.AddDays(-_options.ManufactureCostHistoryDays));
        var dateTo = DateOnly.FromDateTime(now);

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

    private static double CalculateTotalSoldPieces(
        List<CatalogAggregate> products,
        DateTime from,
        DateTime to)
    {
        double totalSold = 0;

        foreach (var product in products)
        {
            var productSold = product.SalesHistory
                .Where(s => s.Date >= from && s.Date <= to && s.SourceBundleCode == null)
                .Sum(s => s.AmountTotal);

            totalSold += productSold;
        }

        return totalSold;
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
        double costPerPiece,
        List<DateTime> months)
    {
        var productCosts = new Dictionary<string, List<MonthlyCost>>();

        foreach (var product in products)
        {
            if (string.IsNullOrEmpty(product.ProductCode))
                continue;

            // Plošný rozpočet - stejný náklad na kus pro všechny měsíce
            var costPerPieceDecimal = (decimal)costPerPiece;
            productCosts[product.ProductCode] = months.Select(m => new MonthlyCost(m, costPerPieceDecimal)).ToList();
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
