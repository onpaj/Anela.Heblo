# User Story: Manufacturing Stock Management

## Feature Description
The Manufacturing Stock Management feature provides comprehensive analysis and monitoring of production stock levels with demand forecasting, severity classification, and restocking recommendations. The system integrates with catalog data to track sales velocity, monitors stock health across product families, and provides automated alerts for critical stock situations.

## Business Requirements

### Primary Use Cases
1. **Stock Level Analysis**: Monitor current stock levels across all manufactured products
2. **Demand Forecasting**: Calculate optimal stock days based on sales velocity
3. **Severity Classification**: Automatically classify stock urgency levels
4. **Restocking Recommendations**: Generate production recommendations based on stock analysis
5. **Product Family Management**: Group and analyze products by family and type

### User Stories
- As a production manager, I want to monitor stock levels across product families so I can prioritize production planning
- As a warehouse manager, I want to receive alerts for critical stock situations so I can prevent stockouts
- As a sales analyst, I want to view demand forecasting data so I can understand sales velocity trends
- As an operations manager, I want automated restocking recommendations so I can optimize inventory levels

## Technical Requirements

### Domain Models

#### ManufactureStockAggregate
```csharp
public class ManufactureStockAggregate : AuditedAggregateRoot<int>
{
    // Product Identification
    public string ProductId { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductFamily { get; set; } = "";
    public string ProductType { get; set; } = "";
    public string SizeCode { get; set; } = "";
    
    // Stock Configuration
    public int OptimalStockDaysSetup { get; set; }
    public int StockMinSetup { get; set; }
    public int BatchSize { get; set; }
    
    // Current Stock Levels
    public int OnStockEshop { get; set; }
    public int OnStockTransport { get; set; }
    public int OnStockReserve { get; set; }
    
    // Sales Analytics
    public decimal AmountB2C { get; set; }
    public decimal AmountB2B { get; set; }
    public decimal SalesB2C { get; set; }
    public decimal SalesB2B { get; set; }
    public decimal DailySalesSum { get; set; }
    
    // Computed Properties
    public int OnStockSum => OnStockEshop + OnStockTransport + OnStockReserve;
    public bool IsUnderStocked => OnStockSum < StockMinSetup;
    public bool IsUnderForecasted => OptimalStockDaysForecasted < OptimalStockDaysSetup;
    public bool IsOptimalStockConfigured => OptimalStockDaysSetup > 0;
    
    public double OptimalStockPercentage 
    {
        get
        {
            if (OptimalStockDaysSetup <= 0) return 100;
            var optimalStock = DailySalesSum * OptimalStockDaysSetup;
            return optimalStock > 0 ? (OnStockSum / optimalStock) * 100 : 100;
        }
    }
    
    public double OptimalStockDaysForecasted 
    {
        get
        {
            return DailySalesSum > 0 ? OnStockSum / (double)DailySalesSum : double.MaxValue;
        }
    }
    
    public StockSeverity Severity
    {
        get
        {
            if (OptimalStockDaysForecasted < 8 && IsOptimalStockConfigured)
                return StockSeverity.Critical;
            if ((OptimalStockDaysForecasted < 15 && IsOptimalStockConfigured) || IsUnderStocked)
                return StockSeverity.Major;
            if (IsUnderForecasted)
                return StockSeverity.Minor;
            return StockSeverity.None;
        }
    }
    
    // Business Methods
    public void UpdateStockLevels(int eshop, int transport, int reserve)
    {
        OnStockEshop = eshop;
        OnStockTransport = transport;
        OnStockReserve = reserve;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void UpdateSalesData(decimal b2cAmount, decimal b2bAmount, decimal b2cSales, decimal b2bSales)
    {
        AmountB2C = b2cAmount;
        AmountB2B = b2bAmount;
        SalesB2C = b2cSales;
        SalesB2B = b2bSales;
        DailySalesSum = b2cAmount + b2bAmount;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void UpdateConfiguration(int optimalDays, int minStock, int batchSize)
    {
        OptimalStockDaysSetup = optimalDays;
        StockMinSetup = minStock;
        BatchSize = batchSize;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public RestockingRecommendation GetRestockingRecommendation()
    {
        if (Severity == StockSeverity.Critical)
        {
            var urgentQuantity = Math.Max(BatchSize, (int)(DailySalesSum * OptimalStockDaysSetup));
            return new RestockingRecommendation
            {
                ProductCode = ProductCode,
                Priority = RestockingPriority.Urgent,
                RecommendedQuantity = urgentQuantity,
                Reason = $"Critical stock level - only {OptimalStockDaysForecasted:F1} days remaining",
                TargetDate = DateTime.Today.AddDays(2)
            };
        }
        
        if (Severity == StockSeverity.Major)
        {
            var normalQuantity = BatchSize;
            return new RestockingRecommendation
            {
                ProductCode = ProductCode,
                Priority = RestockingPriority.High,
                RecommendedQuantity = normalQuantity,
                Reason = $"Low stock level - {OptimalStockDaysForecasted:F1} days remaining",
                TargetDate = DateTime.Today.AddDays(7)
            };
        }
        
        if (Severity == StockSeverity.Minor)
        {
            return new RestockingRecommendation
            {
                ProductCode = ProductCode,
                Priority = RestockingPriority.Normal,
                RecommendedQuantity = BatchSize,
                Reason = "Below optimal forecast level",
                TargetDate = DateTime.Today.AddDays(14)
            };
        }
        
        return new RestockingRecommendation
        {
            ProductCode = ProductCode,
            Priority = RestockingPriority.None,
            RecommendedQuantity = 0,
            Reason = "Stock levels adequate",
            TargetDate = null
        };
    }
}

public enum StockSeverity
{
    None,
    Minor,
    Major,
    Critical
}

public enum RestockingPriority
{
    None,
    Normal,
    High,
    Urgent
}
```

#### RestockingRecommendation (Value Object)
```csharp
public class RestockingRecommendation : ValueObject
{
    public string ProductCode { get; set; } = "";
    public RestockingPriority Priority { get; set; }
    public int RecommendedQuantity { get; set; }
    public string Reason { get; set; } = "";
    public DateTime? TargetDate { get; set; }
    public int EstimatedProductionDays { get; set; }
    public decimal EstimatedCost { get; set; }
    
    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return ProductCode;
        yield return Priority;
        yield return RecommendedQuantity;
        yield return Reason;
        yield return TargetDate ?? DateTime.MinValue;
    }
    
    public bool IsActionRequired => Priority != RestockingPriority.None;
    public bool IsUrgent => Priority == RestockingPriority.Urgent;
    public int DaysUntilTarget => TargetDate?.Subtract(DateTime.Today).Days ?? 0;
}
```

#### StockAnalysisSnapshot (Value Object)
```csharp
public class StockAnalysisSnapshot : ValueObject
{
    public DateTime AnalysisDate { get; set; } = DateTime.UtcNow;
    public int TotalProducts { get; set; }
    public int CriticalProducts { get; set; }
    public int MajorProducts { get; set; }
    public int MinorProducts { get; set; }
    public int AdequateProducts { get; set; }
    public decimal TotalStockValue { get; set; }
    public decimal AverageDaysOfStock { get; set; }
    public List<string> ProductFamiliesAnalyzed { get; set; } = new();
    
    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return AnalysisDate;
        yield return TotalProducts;
        yield return CriticalProducts;
        yield return MajorProducts;
        yield return MinorProducts;
        yield return AdequateProducts;
        yield return TotalStockValue;
        yield return AverageDaysOfStock;
    }
    
    public double CriticalPercentage => TotalProducts > 0 ? (CriticalProducts / (double)TotalProducts) * 100 : 0;
    public double HealthyPercentage => TotalProducts > 0 ? (AdequateProducts / (double)TotalProducts) * 100 : 0;
    public bool RequiresAttention => CriticalProducts > 0 || MajorProducts > 5;
}
```

### Application Services

#### IManufactureStockAppService
```csharp
public interface IManufactureStockAppService : IApplicationService
{
    Task<PagedResultDto<ManufactureStockDto>> GetManufactureStockAsync(GetManufactureStockQuery query);
    Task<List<ManufactureStockDto>> GetStockByProductFamilyAsync(string productFamily);
    Task<List<ManufactureStockDto>> GetStockBySeverityAsync(StockSeverity severity);
    Task<StockAnalysisSnapshotDto> GetStockAnalysisAsync(DateTime? analysisDate = null);
    Task<List<RestockingRecommendationDto>> GetRestockingRecommendationsAsync(RestockingPriority? minPriority = null);
    Task<ManufactureStockDto> UpdateStockConfigurationAsync(int stockId, UpdateStockConfigurationDto input);
    Task RefreshStockDataAsync(List<string> productCodes);
    Task<List<ProductFamilyStockSummaryDto>> GetProductFamilySummariesAsync();
    Task<StockTrendAnalysisDto> GetStockTrendsAsync(string productCode, int days = 30);
}
```

#### ManufactureStockAppService Implementation
```csharp
[Authorize]
public class ManufactureStockAppService : ApplicationService, IManufactureStockAppService
{
    private readonly IManufactureStockRepository _stockRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ManufactureStockAppService> _logger;

    public ManufactureStockAppService(
        IManufactureStockRepository stockRepository,
        ICatalogRepository catalogRepository,
        IMemoryCache cache,
        ILogger<ManufactureStockAppService> logger)
    {
        _stockRepository = stockRepository;
        _catalogRepository = catalogRepository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PagedResultDto<ManufactureStockDto>> GetManufactureStockAsync(GetManufactureStockQuery query)
    {
        _logger.LogInformation("Retrieving manufacture stock with query: {@Query}", query);
        
        var specification = CreateStockSpecification(query);
        var totalCount = await _stockRepository.GetCountAsync(specification);
        var stocks = await _stockRepository.GetPagedListAsync(
            specification, 
            query.PageIndex, 
            query.PageSize,
            query.Sorting ?? "Severity desc, OptimalStockDaysForecasted asc");

        var stockDtos = ObjectMapper.Map<List<ManufactureStock>, List<ManufactureStockDto>>(stocks);
        
        return new PagedResultDto<ManufactureStockDto>(totalCount, stockDtos);
    }

    public async Task<List<ManufactureStockDto>> GetStockByProductFamilyAsync(string productFamily)
    {
        var cacheKey = $"stock_family_{productFamily}";
        
        if (_cache.TryGetValue(cacheKey, out List<ManufactureStockDto> cachedResult))
        {
            return cachedResult;
        }

        var stocks = await _stockRepository.GetListAsync(s => s.ProductFamily == productFamily);
        var result = ObjectMapper.Map<List<ManufactureStock>, List<ManufactureStockDto>>(stocks);
        
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(15));
        return result;
    }

    public async Task<List<ManufactureStockDto>> GetStockBySeverityAsync(StockSeverity severity)
    {
        var allStocks = await _stockRepository.GetListAsync();
        var filteredStocks = allStocks.Where(s => s.Severity == severity).ToList();
        
        return ObjectMapper.Map<List<ManufactureStock>, List<ManufactureStockDto>>(filteredStocks);
    }

    public async Task<StockAnalysisSnapshotDto> GetStockAnalysisAsync(DateTime? analysisDate = null)
    {
        var targetDate = analysisDate ?? DateTime.UtcNow;
        var cacheKey = $"stock_analysis_{targetDate:yyyyMMdd}";
        
        if (_cache.TryGetValue(cacheKey, out StockAnalysisSnapshotDto cachedAnalysis))
        {
            return cachedAnalysis;
        }

        var allStocks = await _stockRepository.GetListAsync();
        var snapshot = new StockAnalysisSnapshot
        {
            AnalysisDate = targetDate,
            TotalProducts = allStocks.Count,
            CriticalProducts = allStocks.Count(s => s.Severity == StockSeverity.Critical),
            MajorProducts = allStocks.Count(s => s.Severity == StockSeverity.Major),
            MinorProducts = allStocks.Count(s => s.Severity == StockSeverity.Minor),
            AdequateProducts = allStocks.Count(s => s.Severity == StockSeverity.None),
            TotalStockValue = await CalculateTotalStockValue(allStocks),
            AverageDaysOfStock = allStocks.Where(s => s.OptimalStockDaysForecasted < 1000)
                                        .DefaultIfEmpty()
                                        .Average(s => s?.OptimalStockDaysForecasted ?? 0),
            ProductFamiliesAnalyzed = allStocks.Select(s => s.ProductFamily).Distinct().ToList()
        };

        var result = ObjectMapper.Map<StockAnalysisSnapshot, StockAnalysisSnapshotDto>(snapshot);
        _cache.Set(cacheKey, result, TimeSpan.FromHours(2));
        
        return result;
    }

    public async Task<List<RestockingRecommendationDto>> GetRestockingRecommendationsAsync(RestockingPriority? minPriority = null)
    {
        var allStocks = await _stockRepository.GetListAsync();
        var recommendations = new List<RestockingRecommendation>();

        foreach (var stock in allStocks)
        {
            var recommendation = stock.GetRestockingRecommendation();
            if (minPriority == null || recommendation.Priority >= minPriority)
            {
                recommendations.Add(recommendation);
            }
        }

        var sortedRecommendations = recommendations
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.DaysUntilTarget)
            .ToList();

        return ObjectMapper.Map<List<RestockingRecommendation>, List<RestockingRecommendationDto>>(sortedRecommendations);
    }

    public async Task<ManufactureStockDto> UpdateStockConfigurationAsync(int stockId, UpdateStockConfigurationDto input)
    {
        var stock = await _stockRepository.GetAsync(stockId);
        
        stock.UpdateConfiguration(
            input.OptimalStockDays,
            input.MinimumStock,
            input.BatchSize);

        await _stockRepository.UpdateAsync(stock);
        
        _logger.LogInformation("Updated stock configuration for product {ProductCode}", stock.ProductCode);
        
        // Clear cache
        _cache.Remove($"stock_family_{stock.ProductFamily}");
        
        return ObjectMapper.Map<ManufactureStock, ManufactureStockDto>(stock);
    }

    public async Task RefreshStockDataAsync(List<string> productCodes)
    {
        _logger.LogInformation("Refreshing stock data for {Count} products", productCodes.Count);
        
        foreach (var productCode in productCodes)
        {
            try
            {
                var catalogData = await _catalogRepository.GetProductStockAsync(productCode);
                var salesData = await _catalogRepository.GetProductSalesAsync(productCode, DateTime.Today.AddDays(-30), DateTime.Today);
                
                var stock = await _stockRepository.GetByProductCodeAsync(productCode);
                if (stock != null)
                {
                    stock.UpdateStockLevels(
                        catalogData.EshopStock,
                        catalogData.TransportStock,
                        catalogData.ReserveStock);
                    
                    stock.UpdateSalesData(
                        salesData.B2CAmount,
                        salesData.B2BAmount,
                        salesData.B2CSales,
                        salesData.B2BSales);
                    
                    await _stockRepository.UpdateAsync(stock);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh stock data for product {ProductCode}", productCode);
            }
        }
        
        // Clear relevant caches
        _cache.Remove("stock_analysis");
    }

    private async Task<decimal> CalculateTotalStockValue(List<ManufactureStock> stocks)
    {
        decimal totalValue = 0;
        
        foreach (var stock in stocks)
        {
            try
            {
                var productPrice = await _catalogRepository.GetProductPriceAsync(stock.ProductCode);
                totalValue += stock.OnStockSum * productPrice;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not get price for product {ProductCode}", stock.ProductCode);
            }
        }
        
        return totalValue;
    }

    private ISpecification<ManufactureStock> CreateStockSpecification(GetManufactureStockQuery query)
    {
        var spec = new ManufactureStockSpecification();
        
        if (!string.IsNullOrEmpty(query.ProductFamily))
        {
            spec = spec.And(new ProductFamilySpecification(query.ProductFamily));
        }
        
        if (!string.IsNullOrEmpty(query.ProductType))
        {
            spec = spec.And(new ProductTypeSpecification(query.ProductType));
        }
        
        if (query.Severity.HasValue)
        {
            spec = spec.And(new StockSeveritySpecification(query.Severity.Value));
        }
        
        if (query.UnderStockedOnly)
        {
            spec = spec.And(new UnderStockedSpecification());
        }
        
        if (!string.IsNullOrEmpty(query.SearchText))
        {
            spec = spec.And(new ProductSearchSpecification(query.SearchText));
        }
        
        return spec;
    }
}
```

### Repository Interfaces

#### IManufactureStockRepository
```csharp
public interface IManufactureStockRepository : IRepository<ManufactureStock, int>
{
    Task<ManufactureStock?> GetByProductCodeAsync(string productCode);
    Task<List<ManufactureStock>> GetByProductFamilyAsync(string productFamily);
    Task<List<ManufactureStock>> GetBySeverityAsync(StockSeverity severity);
    Task<List<ManufactureStock>> GetUnderStockedAsync();
    Task<List<ManufactureStock>> GetCriticalStockAsync();
    Task<List<string>> GetDistinctProductFamiliesAsync();
    Task<PagedResultDto<ManufactureStock>> GetPagedWithSalesDataAsync(
        ISpecification<ManufactureStock> specification,
        int pageIndex,
        int pageSize,
        string sorting = null);
    Task BulkUpdateStockLevelsAsync(Dictionary<string, StockUpdateData> updates);
    Task<Dictionary<string, decimal>> GetAverageSalesVelocityByFamilyAsync(int days = 30);
}
```

### DTOs

#### ManufactureStockDto
```csharp
public class ManufactureStockDto
{
    public int Id { get; set; }
    public string ProductId { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductFamily { get; set; } = "";
    public string ProductType { get; set; } = "";
    public string SizeCode { get; set; } = "";
    
    public int OptimalStockDaysSetup { get; set; }
    public int StockMinSetup { get; set; }
    public int BatchSize { get; set; }
    
    public int OnStockEshop { get; set; }
    public int OnStockTransport { get; set; }
    public int OnStockReserve { get; set; }
    public int OnStockSum { get; set; }
    
    public decimal AmountB2C { get; set; }
    public decimal AmountB2B { get; set; }
    public decimal SalesB2C { get; set; }
    public decimal SalesB2B { get; set; }
    public decimal DailySalesSum { get; set; }
    
    public bool IsUnderStocked { get; set; }
    public bool IsUnderForecasted { get; set; }
    public double OptimalStockPercentage { get; set; }
    public double OptimalStockDaysForecasted { get; set; }
    public StockSeverity Severity { get; set; }
    
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}
```

#### GetManufactureStockQuery
```csharp
public class GetManufactureStockQuery : PagedAndSortedRequestDto
{
    public string? ProductFamily { get; set; }
    public string? ProductType { get; set; }
    public StockSeverity? Severity { get; set; }
    public bool UnderStockedOnly { get; set; }
    public string? SearchText { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
```

### Caching Strategy

#### Cache Keys and Patterns
```csharp
public static class ManufactureStockCacheKeys
{
    public const string STOCK_FAMILY_PREFIX = "manufacture_stock_family_";
    public const string STOCK_ANALYSIS_PREFIX = "manufacture_stock_analysis_";
    public const string PRODUCT_FAMILIES = "manufacture_product_families";
    public const string CRITICAL_STOCKS = "manufacture_critical_stocks";
    
    public static string ForFamily(string family) => $"{STOCK_FAMILY_PREFIX}{family}";
    public static string ForAnalysis(DateTime date) => $"{STOCK_ANALYSIS_PREFIX}{date:yyyyMMdd}";
}
```

#### Cache Management
- **Stock by Family**: 15 minutes TTL
- **Analysis Snapshots**: 2 hours TTL  
- **Critical Stocks**: 5 minutes TTL
- **Product Families**: 1 hour TTL

### Performance Requirements

#### Response Time Targets
- Stock listing queries: < 2 seconds
- Analysis calculations: < 5 seconds
- Cache-hit responses: < 500ms
- Bulk operations: < 10 seconds

#### Scalability
- Support 10,000+ products
- Handle 100+ concurrent users
- Process hourly stock updates
- Maintain sub-second cache performance

### Happy Day Scenarios

#### Scenario 1: Production Manager Reviews Critical Stock
```
1. Manager accesses stock monitoring dashboard
2. System displays current stock levels with severity indicators
3. Critical items are highlighted at top of list
4. Manager clicks on critical item for details
5. System shows forecasted days remaining and recommended action
6. Manager approves restocking recommendation
7. System creates production order with recommended quantity
```

#### Scenario 2: Automated Stock Analysis
```
1. Scheduled job runs daily stock analysis
2. System calculates severity levels for all products
3. Critical stock alerts are generated
4. Email notifications sent to production team
5. Dashboard updated with latest analysis
6. Restocking recommendations prepared
```

#### Scenario 3: Stock Configuration Update
```
1. Manager identifies product needing configuration change
2. Updates optimal stock days and minimum levels
3. System recalculates severity and recommendations
4. Changes are reflected immediately in dashboard
5. Historical tracking shows configuration change
```

### Error Scenarios

#### Scenario 1: Missing Catalog Data
```
User: Views stock analysis for product without catalog data
System: Shows warning message "Catalog data not available"
Action: Continue with available data, flag for manual review
```

#### Scenario 2: Sales Data Calculation Error
```
User: Accesses product with corrupted sales history
System: Shows error "Unable to calculate sales velocity"
Action: Use configured defaults, log for investigation
```

#### Scenario 3: Cache Performance Degradation
```
User: Experiences slow dashboard loading
System: Falls back to direct database queries
Action: Rebuild cache, investigate performance issues
```