# User Story: Semi-Product Management

## Overview
As a **manufacturing manager**, I want to access a specialized view of semi-finished products, so that I can track work-in-progress inventory and plan production workflows effectively.

## Acceptance Criteria

### Primary Flow
**Given** that the catalog contains products of various types  
**When** I request semi-products through the application service  
**Then** the system should return only products classified as `ProductType.SemiProduct`

### Business Rules
1. **Product Type Filter**: Only products with `ProductType.SemiProduct` are included
2. **Manufacturing Focus**: Provides simplified view for production planning
3. **Authorization Required**: Only authenticated users can access semi-product data
4. **Real-time Data**: Information reflects current catalog state

## Technical Requirements

### Application Service

#### ISemiProductAppService Interface
```csharp
public interface ISemiProductAppService : IApplicationService
{
    Task<ListResultDto<SemiProductDto>> GetListAsync();
}
```

#### SemiProductAppService Implementation
```csharp
[Authorize]
public class SemiProductAppService : HebloAppService, ISemiProductAppService
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IMapper _mapper;
    private readonly ILogger<SemiProductAppService> _logger;
    
    public SemiProductAppService(
        ICatalogRepository catalogRepository,
        IMapper mapper,
        ILogger<SemiProductAppService> logger)
    {
        _catalogRepository = catalogRepository;
        _mapper = mapper;
        _logger = logger;
    }
    
    public async Task<ListResultDto<SemiProductDto>> GetListAsync()
    {
        _logger.LogInformation("Retrieving semi-products list");
        
        try
        {
            // Create specification for semi-products only
            var specification = new ProductTypeSpecification(ProductType.SemiProduct);
            
            // Get filtered catalog items
            var catalogItems = await _catalogRepository.GetListAsync(specification);
            
            // Map to DTOs
            var semiProductDtos = _mapper.Map<List<SemiProductDto>>(catalogItems);
            
            _logger.LogInformation("Retrieved {Count} semi-products", semiProductDtos.Count);
            
            return new ListResultDto<SemiProductDto>(semiProductDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving semi-products");
            throw;
        }
    }
}
```

### Data Transfer Objects

#### SemiProductDto
```csharp
public class SemiProductDto
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public string ErpId { get; set; }
    public string Location { get; set; }
    public decimal Volume { get; set; }
    public decimal Weight { get; set; }
    public bool HasExpiration { get; set; }
    public bool HasLots { get; set; }
    
    // Simplified stock information for manufacturing
    public decimal AvailableStock { get; set; }
    public decimal ErpStock { get; set; }
    public decimal EshopStock { get; set; }
    public decimal TransportStock { get; set; }
    public decimal ReserveStock { get; set; }
    
    // Manufacturing-relevant properties
    public decimal BatchSize { get; set; }
    public int OptimalStockDays { get; set; }
    public decimal StockMinSetup { get; set; }
    public bool IsUnderStocked { get; set; }
    
    // Product categorization
    public string ProductFamily { get; set; }
    public string ProductTypeCode { get; set; }
    public string ProductSize { get; set; }
    
    // Current status
    public bool IsInSeason { get; set; }
    public DateTime LastUpdated { get; set; }
}
```

### Domain Specifications

#### ProductTypeSpecification
```csharp
public class ProductTypeSpecification : Specification<CatalogAggregate>
{
    private readonly ProductType _productType;
    
    public ProductTypeSpecification(ProductType productType)
    {
        _productType = productType;
    }
    
    public override Expression<Func<CatalogAggregate, bool>> ToExpression()
    {
        return catalog => catalog.Type == _productType;
    }
    
    protected override string[] CreateIncludeArray()
    {
        return new[] 
        { 
            nameof(CatalogAggregate.Stock),
            nameof(CatalogAggregate.Properties),
            nameof(CatalogAggregate.Suppliers)
        };
    }
}
```

### AutoMapper Profile

#### SemiProductMappingProfile
```csharp
public class SemiProductMappingProfile : Profile
{
    public SemiProductMappingProfile()
    {
        CreateMap<CatalogAggregate, SemiProductDto>()
            .ForMember(dest => dest.AvailableStock, 
                      opt => opt.MapFrom(src => src.Stock != null ? src.Stock.Available : 0))
            .ForMember(dest => dest.ErpStock,
                      opt => opt.MapFrom(src => src.Stock != null ? src.Stock.Erp : 0))
            .ForMember(dest => dest.EshopStock,
                      opt => opt.MapFrom(src => src.Stock != null ? src.Stock.Eshop : 0))
            .ForMember(dest => dest.TransportStock,
                      opt => opt.MapFrom(src => src.Stock != null ? src.Stock.Transport : 0))
            .ForMember(dest => dest.ReserveStock,
                      opt => opt.MapFrom(src => src.Stock != null ? src.Stock.Reserve : 0))
            .ForMember(dest => dest.BatchSize,
                      opt => opt.MapFrom(src => src.Properties != null ? src.Properties.BatchSize : 0))
            .ForMember(dest => dest.OptimalStockDays,
                      opt => opt.MapFrom(src => src.Properties != null ? src.Properties.OptimalStockDaysSetup : 0))
            .ForMember(dest => dest.StockMinSetup,
                      opt => opt.MapFrom(src => src.Properties != null ? src.Properties.StockMinSetup : 0))
            .ForMember(dest => dest.LastUpdated,
                      opt => opt.MapFrom(src => DateTime.UtcNow));
    }
}
```

### Extended Repository Support

#### Repository Extension Methods
```csharp
public static class CatalogRepositoryExtensions
{
    public static async Task<List<CatalogAggregate>> GetSemiProductsAsync(
        this ICatalogRepository repository)
    {
        var specification = new ProductTypeSpecification(ProductType.SemiProduct);
        return await repository.GetListAsync(specification);
    }
    
    public static async Task<List<CatalogAggregate>> GetSemiProductsByFamilyAsync(
        this ICatalogRepository repository,
        string productFamily)
    {
        var specification = new ProductTypeSpecification(ProductType.SemiProduct)
            .And(new ProductFamilySpecification(productFamily));
        return await repository.GetListAsync(specification);
    }
    
    public static async Task<List<CatalogAggregate>> GetUnderStockedSemiProductsAsync(
        this ICatalogRepository repository)
    {
        var specification = new ProductTypeSpecification(ProductType.SemiProduct)
            .And(new UnderStockedSpecification());
        return await repository.GetListAsync(specification);
    }
}
```

### Additional Specifications

#### ProductFamilySpecification
```csharp
public class ProductFamilySpecification : Specification<CatalogAggregate>
{
    private readonly string _productFamily;
    
    public ProductFamilySpecification(string productFamily)
    {
        _productFamily = productFamily ?? throw new ArgumentNullException(nameof(productFamily));
    }
    
    public override Expression<Func<CatalogAggregate, bool>> ToExpression()
    {
        return catalog => catalog.ProductFamily == _productFamily;
    }
}
```

#### UnderStockedSpecification
```csharp
public class UnderStockedSpecification : Specification<CatalogAggregate>
{
    public override Expression<Func<CatalogAggregate, bool>> ToExpression()
    {
        return catalog => catalog.IsUnderStocked;
    }
}
```

## Error Handling

### Exception Handling Strategy
```csharp
public class SemiProductAppService : HebloAppService, ISemiProductAppService
{
    public async Task<ListResultDto<SemiProductDto>> GetListAsync()
    {
        try
        {
            // Implementation
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Unauthorized access attempt to semi-products");
            throw new AbpAuthorizationException("Access denied to semi-products");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation while retrieving semi-products");
            throw new UserFriendlyException("Unable to retrieve semi-products. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving semi-products");
            throw new AbpException("An unexpected error occurred while retrieving semi-products");
        }
    }
}
```

### Validation Rules
```csharp
public class SemiProductBusinessRules
{
    public static void ValidateProductType(CatalogAggregate catalog)
    {
        if (catalog.Type != ProductType.SemiProduct)
        {
            throw new BusinessException("Product is not a semi-product");
        }
    }
    
    public static void ValidateStockData(CatalogAggregate catalog)
    {
        if (catalog.Stock == null)
        {
            throw new BusinessException("Stock data is required for semi-products");
        }
    }
    
    public static void ValidateProductionReadiness(CatalogAggregate catalog)
    {
        if (catalog.Properties?.BatchSize <= 0)
        {
            throw new BusinessException("Batch size must be configured for semi-products");
        }
    }
}
```

## Authorization and Security

### Permission Definitions
```csharp
public static class SemiProductPermissions
{
    public const string GroupName = "SemiProducts";
    
    public const string Default = GroupName + ".Default";
    public const string View = GroupName + ".View";
    public const string Manage = GroupName + ".Manage";
}

public class SemiProductPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var semiProductGroup = context.AddGroup(SemiProductPermissions.GroupName);
        
        semiProductGroup.AddPermission(SemiProductPermissions.View)
                       .WithDisplayName("View Semi-Products");
        
        semiProductGroup.AddPermission(SemiProductPermissions.Manage)
                       .WithDisplayName("Manage Semi-Products");
    }
}
```

### Authorization Implementation
```csharp
[Authorize(SemiProductPermissions.View)]
public class SemiProductAppService : HebloAppService, ISemiProductAppService
{
    public async Task<ListResultDto<SemiProductDto>> GetListAsync()
    {
        await CheckPermissionAsync(SemiProductPermissions.View);
        
        // Implementation
    }
    
    [Authorize(SemiProductPermissions.Manage)]
    public async Task UpdateSemiProductAsync(string productCode, UpdateSemiProductDto input)
    {
        await CheckPermissionAsync(SemiProductPermissions.Manage);
        
        // Implementation for updates
    }
}
```

## Caching Strategy

### Cache Implementation
```csharp
public class CachedSemiProductAppService : ISemiProductAppService
{
    private readonly ISemiProductAppService _innerService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedSemiProductAppService> _logger;
    
    private const string CACHE_KEY = "semi-products-list";
    private const int CACHE_DURATION_MINUTES = 15;
    
    public async Task<ListResultDto<SemiProductDto>> GetListAsync()
    {
        var cacheKey = $"{CACHE_KEY}:{CurrentUser.Id}";
        
        var cachedResult = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cachedResult))
        {
            _logger.LogDebug("Returning cached semi-products list");
            return JsonSerializer.Deserialize<ListResultDto<SemiProductDto>>(cachedResult);
        }
        
        var result = await _innerService.GetListAsync();
        
        var serializedResult = JsonSerializer.Serialize(result);
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CACHE_DURATION_MINUTES)
        };
        
        await _cache.SetStringAsync(cacheKey, serializedResult, cacheOptions);
        
        _logger.LogDebug("Cached semi-products list for {Duration} minutes", CACHE_DURATION_MINUTES);
        
        return result;
    }
}
```

### Cache Invalidation
```csharp
public interface ISemiProductCacheManager
{
    Task InvalidateListCacheAsync();
    Task InvalidateListCacheAsync(string userId);
}

public class SemiProductCacheManager : ISemiProductCacheManager
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<SemiProductCacheManager> _logger;
    
    public async Task InvalidateListCacheAsync()
    {
        // Implementation to clear all user caches
        // This would require a cache key pattern or registry
        _logger.LogInformation("Invalidated all semi-product list caches");
    }
    
    public async Task InvalidateListCacheAsync(string userId)
    {
        var cacheKey = $"semi-products-list:{userId}";
        await _cache.RemoveAsync(cacheKey);
        _logger.LogDebug("Invalidated semi-product list cache for user {UserId}", userId);
    }
}
```

## HTTP API Controller

### SemiProductController
```csharp
[ApiController]
[Route("api/semi-products")]
[Authorize]
public class SemiProductController : HebloControllerBase
{
    private readonly ISemiProductAppService _semiProductAppService;
    
    public SemiProductController(ISemiProductAppService semiProductAppService)
    {
        _semiProductAppService = semiProductAppService;
    }
    
    [HttpGet]
    [ProducesResponseType(typeof(ListResultDto<SemiProductDto>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<ListResultDto<SemiProductDto>>> GetListAsync()
    {
        try
        {
            var result = await _semiProductAppService.GetListAsync();
            return Ok(result);
        }
        catch (AbpAuthorizationException)
        {
            return Unauthorized();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving semi-products list");
            return StatusCode(500, "Internal server error");
        }
    }
    
    [HttpGet("under-stocked")]
    [ProducesResponseType(typeof(ListResultDto<SemiProductDto>), 200)]
    public async Task<ActionResult<ListResultDto<SemiProductDto>>> GetUnderStockedAsync()
    {
        // Implementation for under-stocked semi-products
        // Would require additional app service method
        return Ok();
    }
    
    [HttpGet("by-family/{family}")]
    [ProducesResponseType(typeof(ListResultDto<SemiProductDto>), 200)]
    public async Task<ActionResult<ListResultDto<SemiProductDto>>> GetByFamilyAsync(string family)
    {
        // Implementation for family-filtered semi-products
        // Would require additional app service method
        return Ok();
    }
}
```

## Integration Requirements

### Dependency Registration
```csharp
public class SemiProductModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        
        // Register application service
        services.AddTransient<ISemiProductAppService, SemiProductAppService>();
        
        // Register with caching decorator if needed
        services.Decorate<ISemiProductAppService, CachedSemiProductAppService>();
        
        // Register cache manager
        services.AddTransient<ISemiProductCacheManager, SemiProductCacheManager>();
        
        // Register AutoMapper profile
        services.AddAutoMapperProfile<SemiProductMappingProfile>();
    }
}
```

### Background Cache Warming
```csharp
public class SemiProductCacheWarmupService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SemiProductCacheWarmupService> _logger;
    private Timer _timer;
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Warm up cache immediately
        await WarmupCacheAsync();
        
        // Schedule periodic cache warmup
        _timer = new Timer(async _ => await WarmupCacheAsync(), 
                          null, 
                          TimeSpan.Zero, 
                          TimeSpan.FromMinutes(30));
    }
    
    private async Task WarmupCacheAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var semiProductService = scope.ServiceProvider
                .GetRequiredService<ISemiProductAppService>();
            
            await semiProductService.GetListAsync();
            
            _logger.LogInformation("Semi-product cache warmed up successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error warming up semi-product cache");
        }
    }
    
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }
}
```

## Business Value

### Manufacturing Benefits
1. **Production Planning**: Clear visibility of work-in-progress inventory
2. **Resource Allocation**: Identify bottlenecks in production pipeline
3. **Quality Control**: Track semi-products through manufacturing stages
4. **Cost Management**: Monitor work-in-progress costs and efficiency

### Operational Benefits
1. **Simplified Interface**: Manufacturing-focused view without unnecessary data
2. **Real-time Visibility**: Current status of all semi-finished products
3. **Stock Management**: Identify under-stocked semi-products for production planning
4. **Performance**: Optimized queries and caching for fast access

### Integration Benefits
1. **ERP Synchronization**: Consistent data across manufacturing systems
2. **Workflow Integration**: Seamless integration with production planning
3. **Reporting**: Foundation for manufacturing KPIs and dashboards
4. **Scalability**: Efficient data access patterns for growing product catalogs