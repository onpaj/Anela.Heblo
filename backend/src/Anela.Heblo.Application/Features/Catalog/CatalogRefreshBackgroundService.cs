using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog;

public class CatalogRefreshBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatalogRefreshBackgroundService> _logger;
    private readonly DataSourceOptions _options;

    private DateTime _lastTransportRefresh = DateTime.MinValue;
    private DateTime _lastReserveRefresh = DateTime.MinValue;
    private DateTime _lastSalesRefresh = DateTime.MinValue;
    private DateTime _lastAttributesRefresh = DateTime.MinValue;
    private DateTime _lastErpStockRefresh = DateTime.MinValue;
    private DateTime _lastEshopStockRefresh = DateTime.MinValue;
    private DateTime _lastPurchaseHistoryRefresh = DateTime.MinValue;
    private DateTime _lastManufactureHistoryRefresh = DateTime.MinValue;
    private DateTime _lastConsumedRefresh = DateTime.MinValue;
    private DateTime _lastStockTakingRefresh = DateTime.MinValue;
    private DateTime _lastLotsRefresh = DateTime.MinValue;
    private DateTime _lastEshopPricesRefresh = DateTime.MinValue;
    private DateTime _lastErpPricesRefresh = DateTime.MinValue;
    private DateTime _lastManufactureDifficultyRefresh = DateTime.MinValue;
    private DateTime _lastManufactureCostRefresh = DateTime.MinValue;

    public CatalogRefreshBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<CatalogRefreshBackgroundService> logger,
        IOptions<DataSourceOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Catalog Refresh Background Service started");

        // Perform initial load immediately (if intervals are not zero)
        await PerformRefreshCycle(stoppingToken, isInitialLoad: true);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PerformRefreshCycle(stoppingToken, isInitialLoad: false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when service is stopped
        }

        _logger.LogInformation("Catalog Refresh Background Service stopped");
    }

    private async Task PerformRefreshCycle(CancellationToken stoppingToken, bool isInitialLoad)
    {
        try
        {
            var now = DateTime.UtcNow;

            using var scope = _serviceProvider.CreateScope();
            var catalogRepository = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

            // Check and execute refresh operations based on intervals
            if (await RefreshIfNeeded(catalogRepository, "Transport",
                _lastTransportRefresh, _options.TransportRefreshInterval,
                async ct => await catalogRepository.RefreshTransportData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastTransportRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "Reserve",
                _lastReserveRefresh, _options.ReserveRefreshInterval,
                async ct => await catalogRepository.RefreshReserveData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastReserveRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "Sales",
                _lastSalesRefresh, _options.SalesRefreshInterval,
                async ct => await catalogRepository.RefreshSalesData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastSalesRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "Attributes",
                _lastAttributesRefresh, _options.AttributesRefreshInterval,
                async ct => await catalogRepository.RefreshAttributesData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastAttributesRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "ERP Stock",
                _lastErpStockRefresh, _options.ErpStockRefreshInterval,
                async ct => await catalogRepository.RefreshErpStockData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastErpStockRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "E-shop Stock",
                _lastEshopStockRefresh, _options.EshopStockRefreshInterval,
                async ct => await catalogRepository.RefreshEshopStockData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastEshopStockRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "Purchase History",
                _lastPurchaseHistoryRefresh, _options.PurchaseHistoryRefreshInterval,
                async ct => await catalogRepository.RefreshPurchaseHistoryData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastPurchaseHistoryRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "Manufacture History",
                _lastManufactureHistoryRefresh, _options.ManufactureHistoryRefreshInterval,
                async ct => await catalogRepository.RefreshManufactureHistoryData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastManufactureHistoryRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "Consumed History",
                _lastConsumedRefresh, _options.ConsumedRefreshInterval,
                async ct => await catalogRepository.RefreshConsumedHistoryData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastConsumedRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "Stock Taking",
                _lastStockTakingRefresh, _options.StockTakingRefreshInterval,
                async ct => await catalogRepository.RefreshStockTakingData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastStockTakingRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "Lots Data",
                _lastLotsRefresh, _options.LotsRefreshInterval,
                async ct => await catalogRepository.RefreshLotsData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastLotsRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "E-shop Prices",
                _lastEshopPricesRefresh, _options.EshopPricesRefreshInterval,
                async ct => await catalogRepository.RefreshEshopPricesData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastEshopPricesRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "ERP Prices",
                _lastErpPricesRefresh, _options.ErpPricesRefreshInterval,
                async ct => await catalogRepository.RefreshErpPricesData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastErpPricesRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "Manufacture Difficulty",
                _lastManufactureDifficultyRefresh, _options.ManufactureDifficultyRefreshInterval,
                async ct => await catalogRepository.RefreshManufactureDifficultyData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastManufactureDifficultyRefresh = now;
            }

            // Refresh ManufactureCostData after ManufactureDifficulty and ManufactureHistory are refreshed
            // Use the same interval as ManufactureHistory since both need to be fresh
            if (_lastManufactureDifficultyRefresh >= _lastManufactureCostRefresh &&
                _lastManufactureHistoryRefresh >= _lastManufactureCostRefresh)
            {
                if (await RefreshIfNeeded(catalogRepository, "Manufacture Cost",
                    _lastManufactureCostRefresh, _options.ManufactureHistoryRefreshInterval,
                    async ct => await ((CatalogRepository)catalogRepository).RefreshManufactureCostData(ct),
                    now, stoppingToken, isInitialLoad))
                {
                    _lastManufactureCostRefresh = now;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in Catalog Refresh Background Service");
            // Continue - method will be called again in next cycle
        }
    }

    private async Task<bool> RefreshIfNeeded(
        ICatalogRepository catalogRepository,
        string operationName,
        DateTime lastRefresh,
        TimeSpan interval,
        Func<CancellationToken, Task> refreshAction,
        DateTime now,
        CancellationToken cancellationToken,
        bool isInitialLoad)
    {
        // Skip if interval is zero (disabled)
        if (interval == TimeSpan.Zero)
            return false;

        // For initial load, always refresh if interval is not zero
        // For subsequent loads, check if enough time has passed
        bool shouldRefresh = isInitialLoad || (now - lastRefresh >= interval);

        if (shouldRefresh)
        {
            try
            {
                _logger.LogInformation("Starting {OperationName} refresh", operationName);
                await refreshAction(cancellationToken);
                _logger.LogInformation("Completed {OperationName} refresh", operationName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh {OperationName}", operationName);
                // Continue with other refresh operations even if one fails
                return false;
            }
        }
        return false;
    }
}