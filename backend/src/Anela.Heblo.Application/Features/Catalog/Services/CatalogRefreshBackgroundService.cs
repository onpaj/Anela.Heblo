using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class CatalogRefreshBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatalogRefreshBackgroundService> _logger;
    private readonly DataSourceOptions _options;
    private readonly IBackgroundServiceReadinessTracker _readinessTracker;

    private DateTime _lastTransportRefresh = DateTime.MinValue;
    private DateTime _lastReserveRefresh = DateTime.MinValue;
    private DateTime _lastOrderedRefresh = DateTime.MinValue;
    private DateTime _lastPlannedRefresh = DateTime.MinValue;
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
        IOptions<DataSourceOptions> options,
        IBackgroundServiceReadinessTracker readinessTracker)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
        _readinessTracker = readinessTracker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Catalog Refresh Background Service started");

        // Perform initial load immediately (if intervals are not zero)
        await PerformRefreshCycle(stoppingToken, isInitialLoad: true);

        // Check if all refresh intervals are zero (disabled), if so, report as ready immediately
        if (AreAllRefreshIntervalsDisabled())
        {
            _readinessTracker.ReportInitialLoadCompleted<CatalogRefreshBackgroundService>();
            _logger.LogInformation("All CatalogRefreshBackgroundService refresh intervals are disabled, service is ready");
        }

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

            if (await RefreshIfNeeded(catalogRepository, "Ordered",
                _lastOrderedRefresh, _options.OrderedRefreshInterval,
                async ct => await catalogRepository.RefreshOrderedData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastOrderedRefresh = now;
            }

            if (await RefreshIfNeeded(catalogRepository, "Planned",
                _lastPlannedRefresh, _options.PlannedRefreshInterval,
                async ct => await catalogRepository.RefreshPlannedData(ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastPlannedRefresh = now;
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
                async ct => await catalogRepository.RefreshManufactureDifficultySettingsData(null, ct),
                now, stoppingToken, isInitialLoad))
            {
                _lastManufactureDifficultyRefresh = now;
            }

            // Refresh ManufactureCostData only after all other data sources are loaded
            // Check that all required data sources are loaded
            if (AreAllDataSourcesLoaded(catalogRepository) && !catalogRepository.ChangesPendingForMerge)
            {
                if ((await RefreshIfNeeded(catalogRepository, "Manufacture Cost",
                    _lastManufactureCostRefresh, _options.ManufactureHistoryRefreshInterval,
                    async ct => await ((CatalogRepository)catalogRepository).RefreshManufactureCostData(ct),
                    now, stoppingToken, isInitialLoad)))
                {
                    _lastManufactureCostRefresh = now;
                }
            }

            // Report readiness after initial load completes successfully
            if (isInitialLoad)
            {
                _readinessTracker.ReportInitialLoadCompleted<CatalogRefreshBackgroundService>();
                _logger.LogInformation("CatalogRefreshBackgroundService initial load completed, service is ready");
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

    private bool AreAllRefreshIntervalsDisabled()
    {
        return _options.TransportRefreshInterval == TimeSpan.Zero &&
               _options.ReserveRefreshInterval == TimeSpan.Zero &&
               _options.OrderedRefreshInterval == TimeSpan.Zero &&
               _options.PlannedRefreshInterval == TimeSpan.Zero &&
               _options.SalesRefreshInterval == TimeSpan.Zero &&
               _options.AttributesRefreshInterval == TimeSpan.Zero &&
               _options.ErpStockRefreshInterval == TimeSpan.Zero &&
               _options.EshopStockRefreshInterval == TimeSpan.Zero &&
               _options.PurchaseHistoryRefreshInterval == TimeSpan.Zero &&
               _options.ManufactureHistoryRefreshInterval == TimeSpan.Zero &&
               _options.ConsumedRefreshInterval == TimeSpan.Zero &&
               _options.StockTakingRefreshInterval == TimeSpan.Zero &&
               _options.LotsRefreshInterval == TimeSpan.Zero &&
               _options.EshopPricesRefreshInterval == TimeSpan.Zero &&
               _options.ErpPricesRefreshInterval == TimeSpan.Zero &&
               _options.ManufactureDifficultyRefreshInterval == TimeSpan.Zero;
    }

    private bool AreAllDataSourcesLoaded(ICatalogRepository catalogRepository)
    {
        return catalogRepository.TransportLoadDate != null &&
               catalogRepository.ReserveLoadDate != null &&
               catalogRepository.OrderedLoadDate != null &&
               catalogRepository.PlannedLoadDate != null &&
               catalogRepository.SalesLoadDate != null &&
               catalogRepository.AttributesLoadDate != null &&
               catalogRepository.ErpStockLoadDate != null &&
               catalogRepository.EshopStockLoadDate != null &&
               catalogRepository.PurchaseHistoryLoadDate != null &&
               catalogRepository.ManufactureHistoryLoadDate != null &&
               catalogRepository.ConsumedHistoryLoadDate != null &&
               catalogRepository.StockTakingLoadDate != null &&
               catalogRepository.LotsLoadDate != null &&
               catalogRepository.EshopPricesLoadDate != null &&
               catalogRepository.ErpPricesLoadDate != null &&
               catalogRepository.ManufactureDifficultySettingsLoadDate != null;
    }
}