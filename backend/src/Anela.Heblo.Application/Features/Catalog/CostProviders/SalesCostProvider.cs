using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.CostProviders;

/// <summary>
/// Sales/Marketing cost provider (M2) - Distributes warehouse and marketing costs across products by sold pieces.
/// Business logic layer with cache fallback.
/// </summary>
public class SalesCostProvider : ISalesCostProvider
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private readonly ISalesCostCache _cache;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILedgerService _ledgerService;
    private readonly ILogger<SalesCostProvider> _logger;
    private readonly DataSourceOptions _options;

    private const string WarehouseCostCenter = "SKLAD";
    private const string MarketingCostCenter = "MARKETING";

    public SalesCostProvider(
        ISalesCostCache cache,
        ICatalogRepository catalogRepository,
        ILedgerService ledgerService,
        ILogger<SalesCostProvider> logger,
        IOptions<DataSourceOptions> options)
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
            _logger.LogWarning("SalesCostCache not hydrated yet");
            return new Dictionary<string, List<MonthlyCost>>();
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

        var products = (await _catalogRepository.GetAllAsync(ct)).ToList();
        var (dateFrom, dateTo, costsFrom, costsTo) = GetDateRange();
        var months = GenerateMonthRange(costsFrom, costsTo);

        // Krok 1: Načíst náklady SKLAD + MARKETING
        var warehouseCosts = await _ledgerService.GetDirectCosts(
            costsFrom,
            costsTo,
            WarehouseCostCenter,
            ct);
        var marketingCosts = await _ledgerService.GetDirectCosts(
            costsFrom,
            costsTo,
            MarketingCostCenter,
            ct);

        var totalCost = (double)(warehouseCosts.Sum(c => c.Cost) + marketingCosts.Sum(c => c.Cost));

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
        var dateFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-_options.ManufactureCostHistoryDays));
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

    private static double CalculateTotalSoldPieces(
        List<CatalogAggregate> products,
        DateTime from,
        DateTime to)
    {
        double totalSold = 0;

        foreach (var product in products)
        {
            var productSold = product.SalesHistory
                .Where(s => s.Date >= from && s.Date <= to)
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
