# User Story: Background Data Refresh Service

## Overview
As a **system administrator**, I want the catalog data to be automatically refreshed from external systems on configurable intervals, so that users always have access to current product information without manual intervention.

## Acceptance Criteria

### Primary Flow
**Given** that the system has multiple external data sources (ERP, E-shop, Sales, etc.)  
**When** the background service runs  
**Then** it should refresh data from all configured sources according to their individual refresh intervals

### Business Rules
1. **Independent Refresh Cycles**: Each data type has its own configurable refresh interval
2. **Continuous Operation**: Service runs continuously as a background hosted service
3. **Graceful Error Handling**: Individual source failures don't stop other refreshes
4. **Historical Data Retention**: Different retention periods for different data types
5. **Performance Optimization**: Parallel processing of different data sources

## Technical Requirements

### Background Service Implementation

#### CatalogDataRefresher Service
```csharp
public class CatalogDataRefresher : BackgroundService
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<CatalogDataRefresher> _logger;
    private readonly CatalogRepositoryOptions _options;
    
    public CatalogDataRefresher(
        ICatalogRepository catalogRepository,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<CatalogDataRefresher> logger,
        IOptions<CatalogRepositoryOptions> options)
    {
        _catalogRepository = catalogRepository;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _options = options.Value;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshTasks = new[]
        {
            RefreshErpStockAsync(stoppingToken),
            RefreshEshopStockAsync(stoppingToken),
            RefreshSalesDataAsync(stoppingToken),
            RefreshPurchaseHistoryAsync(stoppingToken),
            RefreshConsumedMaterialsAsync(stoppingToken),
            RefreshTransportDataAsync(stoppingToken),
            RefreshAttributesAsync(stoppingToken),
            RefreshLotsAsync(stoppingToken),
            RefreshStockTakingAsync(stoppingToken),
            RefreshSuppliersAsync(stoppingToken)
        };
        
        await Task.WhenAll(refreshTasks);
    }
    
    private async Task RefreshErpStockAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting ERP stock refresh");
                
                using var scope = _serviceScopeFactory.CreateScope();
                var erpClient = scope.ServiceProvider.GetRequiredService<IErpStockClient>();
                
                var stockData = await erpClient.GetStockAsync();
                await _catalogRepository.UpdateErpStockAsync(stockData);
                
                _logger.LogInformation("Completed ERP stock refresh. Updated {Count} products", 
                                     stockData.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing ERP stock data");
            }
            
            await Task.Delay(_options.ErpStockRefreshInterval, cancellationToken);
        }
    }
    
    private async Task RefreshSalesDataAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting sales data refresh");
                
                using var scope = _serviceScopeFactory.CreateScope();
                var salesClient = scope.ServiceProvider.GetRequiredService<ICatalogSalesClient>();
                
                var dateFrom = DateTime.Now.AddDays(-_options.SalesHistoryRetentionDays);
                var dateTo = DateTime.Now;
                
                var salesData = await salesClient.GetSalesAsync(dateFrom, dateTo);
                await _catalogRepository.UpdateSalesDataAsync(salesData);
                
                _logger.LogInformation("Completed sales data refresh. Updated {Count} records", 
                                     salesData.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing sales data");
            }
            
            await Task.Delay(_options.SalesDataRefreshInterval, cancellationToken);
        }
    }
    
    // Additional refresh methods follow same pattern...
}
```

### Configuration Options

#### CatalogRepositoryOptions
```csharp
public class CatalogRepositoryOptions
{
    // Refresh Intervals
    public TimeSpan ErpStockRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan EshopStockRefreshInterval { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan SalesDataRefreshInterval { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan PurchaseHistoryRefreshInterval { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan ConsumedMaterialsRefreshInterval { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan TransportDataRefreshInterval { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan AttributesRefreshInterval { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan LotsRefreshInterval { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan StockTakingRefreshInterval { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan SuppliersRefreshInterval { get; set; } = TimeSpan.FromMinutes(120);
    
    // Data Retention Settings
    public int SalesHistoryRetentionDays { get; set; } = 365;
    public int PurchaseHistoryRetentionDays { get; set; } = 365;
    public int ConsumedMaterialsRetentionDays { get; set; } = 720;
    
    // Performance Settings
    public int MaxConcurrentRefreshes { get; set; } = 3;
    public TimeSpan RefreshTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public bool EnableMetrics { get; set; } = true;
}
```

### Extended Repository Interface

#### Additional Repository Methods
```csharp
public interface ICatalogRepository : IReadOnlyRepository<CatalogAggregate, string>
{
    // Existing methods...
    
    // Bulk refresh methods for background service
    Task UpdateErpStockAsync(List<ErpStockData> stockData);
    Task UpdateEshopStockAsync(List<EshopStockData> stockData);
    Task UpdateSalesDataAsync(List<CatalogSales> salesData);
    Task UpdatePurchaseHistoryAsync(List<CatalogPurchaseHistory> purchaseHistory);
    Task UpdateConsumedMaterialsAsync(List<CatalogConsumedHistory> consumedHistory);
    Task UpdateTransportDataAsync(List<TransportBoxData> transportData);
    Task UpdateAttributesAsync(List<CatalogAttributes> attributes);
    Task UpdateLotsAsync(List<CatalogLot> lots);
    Task UpdateStockTakingAsync(List<StockTakingResult> stockTakingResults);
    Task UpdateSuppliersAsync(List<Supplier> suppliers);
    
    // Cache management
    Task InvalidateCacheAsync();
    Task InvalidateCacheAsync(string productCode);
    Task<int> GetCacheSize();
}
```

### Service Registration

#### Dependency Injection Setup
```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogServices(this IServiceCollection services, 
                                                       IConfiguration configuration)
    {
        // Configure options
        services.Configure<CatalogRepositoryOptions>(
            configuration.GetSection("CatalogRepository"));
        
        // Register repository
        services.AddSingleton<ICatalogRepository, CatalogRepository>();
        
        // Register background service
        services.AddHostedService<CatalogDataRefresher>();
        
        // Register external clients
        services.AddTransient<IErpStockClient, ErpStockClient>();
        services.AddTransient<IEshopStockClient, EshopStockClient>();
        services.AddTransient<ICatalogSalesClient, CatalogSalesClient>();
        // ... other clients
        
        // Add memory cache
        services.AddMemoryCache();
        
        return services;
    }
}
```

## Error Handling and Resilience

### Error Handling Strategy
```csharp
private async Task<bool> TryRefreshWithRetryAsync<T>(
    Func<Task<List<T>>> refreshFunc,
    Func<List<T>, Task> updateFunc,
    string operationName,
    CancellationToken cancellationToken)
{
    var retryCount = 0;
    const int maxRetries = 3;
    
    while (retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
    {
        try
        {
            var data = await refreshFunc();
            await updateFunc(data);
            
            if (_options.EnableMetrics)
            {
                RecordSuccessMetric(operationName);
            }
            
            return true;
        }
        catch (Exception ex) when (retryCount < maxRetries - 1)
        {
            retryCount++;
            _logger.LogWarning(ex, "Retry {Retry}/{MaxRetries} for {Operation}", 
                             retryCount, maxRetries, operationName);
            
            var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)); // Exponential backoff
            await Task.Delay(delay, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh {Operation} after {MaxRetries} retries", 
                           operationName, maxRetries);
            
            if (_options.EnableMetrics)
            {
                RecordFailureMetric(operationName);
            }
            
            return false;
        }
    }
    
    return false;
}
```

### Circuit Breaker Pattern
```csharp
public class RefreshCircuitBreaker
{
    private readonly TimeSpan _openDuration;
    private readonly int _failureThreshold;
    private int _failureCount;
    private DateTime _lastFailureTime;
    private CircuitState _state = CircuitState.Closed;
    
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        if (_state == CircuitState.Open)
        {
            if (DateTime.UtcNow - _lastFailureTime > _openDuration)
            {
                _state = CircuitState.HalfOpen;
            }
            else
            {
                throw new CircuitBreakerOpenException();
            }
        }
        
        try
        {
            var result = await operation();
            OnSuccess();
            return result;
        }
        catch (Exception)
        {
            OnFailure();
            throw;
        }
    }
    
    private void OnSuccess()
    {
        _failureCount = 0;
        _state = CircuitState.Closed;
    }
    
    private void OnFailure()
    {
        _failureCount++;
        _lastFailureTime = DateTime.UtcNow;
        
        if (_failureCount >= _failureThreshold)
        {
            _state = CircuitState.Open;
        }
    }
}

public enum CircuitState { Closed, Open, HalfOpen }
```

## Performance Optimizations

### Bulk Update Implementation
```csharp
public async Task UpdateErpStockAsync(List<ErpStockData> stockData)
{
    var updateTasks = stockData.GroupBy(s => s.ProductCode[..3]) // Group by product type
                              .Select(group => UpdateErpStockGroupAsync(group.ToList()))
                              .ToList();
    
    await Task.WhenAll(updateTasks);
    
    // Invalidate cache for updated products
    foreach (var stock in stockData)
    {
        await InvalidateCacheAsync(stock.ProductCode);
    }
}

private async Task UpdateErpStockGroupAsync(List<ErpStockData> stockGroup)
{
    foreach (var stock in stockGroup)
    {
        var cacheKey = $"catalog_{stock.ProductCode}";
        if (_cache.TryGetValue(cacheKey, out CatalogAggregate aggregate))
        {
            // Update cached aggregate
            aggregate.Stock.Erp = stock.Stock;
            aggregate.Location = stock.Location;
            // ... update other properties
        }
    }
}
```

### Memory Management
```csharp
public class CatalogDataRefresher : BackgroundService, IDisposable
{
    private readonly SemaphoreSlim _refreshSemaphore;
    private readonly Timer _cleanupTimer;
    
    public CatalogDataRefresher(/* dependencies */)
    {
        _refreshSemaphore = new SemaphoreSlim(_options.MaxConcurrentRefreshes);
        _cleanupTimer = new Timer(CleanupExpiredCache, null, 
                                TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }
    
    private async void CleanupExpiredCache(object state)
    {
        try
        {
            await _catalogRepository.CleanupExpiredCacheAsync();
            GC.Collect(); // Force garbage collection
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache cleanup");
        }
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshSemaphore?.Dispose();
            _cleanupTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
```

## Monitoring and Metrics

### Health Checks
```csharp
public class CatalogRefreshHealthCheck : IHealthCheck
{
    private readonly ICatalogRepository _repository;
    private readonly ILogger<CatalogRefreshHealthCheck> _logger;
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var lastRefreshTime = await _repository.GetLastRefreshTimeAsync();
            var timeSinceRefresh = DateTime.UtcNow - lastRefreshTime;
            
            if (timeSinceRefresh > TimeSpan.FromMinutes(30))
            {
                return HealthCheckResult.Degraded(
                    $"Last refresh was {timeSinceRefresh.TotalMinutes:F1} minutes ago");
            }
            
            return HealthCheckResult.Healthy("Catalog refresh is running normally");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Catalog refresh health check failed", ex);
        }
    }
}
```

### Metrics Collection
```csharp
public interface ICatalogMetrics
{
    void RecordRefreshDuration(string dataType, TimeSpan duration);
    void RecordRefreshSuccess(string dataType);
    void RecordRefreshFailure(string dataType, string errorType);
    void RecordCacheHitRate(double hitRate);
}

public class CatalogMetrics : ICatalogMetrics
{
    private readonly IMetricsLogger _metricsLogger;
    
    public void RecordRefreshDuration(string dataType, TimeSpan duration)
    {
        _metricsLogger.LogMetric("catalog.refresh.duration", 
                               duration.TotalMilliseconds,
                               new Dictionary<string, string> { ["data_type"] = dataType });
    }
    
    public void RecordRefreshSuccess(string dataType)
    {
        _metricsLogger.LogMetric("catalog.refresh.success", 1,
                               new Dictionary<string, string> { ["data_type"] = dataType });
    }
    
    public void RecordRefreshFailure(string dataType, string errorType)
    {
        _metricsLogger.LogMetric("catalog.refresh.failure", 1,
                               new Dictionary<string, string> 
                               { 
                                   ["data_type"] = dataType,
                                   ["error_type"] = errorType 
                               });
    }
}
```

## Configuration Management

### appsettings.json Configuration
```json
{
  "CatalogRepository": {
    "ErpStockRefreshInterval": "00:05:00",
    "EshopStockRefreshInterval": "00:10:00", 
    "SalesDataRefreshInterval": "00:30:00",
    "PurchaseHistoryRefreshInterval": "01:00:00",
    "ConsumedMaterialsRefreshInterval": "00:30:00",
    "TransportDataRefreshInterval": "00:15:00",
    "AttributesRefreshInterval": "01:00:00",
    "LotsRefreshInterval": "00:15:00",
    "StockTakingRefreshInterval": "00:10:00",
    "SuppliersRefreshInterval": "02:00:00",
    "SalesHistoryRetentionDays": 365,
    "PurchaseHistoryRetentionDays": 365,
    "ConsumedMaterialsRetentionDays": 720,
    "MaxConcurrentRefreshes": 3,
    "RefreshTimeout": "00:10:00",
    "EnableMetrics": true
  }
}
```

### Environment-Specific Overrides
```json
{
  "CatalogRepository": {
    "ErpStockRefreshInterval": "00:01:00",  // Faster refresh in development
    "EnableMetrics": false                   // Disable metrics in development
  }
}
```

## Security and Access Control

### Service Account Security
- Background service runs under system account
- External API credentials stored in secure configuration
- No user-specific data access requirements
- Network security for external system connections

### Audit and Compliance
- All refresh operations logged with timestamps
- Success/failure tracking for compliance reporting
- Data lineage tracking for audit purposes
- Retention policy compliance for historical data

## Business Continuity

### Failover Scenarios
1. **Primary ERP Unavailable**: Continue with cached data, alert administrators
2. **Network Connectivity Issues**: Implement exponential backoff and retry
3. **Memory Pressure**: Reduce cache size, prioritize critical data sources
4. **Service Restart**: Graceful shutdown with completion of in-progress refreshes

### Data Recovery
- Last-known-good cache preservation
- Manual refresh triggers for recovery scenarios
- Incremental refresh capability for large data sets
- Backup data source configuration for critical systems