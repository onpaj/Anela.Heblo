using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.DashboardTiles;

/// <summary>
/// Dashboard tile showing products that are approaching stock-out based on average sales data.
/// </summary>
public class LowStockAlertTile : ITile
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly TimeProvider _timeProvider;
    private readonly DataSourceOptions _dataSourceOptions;

    public LowStockAlertTile(
        ICatalogRepository catalogRepository,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> dataSourceOptions)
    {
        _catalogRepository = catalogRepository;
        _timeProvider = timeProvider;
        _dataSourceOptions = dataSourceOptions.Value;
    }

    public string Title => "K přeskladnění (S/R/T)";
    public string Description => "Produkty s nízkou zásobou na základě průměrných prodejů";
    public TileSize Size => TileSize.Medium;
    public TileCategory Category => TileCategory.Warehouse;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var catalogItems = await _catalogRepository.GetAllAsync(cancellationToken);
            var now = _timeProvider.GetUtcNow().DateTime;
            var saleHistoryStartDate = now.AddDays(-_dataSourceOptions.SalesHistoryDays);

            // Filter products (Product and Goods types only)
            var products = catalogItems
                .Where(c => c.Type == ProductType.Product || c.Type == ProductType.Goods)
                .ToList();

            var lowStockProducts = new List<LowStockProductData>();

            foreach (var product in products)
            {
                // Calculate average daily sales from last year
                var salesHistory = product.SalesHistory
                    .Where(s => s.Date >= saleHistoryStartDate && s.Date <= now)
                    .ToList();

                decimal safetyThreshold;
                double averageDailySales;

                bool includeProduct;

                if (!salesHistory.Any())
                {
                    // No sales history - include if has reserve stock
                    averageDailySales = 0;
                    includeProduct = product.Stock.Reserve > 0;
                }
                else
                {
                    var totalSales = salesHistory.Sum(s => s.AmountTotal);
                    averageDailySales = totalSales / _dataSourceOptions.SalesHistoryDays;
                    safetyThreshold = (decimal)(averageDailySales * _dataSourceOptions.ResupplyThresholdMultiplier);
                    // Include if has reserve stock AND low eshop stock
                    includeProduct = product.Stock.Reserve > 0 && product.Stock.Eshop <= safetyThreshold;
                }

                // Check if product should be included
                if (includeProduct)
                {
                    var daysOfStockRemaining = averageDailySales > 0
                        ? (double)product.Stock.Eshop / averageDailySales
                        : double.PositiveInfinity; // Infinite days if no sales

                    lowStockProducts.Add(new LowStockProductData
                    {
                        ProductCode = product.ProductCode,
                        ProductName = product.ProductName,
                        EshopStock = product.Stock.Eshop,
                        ReserveStock = product.Stock.Reserve,
                        TransportStock = product.Stock.Transport,
                        AverageDailySales = (decimal)averageDailySales,
                        DaysOfStockRemaining = averageDailySales > 0 ? (decimal)daysOfStockRemaining : decimal.MaxValue
                    });
                }
            }

            // Sort by lowest stock first
            lowStockProducts = lowStockProducts.OrderBy(p => p.EshopStock).ToList();

            return new
            {
                status = "success",
                data = new
                {
                    products = lowStockProducts,
                    totalCount = lowStockProducts.Count
                },
                metadata = new
                {
                    lastUpdated = now,
                    source = "CatalogRepository"
                },
                drillDown = new
                {
                    filters = new { sortBy = "eshop", sortDescending = false, type = "Product" },
                    enabled = true,
                    tooltip = "Zobrazit inventuru produktů s nízkou zásobou"
                }
            };
        }
        catch (Exception ex)
        {
            return new
            {
                status = "error",
                error = ex.Message
            };
        }
    }
}