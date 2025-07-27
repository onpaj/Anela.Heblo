# User Stories: Remaining Catalog Domain Features

## 4. Stock Taking Operations

### Overview
As a **warehouse manager**, I want to perform inventory counts and adjust stock levels across ERP and E-shop systems, so that inventory records remain accurate and synchronized.

### Key Requirements
- Support for both ERP and E-shop stock taking
- Lot-based inventory tracking with expiration dates
- Soft/dry-run mode for validation before committing
- Audit trail for all stock adjustments
- Real-time synchronization across systems

### Implementation Approach
```csharp
public interface IStockTakingService
{
    Task<StockTakingResult> SubmitErpStockTakingAsync(ErpStockTakingRequest request);
    Task<StockTakingResult> SubmitEshopStockTakingAsync(EshopStockTakingRequest request);
    Task<List<StockTakingResult>> GetStockTakingHistoryAsync(string productCode);
}
```

---

## 5. Multi-Source Stock Management

### Overview
As a **inventory analyst**, I want to view unified stock levels across ERP, E-shop, transport, and reserve locations, so that I can make informed decisions about stock allocation and availability.

### Key Requirements
- Real-time aggregation from multiple stock sources
- Configurable primary stock source (ERP vs E-shop)
- Available stock calculation: Primary + Transport
- Lot tracking with expiration management
- Stock movement audit trail

### Implementation Approach
```csharp
public class StockData
{
    public decimal Available => (PrimaryStockSource == StockSource.ERP ? Erp : Eshop) + Transport;
    public StockSource PrimaryStockSource { get; set; }
    public List<CatalogLot> Lots { get; set; }
}
```

---

## 6. Product Attributes & Configuration Management

### Overview
As a **product manager**, I want to configure business-critical product parameters like stock minimums, optimal levels, and batch sizes, so that the system can provide accurate planning and alerts.

### Key Requirements
- Optimal stock days configuration for demand planning
- Minimum stock thresholds for reorder alerts
- Batch sizes for manufacturing optimization
- Seasonal month configuration for demand patterns
- Real-time attribute synchronization

### Implementation Approach
```csharp
public class CatalogProperties
{
    public int OptimalStockDaysSetup { get; set; }
    public decimal StockMinSetup { get; set; }
    public decimal BatchSize { get; set; }
    public int[] SeasonMonths { get; set; }
}
```

---

## 7. Sales History Analytics

### Overview
As a **sales analyst**, I want to access comprehensive sales data with B2B/B2C segmentation, so that I can analyze trends, forecast demand, and optimize inventory planning.

### Key Requirements
- B2B and B2C sales separation
- Date range filtering for flexible analysis
- Daily sales averaging for trend analysis
- Revenue and quantity tracking
- Historical data retention (365 days)

### Implementation Approach
```csharp
public class CatalogSales
{
    public string Period { get; set; } // Monthly periods
    public decimal B2BSold { get; set; }
    public decimal B2CSold { get; set; }
    public int DaysInPeriod { get; set; }
    public decimal DailySalesAverage => (B2BSold + B2CSold) / DaysInPeriod;
}
```

---

## 8. Material Consumption Tracking

### Overview
As a **manufacturing planner**, I want to track material consumption for cost analysis and inventory planning, so that I can optimize production workflows and material procurement.

### Key Requirements
- Date-based consumption tracking
- Historical consumption analysis (720 days retention)
- Integration with manufacturing systems
- Quantity-based consumption records
- Planning analytics for future needs

### Implementation Approach
```csharp
public class CatalogConsumedHistory
{
    public string Period { get; set; }
    public decimal MaterialsConsumed { get; set; }
    public DateTime ConsumptionDate { get; set; }
}
```

---

## 9. Lot & Expiration Management

### Overview
As a **quality manager**, I want to manage product lots with expiration dates, so that I can ensure product traceability, FIFO inventory management, and regulatory compliance.

### Key Requirements
- Lot-based inventory tracking
- Expiration date management
- FIFO (First In, First Out) principles
- Traceability for regulated products
- Integration with quality control processes

### Implementation Approach
```csharp
public class CatalogLot
{
    public string Lot { get; set; }
    public decimal Amount { get; set; }
    public DateTime? Expiration { get; set; }
    public bool IsExpired => Expiration.HasValue && Expiration.Value < DateTime.Now;
}
```

---

## Common Technical Patterns

### Repository Pattern
All features use the unified `ICatalogRepository` interface:
```csharp
public interface ICatalogRepository : IReadOnlyRepository<CatalogAggregate, string>
{
    Task<CatalogAggregate> GetAsync(string productCode, bool includeDetails = true);
    Task<List<CatalogAggregate>> GetListAsync(ISpecification<CatalogAggregate> specification = null);
    Task UpdateStockAsync(string productCode, StockData newStockData);
    Task RefreshAsync();
}
```

### Specification Pattern
Business logic filtering using specifications:
```csharp
public class ProductTypeSpecification : Specification<CatalogAggregate>
{
    public override Expression<Func<CatalogAggregate, bool>> ToExpression()
    {
        return catalog => catalog.Type == _productType;
    }
}
```

### Caching Strategy
Performance optimization through caching:
- In-memory caching with configurable expiration
- Cache invalidation on stock updates
- Background refresh for data freshness
- Efficient cache key strategies

### Error Handling
Consistent error management:
- Business exceptions for domain rule violations
- External system exceptions for integration failures
- User-friendly error messages
- Comprehensive logging for troubleshooting

### Authorization
Role-based access control:
- View permissions for read operations
- Manage permissions for write operations
- User activity tracking for audit
- Secure external system integration

## Integration Architecture

### External System Clients
- **IErpStockClient**: ERP inventory data
- **IEshopStockClient**: E-commerce stock levels
- **ICatalogSalesClient**: Sales transaction history
- **IPurchaseHistoryClient**: Purchase order data
- **IConsumedMaterialsClient**: Manufacturing consumption
- **ICatalogAttributesClient**: Product configuration

### Data Flow Patterns
1. **Pull-based Refresh**: Background service pulls data periodically
2. **Event-driven Updates**: Real-time updates trigger cache invalidation
3. **Eventual Consistency**: Accepts temporary inconsistency for performance
4. **Bulk Operations**: Efficient batch processing for large datasets

### Performance Considerations
- **Background Processing**: Non-blocking data refresh
- **Parallel Execution**: Concurrent processing of different data sources
- **Memory Management**: Efficient caching with size limits
- **Database Optimization**: Proper indexing and query optimization

## Testing Strategy

### Unit Tests
- Domain model business logic
- Specification pattern implementations
- AutoMapper profile configurations
- Service layer business rules

### Integration Tests
- External system client integration
- End-to-end data flow validation
- Cache behavior verification
- Error handling scenarios

### Performance Tests
- Large dataset handling
- Concurrent access patterns
- Memory usage optimization
- Cache effectiveness measurement

## Business Value

The Catalog domain features deliver comprehensive business value:

1. **Operational Efficiency**: Unified data access eliminates system silos
2. **Real-time Visibility**: Current inventory status across all locations
3. **Data Accuracy**: Automated synchronization reduces manual errors
4. **Decision Support**: Historical analytics enable informed planning
5. **Compliance**: Lot tracking and audit trails meet regulatory requirements
6. **Scalability**: Architecture supports business growth and system expansion