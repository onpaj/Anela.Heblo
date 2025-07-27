# Procurement Stock Analysis User Story

## Feature Overview
The Procurement Stock Analysis feature provides comprehensive stock monitoring and purchasing decision support by analyzing consumption patterns, stock levels, and purchase history. This specialized view over catalog data enables procurement officers to identify stock shortages, forecast requirements, and optimize inventory levels through sophisticated filtering and analysis capabilities.

## Business Requirements

### Primary Use Case
As a procurement officer, I want to analyze stock levels and consumption patterns across all purchasable products so that I can identify critical stock shortages, forecast future requirements, prioritize purchase orders, and maintain optimal inventory levels while avoiding stockouts and excess inventory.

### Acceptance Criteria
1. The system shall analyze stock levels against configured minimum and optimal thresholds
2. The system shall calculate consumption rates based on product type (materials vs goods)
3. The system shall forecast stock depletion dates using historical consumption data
4. The system shall provide comprehensive filtering by stock status and configuration completeness
5. The system shall support flexible analysis periods with default to one year
6. The system shall integrate supplier information and purchase history
7. The system shall prioritize results by stock efficiency percentage
8. The system shall distinguish between materials (manufacturing consumption) and goods (sales consumption)

## Technical Contracts

### Domain Model

```csharp
// Primary aggregate for purchase stock analysis
public class PurchaseStockAggregate : Entity<string>
{
    // Product identification
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    
    // Stock configuration
    public int OptimalStockDaysSetup { get; set; } = 0;
    public double StockMinSetup { get; set; } = 0;
    
    // Current state
    public double OnStockNow { get; set; } = 0;
    
    // Analysis period
    public DateTime DateFrom { get; set; } = DateTime.Today;
    public DateTime DateTo { get; set; } = DateTime.Today;
    public int Days => (DateTo - DateFrom).Days;
    
    // Consumption metrics
    public double Consumed { get; set; }
    public double ConsumedDaily => Days > 0 ? Consumed / Days : 0;
    
    // Forecasting metrics
    public double OptimalStockDaysForecasted => 
        ConsumedDaily == 0 ? 0 : OnStockNow / ConsumedDaily;
    
    public double OptimalStockPercentage => 
        OptimalStockDaysSetup == 0 ? 0 : OptimalStockDaysForecasted / OptimalStockDaysSetup;
    
    // Supplier information
    public IReadOnlyList<Supplier> Suppliers { get; set; } = new List<Supplier>();
    public string? PrimarySupplier => Suppliers.FirstOrDefault(f => f.IsPrimary)?.Name;
    public string MinimalOrderQuantity { get; set; } = "";
    
    // Purchase history
    public IReadOnlyList<PurchaseHistoryData> PurchaseHistory { get; set; } = new List<PurchaseHistoryData>();
    public PurchaseHistoryData? LastPurchase { get; set; }
    
    // Stock health indicators
    public bool IsUnderStocked => OnStockNow < StockMinSetup && IsMinStockConfigured;
    public bool IsUnderForecasted => OptimalStockDaysForecasted < OptimalStockDaysSetup && IsOptimalStockConfigured;
    public bool IsMinStockConfigured => StockMinSetup > 0;
    public bool IsOptimalStockConfigured => OptimalStockDaysSetup > 0;
    public bool IsOk => IsMinStockConfigured && IsOptimalStockConfigured && 
                        !IsUnderForecasted && !IsUnderStocked;
    
    // Business methods
    public static PurchaseStockAggregate CreateFromCatalog(
        CatalogAggregate catalog, 
        DateTime dateFrom, 
        DateTime dateTo)
    {
        if (catalog == null)
            throw new BusinessException("Catalog aggregate is required");
        
        var aggregate = new PurchaseStockAggregate
        {
            Id = catalog.ProductCode,
            ProductCode = catalog.ProductCode,
            ProductName = catalog.ProductName,
            OptimalStockDaysSetup = catalog.OptimalStockDaysSetup,
            StockMinSetup = catalog.StockMinSetup,
            OnStockNow = catalog.Stock.Available,
            DateFrom = dateFrom,
            DateTo = dateTo
        };
        
        // Calculate consumption based on product type
        aggregate.Consumed = catalog.Type == ProductType.Material 
            ? catalog.GetConsumed(dateFrom, dateTo)
            : catalog.GetTotalSold(dateFrom, dateTo);
        
        return aggregate;
    }
    
    public double GetPurchaseRecommendation()
    {
        if (!IsOptimalStockConfigured)
            return 0;
        
        var targetStock = ConsumedDaily * OptimalStockDaysSetup;
        var shortfall = Math.Max(0, targetStock - OnStockNow);
        
        return shortfall;
    }
    
    public int GetDaysUntilStockout()
    {
        return ConsumedDaily > 0 ? (int)(OnStockNow / ConsumedDaily) : int.MaxValue;
    }
    
    public StockSeverity GetStockSeverity()
    {
        if (IsUnderStocked && OptimalStockDaysForecasted < 7)
            return StockSeverity.Critical;
        
        if (IsUnderStocked || (IsUnderForecasted && OptimalStockDaysForecasted < 14))
            return StockSeverity.Major;
        
        if (IsUnderForecasted)
            return StockSeverity.Minor;
        
        return StockSeverity.None;
    }
}

// Purchase history value object
public class PurchaseHistoryData
{
    public DateTime Date { get; set; }
    public double Quantity { get; set; }
    public string SupplierCode { get; set; }
    public string SupplierName { get; set; }
    public decimal PurchasePrice { get; set; }
    public string Currency { get; set; } = "CZK";
    
    public static PurchaseHistoryData Create(
        DateTime date,
        double quantity,
        string supplierCode,
        string supplierName,
        decimal purchasePrice,
        string currency = "CZK")
    {
        if (quantity <= 0)
            throw new BusinessException("Purchase quantity must be positive");
        
        if (string.IsNullOrEmpty(supplierCode))
            throw new BusinessException("Supplier code is required");
        
        return new PurchaseHistoryData
        {
            Date = date,
            Quantity = quantity,
            SupplierCode = supplierCode,
            SupplierName = supplierName,
            PurchasePrice = purchasePrice,
            Currency = currency
        };
    }
}

// Stock severity enumeration
public enum StockSeverity
{
    None = 0,     // Adequate stock
    Minor = 1,    // Monitor closely
    Major = 2,    // Action needed soon
    Critical = 3  // Immediate action required
}
```

### Application Layer Contracts

```csharp
// Application service interface
public interface IPurchaseStockAppService : IReadOnlyAppService<PurchaseStockDto, string, PurchaseStockQueryDto>
{
    Task<List<PurchaseStockDto>> GetCriticalStockAsync();
    Task<PurchaseRecommendationDto> GetPurchaseRecommendationsAsync(PurchaseRecommendationRequestDto request);
    Task<StockAnalysisReportDto> GetStockAnalysisReportAsync(DateTime fromDate, DateTime toDate);
}

// DTOs
public class PurchaseStockDto
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public int OptimalStockDaysSetup { get; set; }
    public double StockMinSetup { get; set; }
    public double OnStockNow { get; set; }
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public int Days { get; set; }
    public double Consumed { get; set; }
    public double ConsumedDaily { get; set; }
    public double OptimalStockDaysForecasted { get; set; }
    public double OptimalStockPercentage { get; set; }
    public List<SupplierDto> Suppliers { get; set; } = new();
    public string? PrimarySupplier { get; set; }
    public string MinimalOrderQuantity { get; set; }
    public List<PurchaseHistoryDataDto> PurchaseHistory { get; set; } = new();
    public PurchaseHistoryDataDto? LastPurchase { get; set; }
    public bool IsUnderStocked { get; set; }
    public bool IsUnderForecasted { get; set; }
    public bool IsMinStockConfigured { get; set; }
    public bool IsOptimalStockConfigured { get; set; }
    public bool IsOk { get; set; }
    public StockSeverity Severity { get; set; }
    public double PurchaseRecommendation { get; set; }
    public int DaysUntilStockout { get; set; }
}

public class PurchaseStockQueryDto : PagedAndSortedResultRequestDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    
    // Status filters (default: show all problems, hide OK)
    public bool ShowOk { get; set; } = false;
    public bool ShowUnderStocked { get; set; } = true;
    public bool ShowUnderForecasted { get; set; } = true;
    public bool ShowMinStockMissing { get; set; } = true;
    public bool ShowOptimalStockMissing { get; set; } = true;
    
    // Severity filtering
    public StockSeverity? MinimumSeverity { get; set; }
    
    // Supplier filtering
    public string? SupplierCode { get; set; }
}

public class PurchaseRecommendationDto
{
    public List<PurchaseRecommendationItemDto> Recommendations { get; set; } = new();
    public decimal TotalEstimatedCost { get; set; }
    public string Currency { get; set; } = "CZK";
    public DateTime GeneratedAt { get; set; }
    public string GeneratedBy { get; set; }
}

public class PurchaseRecommendationItemDto
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double RecommendedQuantity { get; set; }
    public string PrimarySupplier { get; set; }
    public decimal EstimatedUnitCost { get; set; }
    public decimal EstimatedTotalCost { get; set; }
    public int DaysUntilStockout { get; set; }
    public StockSeverity Severity { get; set; }
    public string Justification { get; set; }
}

public class StockAnalysisReportDto
{
    public DateTime ReportDate { get; set; }
    public DateTime AnalysisFrom { get; set; }
    public DateTime AnalysisTo { get; set; }
    public int TotalProducts { get; set; }
    public int CriticalStockItems { get; set; }
    public int MajorStockItems { get; set; }
    public int MinorStockItems { get; set; }
    public int AdequateStockItems { get; set; }
    public int MissingConfiguration { get; set; }
    public double AverageStockEfficiency { get; set; }
    public decimal EstimatedPurchaseValue { get; set; }
    public List<TopConsumptionItemDto> TopConsumptionItems { get; set; } = new();
    public List<CriticalStockItemDto> CriticalStockItems { get; set; } = new();
}
```

### Repository Pattern

```csharp
public interface IPurchaseStockRepository : IReadOnlyRepository<PurchaseStockAggregate, string>
{
    Task<List<PurchaseStockAggregate>> GetListAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default);
    Task<List<PurchaseStockAggregate>> GetCriticalStockAsync(CancellationToken cancellationToken = default);
    Task<List<PurchaseStockAggregate>> GetBySupplierAsync(string supplierCode, CancellationToken cancellationToken = default);
}

// Catalog filter for purchase-specific products
public class PurchaseCatalogFilter : ICatalogFilter
{
    public Expression<Func<CatalogAggregate, bool>> Predicate =>
        catalog => catalog.Type == ProductType.Material || catalog.Type == ProductType.Goods;
    
    public string Name => "PurchaseCatalogFilter";
    public string Description => "Filters catalog to include only purchasable products (Materials and Goods)";
}
```

## Implementation Details

### Application Service Implementation

```csharp
[Authorize]
public class PurchaseStockAppService : ReadOnlyAppService<PurchaseStockAggregate, PurchaseStockDto, string, PurchaseStockQueryDto>, 
    IPurchaseStockAppService
{
    private readonly IPurchaseStockRepository _repository;
    private readonly IClock _clock;
    private readonly ILogger<PurchaseStockAppService> _logger;
    
    public PurchaseStockAppService(
        IPurchaseStockRepository repository,
        IClock clock,
        ILogger<PurchaseStockAppService> logger)
        : base(repository)
    {
        _repository = repository;
        _clock = clock;
        _logger = logger;
    }
    
    protected override IQueryable<PurchaseStockAggregate> ApplyDefaultSorting(IQueryable<PurchaseStockAggregate> query)
    {
        // Sort by worst stock efficiency first (lowest percentage = highest priority)
        return query.OrderBy(o => o.OptimalStockPercentage)
                   .ThenBy(o => o.OptimalStockDaysForecasted)
                   .ThenBy(o => o.ProductCode);
    }
    
    protected override async Task<IQueryable<PurchaseStockAggregate>> CreateFilteredQueryAsync(PurchaseStockQueryDto input)
    {
        // Default to last year if no dates specified
        var dateFrom = input.DateFrom ?? _clock.Now.AddYears(-1);
        var dateTo = input.DateTo ?? _clock.Now;
        
        var query = (await _repository.GetListAsync(dateFrom, dateTo)).AsQueryable();
        
        // Apply negative filtering (exclude unwanted statuses)
        if (!input.ShowMinStockMissing)
            query = query.Where(w => w.IsMinStockConfigured);
        
        if (!input.ShowOptimalStockMissing)
            query = query.Where(w => w.IsOptimalStockConfigured);
        
        if (!input.ShowUnderForecasted)
            query = query.Where(w => !w.IsUnderForecasted);
        
        if (!input.ShowUnderStocked)
            query = query.Where(w => !w.IsUnderStocked);
        
        if (!input.ShowOk)
            query = query.Where(w => !w.IsOk);
        
        // Apply text filters
        if (!string.IsNullOrEmpty(input.ProductName))
            query = query.Where(w => w.ProductName.Contains(input.ProductName, StringComparison.CurrentCultureIgnoreCase));
        
        if (!string.IsNullOrEmpty(input.ProductCode))
            query = query.Where(w => w.ProductCode.Contains(input.ProductCode, StringComparison.CurrentCultureIgnoreCase));
        
        // Apply severity filter
        if (input.MinimumSeverity.HasValue)
        {
            query = query.Where(w => w.GetStockSeverity() >= input.MinimumSeverity.Value);
        }
        
        // Apply supplier filter
        if (!string.IsNullOrEmpty(input.SupplierCode))
        {
            query = query.Where(w => w.Suppliers.Any(s => s.Code == input.SupplierCode));
        }
        
        return query;
    }
    
    public async Task<List<PurchaseStockDto>> GetCriticalStockAsync()
    {
        var criticalItems = await _repository.GetCriticalStockAsync();
        
        return ObjectMapper.Map<List<PurchaseStockAggregate>, List<PurchaseStockDto>>(criticalItems);
    }
    
    public async Task<PurchaseRecommendationDto> GetPurchaseRecommendationsAsync(PurchaseRecommendationRequestDto request)
    {
        var dateFrom = request.AnalysisFrom ?? _clock.Now.AddYears(-1);
        var dateTo = request.AnalysisTo ?? _clock.Now;
        
        var allItems = await _repository.GetListAsync(dateFrom, dateTo);
        
        var recommendations = allItems
            .Where(item => item.GetStockSeverity() >= (request.MinimumSeverity ?? StockSeverity.Minor))
            .Where(item => item.GetPurchaseRecommendation() > 0)
            .OrderByDescending(item => item.GetStockSeverity())
            .ThenBy(item => item.GetDaysUntilStockout())
            .Select(item => CreateRecommendationItem(item))
            .Take(request.MaxRecommendations ?? 50)
            .ToList();
        
        var result = new PurchaseRecommendationDto
        {
            Recommendations = recommendations,
            TotalEstimatedCost = recommendations.Sum(r => r.EstimatedTotalCost),
            GeneratedAt = _clock.Now,
            GeneratedBy = CurrentUser.UserName ?? "System"
        };
        
        _logger.LogInformation("Generated {Count} purchase recommendations with total value {TotalValue}",
            recommendations.Count, result.TotalEstimatedCost);
        
        return result;
    }
    
    public async Task<StockAnalysisReportDto> GetStockAnalysisReportAsync(DateTime fromDate, DateTime toDate)
    {
        var allItems = await _repository.GetListAsync(fromDate, toDate);
        
        var report = new StockAnalysisReportDto
        {
            ReportDate = _clock.Now,
            AnalysisFrom = fromDate,
            AnalysisTo = toDate,
            TotalProducts = allItems.Count,
            CriticalStockItems = allItems.Count(i => i.GetStockSeverity() == StockSeverity.Critical),
            MajorStockItems = allItems.Count(i => i.GetStockSeverity() == StockSeverity.Major),
            MinorStockItems = allItems.Count(i => i.GetStockSeverity() == StockSeverity.Minor),
            AdequateStockItems = allItems.Count(i => i.GetStockSeverity() == StockSeverity.None),
            MissingConfiguration = allItems.Count(i => !i.IsMinStockConfigured || !i.IsOptimalStockConfigured),
            AverageStockEfficiency = allItems.Where(i => i.IsOptimalStockConfigured).Average(i => i.OptimalStockPercentage),
            EstimatedPurchaseValue = (decimal)allItems.Sum(i => i.GetPurchaseRecommendation() * (double)(i.LastPurchase?.PurchasePrice ?? 0))
        };
        
        // Top consumption items
        report.TopConsumptionItems = allItems
            .OrderByDescending(i => i.Consumed)
            .Take(10)
            .Select(i => new TopConsumptionItemDto
            {
                ProductCode = i.ProductCode,
                ProductName = i.ProductName,
                TotalConsumption = i.Consumed,
                DailyConsumption = i.ConsumedDaily,
                CurrentStock = i.OnStockNow
            })
            .ToList();
        
        // Critical stock items
        report.CriticalStockItems = allItems
            .Where(i => i.GetStockSeverity() >= StockSeverity.Major)
            .OrderByDescending(i => i.GetStockSeverity())
            .ThenBy(i => i.GetDaysUntilStockout())
            .Take(20)
            .Select(i => new CriticalStockItemDto
            {
                ProductCode = i.ProductCode,
                ProductName = i.ProductName,
                CurrentStock = i.OnStockNow,
                DaysUntilStockout = i.GetDaysUntilStockout(),
                Severity = i.GetStockSeverity(),
                RecommendedPurchase = i.GetPurchaseRecommendation()
            })
            .ToList();
        
        return report;
    }
    
    private PurchaseRecommendationItemDto CreateRecommendationItem(PurchaseStockAggregate item)
    {
        var recommendedQuantity = item.GetPurchaseRecommendation();
        var unitCost = item.LastPurchase?.PurchasePrice ?? 0;
        
        return new PurchaseRecommendationItemDto
        {
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            RecommendedQuantity = recommendedQuantity,
            PrimarySupplier = item.PrimarySupplier ?? "No supplier configured",
            EstimatedUnitCost = unitCost,
            EstimatedTotalCost = (decimal)recommendedQuantity * unitCost,
            DaysUntilStockout = item.GetDaysUntilStockout(),
            Severity = item.GetStockSeverity(),
            Justification = GenerateJustification(item)
        };
    }
    
    private string GenerateJustification(PurchaseStockAggregate item)
    {
        var reasons = new List<string>();
        
        if (item.IsUnderStocked)
            reasons.Add($"Below minimum stock level ({item.StockMinSetup})");
        
        if (item.IsUnderForecasted)
            reasons.Add($"Below optimal stock days ({item.OptimalStockDaysSetup} days target)");
        
        var daysUntilStockout = item.GetDaysUntilStockout();
        if (daysUntilStockout < 30)
            reasons.Add($"Stock depletes in {daysUntilStockout} days");
        
        return string.Join("; ", reasons);
    }
}
```

### Repository Implementation

```csharp
public class PurchaseStockRepository : CatalogRepositoryAdapter<PurchaseStockAggregate, PurchaseCatalogFilter>, 
    IPurchaseStockRepository
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly PurchaseCatalogFilter _filter;
    private readonly IObjectMapper<HebloApplicationModule> _mapper;
    private readonly ILogger<PurchaseStockRepository> _logger;
    
    public PurchaseStockRepository(
        ICatalogRepository catalogRepository,
        PurchaseCatalogFilter filter,
        IObjectMapper<HebloApplicationModule> mapper,
        ILogger<PurchaseStockRepository> logger)
        : base(catalogRepository, filter, mapper)
    {
        _catalogRepository = catalogRepository;
        _filter = filter;
        _mapper = mapper;
        _logger = logger;
    }
    
    public async Task<List<PurchaseStockAggregate>> GetListAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default)
    {
        var catalog = (await _catalogRepository.GetListAsync(false, cancellationToken))
            .Where(_filter.Predicate.Compile());
        
        var results = catalog.Select(catalogItem =>
        {
            var purchaseAggregate = _mapper.Map<CatalogAggregate, PurchaseStockAggregate>(catalogItem);
            
            // Set analysis period
            purchaseAggregate.DateFrom = dateFrom;
            purchaseAggregate.DateTo = dateTo;
            
            // Calculate consumption based on product type
            purchaseAggregate.Consumed = catalogItem.Type == ProductType.Material 
                ? catalogItem.GetConsumed(dateFrom, dateTo)
                : catalogItem.GetTotalSold(dateFrom, dateTo);
            
            return purchaseAggregate;
        })
        .ToList();
        
        _logger.LogDebug("Retrieved {Count} purchase stock items for period {From} to {To}",
            results.Count, dateFrom, dateTo);
        
        return results;
    }
    
    public async Task<List<PurchaseStockAggregate>> GetCriticalStockAsync(CancellationToken cancellationToken = default)
    {
        // Use last 6 months for critical stock analysis
        var dateFrom = DateTime.Now.AddMonths(-6);
        var dateTo = DateTime.Now;
        
        var allItems = await GetListAsync(dateFrom, dateTo, cancellationToken);
        
        return allItems
            .Where(item => item.GetStockSeverity() >= StockSeverity.Major)
            .OrderByDescending(item => item.GetStockSeverity())
            .ThenBy(item => item.GetDaysUntilStockout())
            .ToList();
    }
    
    public async Task<List<PurchaseStockAggregate>> GetBySupplierAsync(string supplierCode, CancellationToken cancellationToken = default)
    {
        var dateFrom = DateTime.Now.AddYears(-1);
        var dateTo = DateTime.Now;
        
        var allItems = await GetListAsync(dateFrom, dateTo, cancellationToken);
        
        return allItems
            .Where(item => item.Suppliers.Any(s => s.Code == supplierCode))
            .ToList();
    }
}
```

## Business Logic Implementation

### Consumption Calculation

```csharp
public static class ConsumptionCalculator
{
    public static double CalculateConsumption(CatalogAggregate catalog, DateTime fromDate, DateTime toDate)
    {
        return catalog.Type switch
        {
            ProductType.Material => catalog.GetConsumed(fromDate, toDate), // Manufacturing consumption
            ProductType.Goods => catalog.GetTotalSold(fromDate, toDate),   // Sales consumption
            _ => 0
        };
    }
    
    public static double CalculateDailyConsumption(double totalConsumption, DateTime fromDate, DateTime toDate)
    {
        var days = (toDate - fromDate).Days;
        return days > 0 ? totalConsumption / days : 0;
    }
    
    public static double ForecastStockDays(double currentStock, double dailyConsumption)
    {
        return dailyConsumption > 0 ? currentStock / dailyConsumption : double.MaxValue;
    }
}
```

### Stock Analysis Engine

```csharp
public static class StockAnalysisEngine
{
    public static StockSeverity AnalyzeSeverity(PurchaseStockAggregate item)
    {
        // Critical: Under minimum stock AND very low days remaining
        if (item.IsUnderStocked && item.OptimalStockDaysForecasted < 7)
            return StockSeverity.Critical;
        
        // Major: Under minimum OR under forecasted with low days
        if (item.IsUnderStocked || (item.IsUnderForecasted && item.OptimalStockDaysForecasted < 14))
            return StockSeverity.Major;
        
        // Minor: Under forecasted but still some buffer
        if (item.IsUnderForecasted)
            return StockSeverity.Minor;
        
        return StockSeverity.None;
    }
    
    public static double CalculatePurchaseRecommendation(PurchaseStockAggregate item)
    {
        if (!item.IsOptimalStockConfigured)
            return 0;
        
        var targetStock = item.ConsumedDaily * item.OptimalStockDaysSetup;
        var shortfall = Math.Max(0, targetStock - item.OnStockNow);
        
        // Add safety buffer based on consumption volatility
        var safetyBuffer = item.ConsumedDaily * 7; // 1 week safety stock
        
        return shortfall + safetyBuffer;
    }
    
    public static string GenerateStockAlert(PurchaseStockAggregate item)
    {
        var severity = AnalyzeSeverity(item);
        var daysUntilStockout = item.GetDaysUntilStockout();
        
        return severity switch
        {
            StockSeverity.Critical => $"CRITICAL: {item.ProductCode} will stock out in {daysUntilStockout} days",
            StockSeverity.Major => $"MAJOR: {item.ProductCode} requires immediate purchase attention",
            StockSeverity.Minor => $"MINOR: {item.ProductCode} below optimal stock level",
            _ => $"OK: {item.ProductCode} adequately stocked"
        };
    }
}
```

## Happy Day Scenario

1. **Analysis Request**: Procurement officer requests stock analysis for last 6 months
2. **Data Retrieval**: System fetches catalog data filtered for materials and goods
3. **Consumption Calculation**: Calculate consumption based on product type (manufacturing vs sales)
4. **Stock Assessment**: Evaluate current stock against configured thresholds
5. **Severity Analysis**: Classify each product by stock urgency level
6. **Filtering Application**: Apply user-specified filters to focus on problem areas
7. **Recommendation Generation**: Calculate purchase quantities for critical items
8. **Results Presentation**: Display prioritized list sorted by stock efficiency

## Error Handling

### Data Validation Errors
- **Invalid Date Range**: Ensure fromDate <= toDate and reasonable analysis periods
- **Missing Configuration**: Handle products without min/optimal stock settings gracefully
- **Zero Consumption**: Avoid division by zero in forecasting calculations
- **Negative Stock**: Validate stock levels and consumption data integrity

### Integration Errors
- **Catalog Access Failures**: Fallback to cached data or partial results
- **Mapping Errors**: Handle mismatched product types or missing properties
- **Performance Issues**: Implement timeouts and chunked processing for large datasets
- **Supplier Data Issues**: Handle missing or invalid supplier information

### Business Logic Errors
- **Configuration Conflicts**: Detect inconsistent min/optimal stock settings
- **Consumption Anomalies**: Identify unusual consumption patterns requiring review
- **Forecasting Limits**: Handle extreme forecasting scenarios (very high/low consumption)
- **Currency Mismatches**: Ensure consistent currency handling in cost calculations

## Business Rules

### Product Classification
1. **Materials**: Use manufacturing consumption data for analysis
2. **Goods**: Use sales velocity data for analysis
3. **Other Types**: Exclude from purchase analysis (e.g., services, labor)

### Stock Thresholds
1. **Minimum Stock**: Absolute minimum before stockout risk
2. **Optimal Stock**: Target days of inventory to maintain
3. **Safety Stock**: Additional buffer for consumption volatility
4. **Configuration Completeness**: Both min and optimal must be set for full analysis

### Consumption Analysis
1. **Analysis Period**: Default to 1 year, allow custom periods
2. **Daily Calculation**: Total consumption divided by days in period
3. **Seasonal Adjustment**: Consider seasonal patterns in consumption
4. **Data Quality**: Exclude periods with known data issues

### Purchase Recommendations
1. **Severity-Based Priority**: Critical items get immediate attention
2. **Economic Order Quantities**: Consider supplier minimum orders
3. **Lead Time Consideration**: Factor in supplier delivery times
4. **Cash Flow Impact**: Balance stock needs with financial constraints

## Persistence Layer Requirements

### Database Schema
```sql
-- Purchase stock is a view over catalog data, no separate tables needed
-- Leverages existing catalog, product, and supplier tables

-- Additional indexes for performance
CREATE INDEX IX_CatalogAggregate_Type_StockLevels 
    ON CatalogAggregates(Type, StockMinSetup, OptimalStockDaysSetup)
    WHERE Type IN (1, 2); -- Material and Goods only

CREATE INDEX IX_CatalogAggregate_Stock_Consumption 
    ON CatalogAggregates(OnStockAvailable, LastConsumptionUpdate);
```

### Caching Strategy
- **Catalog Data Cache**: Cache filtered catalog data (TTL: 10 minutes)
- **Consumption Cache**: Cache consumption calculations (TTL: 1 hour)
- **Analysis Results Cache**: Cache analysis results per user/query (TTL: 5 minutes)
- **Supplier Cache**: Cache supplier information (TTL: 30 minutes)

## Integration Requirements

### Catalog Domain Integration
- **Primary Dependency**: ICatalogRepository for all product and stock data
- **Consumption Data**: Historical manufacturing and sales consumption
- **Stock Levels**: Real-time inventory across all locations
- **Configuration**: Min/optimal stock thresholds per product

### Supplier Management Integration
- **Supplier Information**: Primary and alternative suppliers per product
- **Purchase History**: Historical transaction data and pricing
- **Lead Times**: Delivery time expectations for planning
- **Minimum Orders**: Economic order quantities and constraints

### ERP Integration
- **FlexiBee Sync**: Real-time stock level synchronization
- **Purchase Orders**: Integration with procurement workflow
- **Cost Data**: Historical and current pricing information
- **Vendor Management**: Supplier master data maintenance

## Performance Requirements
- Analyze 10,000+ products within 30 seconds
- Support concurrent analysis by multiple users
- Handle 1-year analysis periods efficiently
- Sub-second response for filtered queries
- Scale linearly with product catalog size
- Maintain accuracy with real-time stock updates