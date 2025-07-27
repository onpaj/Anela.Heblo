# User Story: Catalog Data Aggregation & Management

## Overview
As a **business user**, I want to access a unified product catalog that aggregates information from multiple systems, so that I can view comprehensive product information without knowing which system stores what data.

## Acceptance Criteria

### Primary Flow
**Given** that multiple external systems contain product data (ERP stock, E-shop stock, sales history, etc.)  
**When** I request product information via `ICatalogRepository`  
**Then** the system should return a unified `CatalogAggregate` containing all relevant product data

### Business Rules
1. **Product Identification**: Products are uniquely identified by `ProductCode` (string)
2. **Stock Aggregation**: Available stock = Primary stock source + Transport stock
3. **Code Derivation**: 
   - Product family = first 6 characters of product code
   - Product type = first 3 characters of product code
4. **Data Sources**: Must support ERP, E-shop, Transport, Sales, Purchase, and Consumed materials data
5. **Primary Stock Source**: Configurable preference for ERP vs E-shop as authoritative source

## Technical Requirements

### Domain Model

#### CatalogAggregate Entity
```csharp
public class CatalogAggregate : Entity<string>
{
    // Identity
    public string ProductCode { get; private set; } // Primary key
    public string ProductName { get; set; }
    public string ErpId { get; set; }
    public ProductType Type { get; set; }
    
    // Physical Properties
    public string Location { get; set; }
    public decimal Volume { get; set; }
    public decimal Weight { get; set; }
    public bool HasExpiration { get; set; }
    public bool HasLots { get; set; }
    
    // Stock Information
    public StockData Stock { get; set; }
    public CatalogProperties Properties { get; set; }
    
    // Historical Data Collections
    public List<CatalogSales> SalesHistory { get; set; }
    public List<CatalogConsumedHistory> ConsumedHistory { get; set; }
    public List<CatalogPurchaseHistory> PurchaseHistory { get; set; }
    public List<StockTakingResult> StockTakingHistory { get; set; }
    public List<Supplier> Suppliers { get; set; }
    
    // Computed Properties
    public string ProductFamily => ProductCode?.Length >= 6 ? ProductCode[..6] : ProductCode;
    public string ProductTypeCode => ProductCode?.Length >= 3 ? ProductCode[..3] : ProductCode;
    public string ProductSize => ProductCode?.Length > 6 ? ProductCode[6..] : "";
    public bool IsUnderStocked => Stock != null && Properties != null && 
                                 Stock.Available < Properties.StockMinSetup;
    public bool IsInSeason => Properties?.SeasonMonths?.Contains(DateTime.Now.Month) == true;
    public Supplier PrimarySupplier => Suppliers?.FirstOrDefault(s => s.IsPrimary);
}
```

#### StockData Value Object
```csharp
public class StockData
{
    public decimal Eshop { get; set; }
    public decimal Erp { get; set; }
    public decimal Transport { get; set; }
    public decimal Reserve { get; set; }
    public StockSource PrimaryStockSource { get; set; }
    public List<CatalogLot> Lots { get; set; }
    
    public decimal Available => 
        (PrimaryStockSource == StockSource.ERP ? Erp : Eshop) + Transport;
}
```

#### CatalogProperties Value Object
```csharp
public class CatalogProperties
{
    public int OptimalStockDaysSetup { get; set; }
    public decimal StockMinSetup { get; set; }
    public decimal BatchSize { get; set; }
    public int[] SeasonMonths { get; set; }
}
```

### Repository Interface
```csharp
public interface ICatalogRepository : IReadOnlyRepository<CatalogAggregate, string>
{
    Task<CatalogAggregate> GetAsync(string productCode, bool includeDetails = true);
    Task<List<CatalogAggregate>> GetListAsync(ISpecification<CatalogAggregate> specification = null);
    Task UpdateStockAsync(string productCode, StockData newStockData);
    Task RefreshAsync();
}
```

### Repository Implementation
```csharp
public class CatalogRepository : ICatalogRepository
{
    private readonly IMemoryCache _cache;
    private readonly IErpStockClient _erpStockClient;
    private readonly IEshopStockClient _eshopStockClient;
    private readonly ICatalogSalesClient _salesClient;
    // ... other clients
    
    public async Task<CatalogAggregate> GetAsync(string productCode, bool includeDetails = true)
    {
        var cacheKey = $"catalog_{productCode}";
        if (_cache.TryGetValue(cacheKey, out CatalogAggregate cached))
            return cached;
            
        var aggregate = await BuildAggregateAsync(productCode, includeDetails);
        _cache.Set(cacheKey, aggregate, TimeSpan.FromMinutes(30));
        return aggregate;
    }
    
    private async Task<CatalogAggregate> BuildAggregateAsync(string productCode, bool includeDetails)
    {
        var aggregate = new CatalogAggregate(productCode);
        
        // Merge data from all sources
        await MergeErpDataAsync(aggregate);
        await MergeEshopDataAsync(aggregate);
        await MergeSalesDataAsync(aggregate);
        await MergePurchaseDataAsync(aggregate);
        // ... other data sources
        
        return aggregate;
    }
}
```

### Data Source Integration

#### External Client Interfaces
```csharp
public interface IErpStockClient
{
    Task<List<ErpStockData>> GetStockAsync();
}

public interface IEshopStockClient
{
    Task<List<EshopStockData>> GetStockAsync();
}

public interface ICatalogSalesClient
{
    Task<List<CatalogSales>> GetSalesAsync(DateTime dateFrom, DateTime dateTo);
}
```

## Error Handling

### Error Scenarios
1. **External System Unavailable**
   - Log warning and continue with available data
   - Cache last known good data
   - Return partial aggregate with available information

2. **Invalid Product Code**
   - Throw `EntityNotFoundException`
   - Log attempt for audit purposes

3. **Cache Miss During High Load**
   - Implement circuit breaker pattern
   - Return cached "loading" placeholder if available

### Error Handling Implementation
```csharp
public async Task<CatalogAggregate> GetAsync(string productCode, bool includeDetails = true)
{
    try
    {
        // Implementation here
    }
    catch (ExternalSystemException ex)
    {
        _logger.LogWarning("External system unavailable: {Error}", ex.Message);
        return await GetFromFallbackCacheAsync(productCode);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to build catalog aggregate for {ProductCode}", productCode);
        throw;
    }
}
```

## Caching Strategy

### Cache Configuration
```csharp
public class CatalogRepositoryOptions
{
    public TimeSpan DefaultCacheExpiration { get; set; } = TimeSpan.FromMinutes(30);
    public int MaxCacheSize { get; set; } = 10000;
    public bool EnableDistributedCache { get; set; } = false;
}
```

### Cache Implementation
- **Primary Cache**: In-memory cache using `IMemoryCache`
- **Cache Key Strategy**: `catalog_{productCode}`
- **Expiration**: 30 minutes default, configurable
- **Invalidation**: Manual invalidation on stock updates

## Data Consistency

### Eventual Consistency Model
- Data from external systems may be slightly stale
- Background refresh service ensures data freshness
- Cache invalidation on explicit updates

### Conflict Resolution
- Primary stock source (ERP vs E-shop) determines authoritative data
- Last-write-wins for non-critical properties
- Business rules applied consistently during aggregation

## Performance Considerations

### Optimization Strategies
1. **Lazy Loading**: Load details only when requested
2. **Batch Processing**: Aggregate multiple products efficiently
3. **Parallel Data Fetching**: Fetch from multiple sources concurrently
4. **Smart Caching**: Cache at appropriate granularity

### Memory Management
```csharp
// Configure memory cache with size limits
services.Configure<MemoryCacheOptions>(options =>
{
    options.SizeLimit = 10000; // Max number of cached items
});
```

## Security Requirements

### Access Control
- No specific authorization required (public catalog data)
- Audit logging for sensitive operations
- Rate limiting for external API calls

### Data Protection
- No PII in catalog data
- Standard encryption for data in transit
- Audit trail for all stock modifications

## Integration Requirements

### External System Dependencies
1. **ERP System**: Primary source for product master data and stock
2. **E-shop System**: Online inventory and product information
3. **Transport System**: Goods in transit tracking
4. **Sales System**: Historical sales and revenue data
5. **Manufacturing System**: Consumption and production data

### Data Format Requirements
- All external systems must provide JSON or XML APIs
- Date formats must be ISO 8601 compliant
- Decimal precision: 2 decimal places for quantities, 4 for prices

## Monitoring and Observability

### Metrics to Track
- Cache hit/miss ratios
- External system response times
- Data aggregation success rates
- Memory usage for cache

### Logging Requirements
```csharp
public class CatalogRepository
{
    private readonly ILogger<CatalogRepository> _logger;
    
    private async Task MergeErpDataAsync(CatalogAggregate aggregate)
    {
        _logger.LogInformation("Starting ERP data merge for {ProductCode}", aggregate.Id);
        // Implementation
        _logger.LogInformation("Completed ERP data merge for {ProductCode}", aggregate.Id);
    }
}
```

## Testing Strategy

### Unit Test Coverage
- Aggregate construction and computed properties
- Repository caching behavior
- Error handling scenarios
- Data merging logic

### Integration Test Coverage
- External system integration
- End-to-end data flow
- Cache invalidation scenarios

### Performance Test Coverage
- Load testing with large catalogs
- Memory usage under stress
- Concurrent access patterns