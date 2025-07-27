# User Story: Purchase History Analysis

## Overview
As a **procurement manager**, I want to analyze historical purchase data for specific products with date range filtering, so that I can make informed purchasing decisions based on supplier performance, pricing trends, and quantity patterns.

## Acceptance Criteria

### Primary Flow
**Given** that purchase history data is available from external systems  
**When** I request purchase history analysis with product code and date range  
**Then** the system should return detailed purchase records with supplier information, pricing, and quantities

### Business Rules
1. **Date Range Filtering**: Support flexible date range queries (default: 10 years back to current date)
2. **Product Filtering**: Optional product code filtering (null returns all products)
3. **Supplier Information**: Include complete supplier details and pricing history
4. **Authorization Required**: Only authenticated users can access purchase data
5. **Historical Data**: Maintain comprehensive purchase transaction history

## Technical Requirements

### Application Service

#### IPurchaseHistoryAppService Interface
```csharp
public interface IPurchaseHistoryAppService : IApplicationService
{
    Task<ListResultDto<PurchaseHistoryRecordDto>> GetListAsync(PurchaseHistoryQueryDto input);
}
```

#### PurchaseHistoryAppService Implementation
```csharp
[Authorize]
public class PurchaseHistoryAppService : HebloAppService, IPurchaseHistoryAppService
{
    private readonly IPurchaseHistoryClient _purchaseHistoryClient;
    private readonly IMapper _mapper;
    private readonly ILogger<PurchaseHistoryAppService> _logger;
    
    public PurchaseHistoryAppService(
        IPurchaseHistoryClient purchaseHistoryClient,
        IMapper mapper,
        ILogger<PurchaseHistoryAppService> logger)
    {
        _purchaseHistoryClient = purchaseHistoryClient;
        _mapper = mapper;
        _logger = logger;
    }
    
    public async Task<ListResultDto<PurchaseHistoryRecordDto>> GetListAsync(
        PurchaseHistoryQueryDto input)
    {
        _logger.LogInformation("Retrieving purchase history for product: {ProductCode}, " +
                             "Date range: {DateFrom} to {DateTo}", 
                             input.ProductCode, input.DateFrom, input.DateTo);
        
        try
        {
            // Set default date range if not provided
            var dateFrom = input.DateFrom ?? DateTime.Now.AddYears(-10);
            var dateTo = input.DateTo ?? DateTime.Now;
            
            // Validate date range
            ValidateDateRange(dateFrom, dateTo);
            
            // Retrieve purchase history from external client
            var purchaseHistory = await _purchaseHistoryClient.GetPurchaseHistoryAsync(
                input.ProductCode, dateFrom, dateTo);
            
            // Apply additional filtering if needed
            var filteredHistory = ApplyBusinessFilters(purchaseHistory, input);
            
            // Map to DTOs
            var historyDtos = _mapper.Map<List<PurchaseHistoryRecordDto>>(filteredHistory);
            
            // Sort by date descending (most recent first)
            historyDtos = historyDtos.OrderByDescending(h => h.Date).ToList();
            
            _logger.LogInformation("Retrieved {Count} purchase history records", historyDtos.Count);
            
            return new ListResultDto<PurchaseHistoryRecordDto>(historyDtos);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid query parameters for purchase history");
            throw new UserFriendlyException("Invalid query parameters", ex.Message);
        }
        catch (ExternalSystemException ex)
        {
            _logger.LogError(ex, "External system error retrieving purchase history");
            throw new UserFriendlyException("Unable to retrieve purchase history. Please try again later.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving purchase history");
            throw new AbpException("An unexpected error occurred while retrieving purchase history");
        }
    }
    
    private void ValidateDateRange(DateTime dateFrom, DateTime dateTo)
    {
        if (dateFrom > dateTo)
        {
            throw new ArgumentException("DateFrom cannot be greater than DateTo");
        }
        
        if (dateTo > DateTime.Now.AddDays(1))
        {
            throw new ArgumentException("DateTo cannot be in the future");
        }
        
        var maxRange = TimeSpan.FromDays(3650); // 10 years
        if (dateTo - dateFrom > maxRange)
        {
            throw new ArgumentException($"Date range cannot exceed {maxRange.TotalDays} days");
        }
    }
    
    private List<CatalogPurchaseHistory> ApplyBusinessFilters(
        List<CatalogPurchaseHistory> history, 
        PurchaseHistoryQueryDto input)
    {
        var filtered = history.AsQueryable();
        
        // Filter by minimum amount if specified
        if (input.MinAmount.HasValue)
        {
            filtered = filtered.Where(h => h.Quantity >= input.MinAmount.Value);
        }
        
        // Filter by supplier if specified
        if (!string.IsNullOrEmpty(input.SupplierCode))
        {
            filtered = filtered.Where(h => h.SupplierCode == input.SupplierCode);
        }
        
        // Filter by price range if specified
        if (input.MinPrice.HasValue)
        {
            filtered = filtered.Where(h => h.PurchasePrice >= input.MinPrice.Value);
        }
        
        if (input.MaxPrice.HasValue)
        {
            filtered = filtered.Where(h => h.PurchasePrice <= input.MaxPrice.Value);
        }
        
        return filtered.ToList();
    }
}
```

### Data Transfer Objects

#### PurchaseHistoryQueryDto
```csharp
public class PurchaseHistoryQueryDto
{
    public string ProductCode { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string SupplierCode { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int MaxRecords { get; set; } = 1000; // Prevent excessive data retrieval
}
```

#### PurchaseHistoryRecordDto
```csharp
public class PurchaseHistoryRecordDto
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public DateTime Date { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "ks"; // Default unit
    public decimal PurchasePrice { get; set; }
    public decimal TotalAmount { get; set; } // Calculated: Quantity * PurchasePrice
    public string Currency { get; set; } = "CZK";
    
    // Supplier Information
    public string SupplierCode { get; set; }
    public string SupplierName { get; set; }
    public string SupplierContact { get; set; }
    
    // Additional Metadata
    public string DocumentNumber { get; set; }
    public string BatchNumber { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public string Warehouse { get; set; }
    public string Notes { get; set; }
    
    // Analysis Fields
    public decimal PricePerUnit { get; set; }
    public bool IsBulkPurchase { get; set; } // Quantity > certain threshold
    public TimeSpan DaysSincePurchase { get; set; }
    public int PurchaseRank { get; set; } // Ranking by price among similar purchases
}
```

### External Client Interface

#### IPurchaseHistoryClient
```csharp
public interface IPurchaseHistoryClient
{
    Task<List<CatalogPurchaseHistory>> GetPurchaseHistoryAsync(
        string productCode = null, 
        DateTime? dateFrom = null, 
        DateTime? dateTo = null);
    
    Task<List<SupplierSummary>> GetSupplierSummaryAsync(
        string productCode,
        DateTime dateFrom,
        DateTime dateTo);
    
    Task<PurchaseTrendAnalysis> GetPurchaseTrendsAsync(
        string productCode,
        DateTime dateFrom,
        DateTime dateTo);
}
```

### Domain Models

#### CatalogPurchaseHistory (Enhanced)
```csharp
public class CatalogPurchaseHistory
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public DateTime Date { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; }
    public decimal PurchasePrice { get; set; }
    public string Currency { get; set; }
    public string SupplierCode { get; set; }
    public string SupplierName { get; set; }
    public string SupplierContact { get; set; }
    public string DocumentNumber { get; set; }
    public string BatchNumber { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public string Warehouse { get; set; }
    public string Notes { get; set; }
    
    // Computed Properties
    public decimal TotalAmount => Quantity * PurchasePrice;
    public bool HasExpired => ExpirationDate.HasValue && ExpirationDate.Value < DateTime.Now;
    public TimeSpan Age => DateTime.Now - Date;
}
```

#### SupplierSummary
```csharp
public class SupplierSummary
{
    public string SupplierCode { get; set; }
    public string SupplierName { get; set; }
    public int TotalPurchases { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal MinPrice { get; set; }
    public decimal MaxPrice { get; set; }
    public DateTime FirstPurchase { get; set; }
    public DateTime LastPurchase { get; set; }
    public double ReliabilityScore { get; set; } // Computed based on delivery times, quality, etc.
}
```

#### PurchaseTrendAnalysis
```csharp
public class PurchaseTrendAnalysis
{
    public string ProductCode { get; set; }
    public List<MonthlyPurchaseData> MonthlyData { get; set; }
    public PriceTrend PriceTrend { get; set; } // Increasing, Decreasing, Stable
    public decimal AverageMonthlyQuantity { get; set; }
    public decimal AveragePrice { get; set; }
    public SeasonalPattern SeasonalPattern { get; set; }
    public List<string> TopSuppliers { get; set; }
    public PurchaseRecommendation Recommendation { get; set; }
}

public class MonthlyPurchaseData
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal AveragePrice { get; set; }
    public int SupplierCount { get; set; }
}

public enum PriceTrend { Increasing, Decreasing, Stable, Volatile }
public enum SeasonalPattern { None, Spring, Summer, Autumn, Winter, YearEnd }

public class PurchaseRecommendation
{
    public string RecommendedSupplier { get; set; }
    public decimal RecommendedQuantity { get; set; }
    public decimal EstimatedPrice { get; set; }
    public DateTime OptimalPurchaseDate { get; set; }
    public string Reasoning { get; set; }
    public ConfidenceLevel Confidence { get; set; }
}

public enum ConfidenceLevel { Low, Medium, High }
```

### AutoMapper Profile

#### PurchaseHistoryMappingProfile
```csharp
public class PurchaseHistoryMappingProfile : Profile
{
    public PurchaseHistoryMappingProfile()
    {
        CreateMap<CatalogPurchaseHistory, PurchaseHistoryRecordDto>()
            .ForMember(dest => dest.TotalAmount, 
                      opt => opt.MapFrom(src => src.TotalAmount))
            .ForMember(dest => dest.PricePerUnit,
                      opt => opt.MapFrom(src => src.PurchasePrice))
            .ForMember(dest => dest.IsBulkPurchase,
                      opt => opt.MapFrom(src => src.Quantity >= 100)) // Configurable threshold
            .ForMember(dest => dest.DaysSincePurchase,
                      opt => opt.MapFrom(src => DateTime.Now - src.Date))
            .AfterMap((src, dest, context) =>
            {
                // Calculate purchase rank (would require additional context)
                dest.PurchaseRank = 0; // To be calculated based on comparative data
            });
        
        CreateMap<SupplierSummary, SupplierSummaryDto>();
        CreateMap<PurchaseTrendAnalysis, PurchaseTrendAnalysisDto>();
    }
}
```

## Business Logic Enhancements

### Purchase Analysis Service

#### IPurchaseAnalysisService
```csharp
public interface IPurchaseAnalysisService
{
    Task<SupplierPerformanceReport> AnalyzeSupplierPerformanceAsync(
        string productCode, DateTime dateFrom, DateTime dateTo);
    
    Task<PriceAnalysisReport> AnalyzePriceTrendsAsync(
        string productCode, DateTime dateFrom, DateTime dateTo);
    
    Task<PurchaseRecommendation> GetPurchaseRecommendationAsync(
        string productCode, decimal requiredQuantity);
    
    Task<List<CostSavingOpportunity>> IdentifyCostSavingOpportunitiesAsync(
        DateTime dateFrom, DateTime dateTo);
}
```

#### PurchaseAnalysisService Implementation
```csharp
public class PurchaseAnalysisService : IPurchaseAnalysisService
{
    private readonly IPurchaseHistoryClient _purchaseHistoryClient;
    private readonly ILogger<PurchaseAnalysisService> _logger;
    
    public async Task<SupplierPerformanceReport> AnalyzeSupplierPerformanceAsync(
        string productCode, DateTime dateFrom, DateTime dateTo)
    {
        var history = await _purchaseHistoryClient.GetPurchaseHistoryAsync(
            productCode, dateFrom, dateTo);
        
        var supplierGroups = history.GroupBy(h => h.SupplierCode);
        
        var performanceData = supplierGroups.Select(group => new SupplierPerformanceData
        {
            SupplierCode = group.Key,
            SupplierName = group.First().SupplierName,
            TotalPurchases = group.Count(),
            TotalQuantity = group.Sum(p => p.Quantity),
            AveragePrice = group.Average(p => p.PurchasePrice),
            PriceStability = CalculatePriceStability(group.ToList()),
            DeliveryReliability = CalculateDeliveryReliability(group.ToList()),
            QualityScore = CalculateQualityScore(group.ToList())
        }).ToList();
        
        return new SupplierPerformanceReport
        {
            ProductCode = productCode,
            AnalysisPeriod = new DateRange(dateFrom, dateTo),
            SupplierPerformance = performanceData,
            RecommendedSupplier = performanceData.OrderByDescending(p => p.OverallScore).First(),
            GeneratedDate = DateTime.Now
        };
    }
    
    public async Task<PriceAnalysisReport> AnalyzePriceTrendsAsync(
        string productCode, DateTime dateFrom, DateTime dateTo)
    {
        var history = await _purchaseHistoryClient.GetPurchaseHistoryAsync(
            productCode, dateFrom, dateTo);
        
        var monthlyData = history
            .GroupBy(h => new { h.Date.Year, h.Date.Month })
            .Select(group => new MonthlyPriceData
            {
                Year = group.Key.Year,
                Month = group.Key.Month,
                AveragePrice = group.Average(p => p.PurchasePrice),
                MinPrice = group.Min(p => p.PurchasePrice),
                MaxPrice = group.Max(p => p.PurchasePrice),
                TotalQuantity = group.Sum(p => p.Quantity),
                TransactionCount = group.Count()
            })
            .OrderBy(d => d.Year)
            .ThenBy(d => d.Month)
            .ToList();
        
        var trend = CalculatePriceTrend(monthlyData);
        var volatility = CalculatePriceVolatility(monthlyData);
        var seasonality = DetectSeasonalPatterns(monthlyData);
        
        return new PriceAnalysisReport
        {
            ProductCode = productCode,
            AnalysisPeriod = new DateRange(dateFrom, dateTo),
            MonthlyData = monthlyData,
            OverallTrend = trend,
            Volatility = volatility,
            SeasonalPattern = seasonality,
            PredictedNextPrice = PredictNextPrice(monthlyData, trend),
            GeneratedDate = DateTime.Now
        };
    }
    
    // Additional analysis methods...
    private double CalculatePriceStability(List<CatalogPurchaseHistory> purchases)
    {
        if (purchases.Count < 2) return 1.0;
        
        var prices = purchases.Select(p => (double)p.PurchasePrice).ToList();
        var mean = prices.Average();
        var variance = prices.Sum(p => Math.Pow(p - mean, 2)) / prices.Count;
        var standardDeviation = Math.Sqrt(variance);
        
        // Return stability score (0-1, where 1 is most stable)
        return Math.Max(0, 1 - (standardDeviation / mean));
    }
}
```

## Error Handling and Validation

### Custom Exceptions
```csharp
public class PurchaseHistoryException : AbpException
{
    public PurchaseHistoryException(string message) : base(message) { }
    public PurchaseHistoryException(string message, Exception innerException) 
        : base(message, innerException) { }
}

public class InvalidQueryParametersException : PurchaseHistoryException
{
    public InvalidQueryParametersException(string parameterName, string reason)
        : base($"Invalid parameter '{parameterName}': {reason}") { }
}
```

### Input Validation
```csharp
public class PurchaseHistoryQueryValidator : AbstractValidator<PurchaseHistoryQueryDto>
{
    public PurchaseHistoryQueryValidator()
    {
        RuleFor(x => x.DateFrom)
            .LessThan(x => x.DateTo)
            .When(x => x.DateFrom.HasValue && x.DateTo.HasValue)
            .WithMessage("DateFrom must be before DateTo");
        
        RuleFor(x => x.DateTo)
            .LessThanOrEqualTo(DateTime.Now.AddDays(1))
            .When(x => x.DateTo.HasValue)
            .WithMessage("DateTo cannot be in the future");
        
        RuleFor(x => x.ProductCode)
            .MaximumLength(50)
            .WithMessage("Product code cannot exceed 50 characters");
        
        RuleFor(x => x.MinAmount)
            .GreaterThan(0)
            .When(x => x.MinAmount.HasValue)
            .WithMessage("Minimum amount must be greater than 0");
        
        RuleFor(x => x.MaxRecords)
            .GreaterThan(0)
            .LessThanOrEqualTo(10000)
            .WithMessage("Max records must be between 1 and 10,000");
    }
}
```

## Caching Strategy

### Cache Implementation
```csharp
public class CachedPurchaseHistoryAppService : IPurchaseHistoryAppService
{
    private readonly IPurchaseHistoryAppService _innerService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedPurchaseHistoryAppService> _logger;
    
    private const int CACHE_DURATION_HOURS = 4; // Purchase history doesn't change frequently
    
    public async Task<ListResultDto<PurchaseHistoryRecordDto>> GetListAsync(
        PurchaseHistoryQueryDto input)
    {
        var cacheKey = GenerateCacheKey(input);
        
        var cachedResult = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cachedResult))
        {
            _logger.LogDebug("Returning cached purchase history for key: {CacheKey}", cacheKey);
            return JsonSerializer.Deserialize<ListResultDto<PurchaseHistoryRecordDto>>(cachedResult);
        }
        
        var result = await _innerService.GetListAsync(input);
        
        var serializedResult = JsonSerializer.Serialize(result);
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(CACHE_DURATION_HOURS)
        };
        
        await _cache.SetStringAsync(cacheKey, serializedResult, cacheOptions);
        
        _logger.LogDebug("Cached purchase history for key: {CacheKey}", cacheKey);
        
        return result;
    }
    
    private string GenerateCacheKey(PurchaseHistoryQueryDto input)
    {
        var keyParts = new[]
        {
            "purchase-history",
            input.ProductCode ?? "all",
            input.DateFrom?.ToString("yyyy-MM-dd") ?? "default",
            input.DateTo?.ToString("yyyy-MM-dd") ?? "default",
            input.SupplierCode ?? "all",
            input.MinAmount?.ToString() ?? "0",
            input.MinPrice?.ToString() ?? "0",
            input.MaxPrice?.ToString() ?? "max"
        };
        
        return string.Join(":", keyParts);
    }
}
```

## HTTP API Controller

### PurchaseHistoryController
```csharp
[ApiController]
[Route("api/purchase-history")]
[Authorize]
public class PurchaseHistoryController : HebloControllerBase
{
    private readonly IPurchaseHistoryAppService _purchaseHistoryAppService;
    
    public PurchaseHistoryController(IPurchaseHistoryAppService purchaseHistoryAppService)
    {
        _purchaseHistoryAppService = purchaseHistoryAppService;
    }
    
    [HttpGet]
    [ProducesResponseType(typeof(ListResultDto<PurchaseHistoryRecordDto>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<ListResultDto<PurchaseHistoryRecordDto>>> GetAsync(
        [FromQuery] PurchaseHistoryQueryDto query)
    {
        try
        {
            var result = await _purchaseHistoryAppService.GetListAsync(query);
            return Ok(result);
        }
        catch (UserFriendlyException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (AbpAuthorizationException)
        {
            return Unauthorized();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving purchase history");
            return StatusCode(500, "Internal server error");
        }
    }
    
    [HttpGet("analysis/supplier-performance")]
    [ProducesResponseType(typeof(SupplierPerformanceReport), 200)]
    public async Task<ActionResult<SupplierPerformanceReport>> GetSupplierPerformanceAsync(
        [FromQuery] string productCode,
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo)
    {
        // Implementation would use IPurchaseAnalysisService
        return Ok();
    }
    
    [HttpGet("analysis/price-trends")]
    [ProducesResponseType(typeof(PriceAnalysisReport), 200)]
    public async Task<ActionResult<PriceAnalysisReport>> GetPriceTrendsAsync(
        [FromQuery] string productCode,
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo)
    {
        // Implementation would use IPurchaseAnalysisService
        return Ok();
    }
}
```

## Business Value

### Procurement Benefits
1. **Data-Driven Decisions**: Historical pricing and supplier performance data
2. **Cost Optimization**: Identify best pricing periods and suppliers
3. **Risk Management**: Supplier reliability and price volatility analysis
4. **Compliance**: Complete audit trail of all purchases
5. **Trend Analysis**: Predictive insights for future procurement planning

### Operational Benefits
1. **Efficiency**: Quick access to comprehensive purchase history
2. **Analytics**: Built-in analysis tools for supplier and price evaluation
3. **Flexibility**: Configurable queries with multiple filter options
4. **Performance**: Caching strategy for fast data retrieval
5. **Integration**: Seamless connection with external procurement systems