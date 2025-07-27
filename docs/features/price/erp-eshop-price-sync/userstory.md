# ERP-to-E-shop Price Synchronization User Story

## Feature Overview
The ERP-to-E-shop Price Synchronization feature provides automated price synchronization between FlexiBee ERP (authoritative source) and Shoptet e-commerce platform. This integration ensures price consistency across systems, handles VAT calculations according to Czech tax requirements, implements intelligent caching for performance, and provides comprehensive error handling and audit trails.

## Business Requirements

### Primary Use Case
As a pricing manager, I want to automatically synchronize product prices from our ERP system to our e-commerce platform so that customers always see accurate, up-to-date pricing that includes proper VAT calculations while maintaining system performance through intelligent caching and providing reliable error handling for business continuity.

### Acceptance Criteria
1. The system shall pull pricing data from FlexiBee ERP as the authoritative source
2. The system shall merge ERP prices with existing e-shop product data 
3. The system shall calculate VAT-inclusive prices according to Czech tax rates (0%, 15%, 21%)
4. The system shall only synchronize products with purchase price greater than zero
5. The system shall support both full synchronization and dry-run mode for testing
6. The system shall implement 5-minute caching to optimize ERP API performance
7. The system shall generate CSV exports in Windows-1250 encoding for Shoptet integration
8. The system shall provide comprehensive error handling and synchronization tracking
9. The system shall round all prices to 2 decimal places for consistency
10. The system shall support force reload to bypass cache when needed

## Technical Contracts

### Domain Model

```csharp
// Primary entity representing ERP pricing data
public class ProductPriceErp
{
    // Product identification
    public string ProductCode { get; set; }
    
    // Base pricing (without VAT)
    public decimal Price { get; set; }
    public decimal PurchasePrice { get; set; }
    
    // VAT-inclusive pricing (calculated)
    public decimal PriceWithVat { get; set; }
    public decimal PurchasePriceWithVat { get; set; }
    
    // Manufacturing integration
    public int? BoMId { get; set; }
    
    // Business logic
    public bool HasBillOfMaterials => BoMId != null;
    
    // Factory method for creating from FlexiBee data
    public static ProductPriceErp CreateFromFlexiData(ProductPriceFlexiDto flexiData)
    {
        if (flexiData == null)
            throw new BusinessException("FlexiBee price data is required");
        
        if (string.IsNullOrEmpty(flexiData.ProductCode))
            throw new BusinessException("Product code is required");
        
        return new ProductPriceErp
        {
            ProductCode = flexiData.ProductCode,
            Price = flexiData.Price,
            PurchasePrice = flexiData.PurchasePrice,
            PriceWithVat = Math.Round(flexiData.Price * ((100 + flexiData.Vat) / 100), 2),
            PurchasePriceWithVat = Math.Round(flexiData.PurchasePrice * ((100 + flexiData.Vat) / 100), 2),
            BoMId = flexiData.BoMId
        };
    }
    
    // Validation method
    public bool IsValidForSync()
    {
        return !string.IsNullOrEmpty(ProductCode) && 
               PurchasePrice > 0 && 
               Price > 0;
    }
}

// Value object for synchronization tracking
public class ProductPriceSyncData
{
    public IEnumerable<ProductPriceErp> Prices { get; private set; }
    public DateTime SyncTimestamp { get; private set; }
    public string SourceSystem { get; private set; } = "FlexiBee";
    public SyncStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int TotalProducts { get; private set; }
    public int SyncedProducts { get; private set; }
    
    public ProductPriceSyncData(IEnumerable<ProductPriceErp> prices)
    {
        Prices = prices ?? throw new ArgumentNullException(nameof(prices));
        SyncTimestamp = DateTime.UtcNow;
        TotalProducts = prices.Count();
        SyncedProducts = prices.Count(p => p.IsValidForSync());
        Status = SyncStatus.Success;
    }
    
    public ProductPriceSyncData(string errorMessage)
    {
        Prices = Enumerable.Empty<ProductPriceErp>();
        SyncTimestamp = DateTime.UtcNow;
        ErrorMessage = errorMessage;
        Status = SyncStatus.Failed;
        TotalProducts = 0;
        SyncedProducts = 0;
    }
    
    public bool IsSuccessful => Status == SyncStatus.Success && string.IsNullOrEmpty(ErrorMessage);
}

// E-shop product DTO for Shoptet integration
public class ProductPriceEshopDto
{
    public string Code { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public decimal PurchasePrice { get; set; }
    public string Category { get; set; } = "";
    public bool Visible { get; set; } = true;
    
    // Factory method for creating from ERP data
    public static ProductPriceEshopDto CreateFromErpData(ProductPriceErp erpPrice, ProductPriceEshopDto? existingEshopData = null)
    {
        if (erpPrice == null)
            throw new ArgumentNullException(nameof(erpPrice));
        
        return new ProductPriceEshopDto
        {
            Code = erpPrice.ProductCode,
            Name = existingEshopData?.Name ?? erpPrice.ProductCode,
            Price = Math.Round(erpPrice.PriceWithVat, 2),
            PurchasePrice = Math.Round(erpPrice.PurchasePriceWithVat, 2),
            Category = existingEshopData?.Category ?? "",
            Visible = existingEshopData?.Visible ?? true
        };
    }
    
    // Validation method
    public bool IsValidForExport()
    {
        return !string.IsNullOrEmpty(Code) && 
               Price > 0 && 
               PurchasePrice > 0;
    }
}

// FlexiBee DTO for ERP integration
public class ProductPriceFlexiDto
{
    public string ProductCode { get; set; }
    public decimal Price { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal Vat { get; set; }
    public int? BoMId { get; set; }
    
    // VAT rate calculation from Czech descriptors
    public static decimal GetVatRate(string vatCategory)
    {
        return vatCategory?.ToLowerInvariant() switch
        {
            "osvobozeno" => 0m,      // VAT exempt
            "snížená" => 15m,        // Reduced VAT rate
            _ => 21m                 // Standard VAT rate
        };
    }
}

// Enumeration for sync status tracking
public enum SyncStatus
{
    Pending = 0,
    InProgress = 1,
    Success = 2,
    Failed = 3,
    PartialSuccess = 4
}
```

### Application Layer Contracts

```csharp
// Primary application service interface
public interface IProductPriceAppService : IApplicationService
{
    Task<SyncPricesResultDto> SyncPricesAsync(bool dryRun = false, CancellationToken cancellationToken = default);
    Task<ProductPriceQueryResultDto> GetProductPricesAsync(ProductPriceQueryDto query, CancellationToken cancellationToken = default);
    Task<PriceSyncStatusDto> GetSyncStatusAsync(CancellationToken cancellationToken = default);
}

// ERP client interface for FlexiBee integration
public interface IProductPriceErpClient
{
    Task<IEnumerable<ProductPriceErp>> GetAllAsync(bool forceReload = false, CancellationToken cancellationToken = default);
    Task<ProductPriceErp?> GetByProductCodeAsync(string productCode, CancellationToken cancellationToken = default);
    void InvalidateCache();
}

// E-shop client interface for Shoptet integration
public interface IProductPriceEshopClient
{
    Task<IEnumerable<ProductPriceEshopDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<SetProductPricesResultDto> SetAllAsync(IEnumerable<ProductPriceEshopDto> prices, CancellationToken cancellationToken = default);
    Task<ProductPriceEshopDto?> GetByCodeAsync(string productCode, CancellationToken cancellationToken = default);
}

// DTOs for API contracts
public class SyncPricesResultDto
{
    public IEnumerable<ProductPriceEshopDto> ProductData { get; set; } = new List<ProductPriceEshopDto>();
    public string? FilePath { get; set; }
    public DateTime SyncTimestamp { get; set; } = DateTime.UtcNow;
    public int TotalProducts { get; set; }
    public int SyncedProducts { get; set; }
    public int SkippedProducts { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Warnings { get; set; } = new();
    
    public static SyncPricesResultDto CreateSuccess(
        IEnumerable<ProductPriceEshopDto> productData, 
        string? filePath = null)
    {
        var products = productData.ToList();
        return new SyncPricesResultDto
        {
            ProductData = products,
            FilePath = filePath,
            TotalProducts = products.Count,
            SyncedProducts = products.Count(p => p.IsValidForExport()),
            SkippedProducts = products.Count(p => !p.IsValidForExport()),
            IsSuccessful = true
        };
    }
    
    public static SyncPricesResultDto CreateFailure(string errorMessage)
    {
        return new SyncPricesResultDto
        {
            IsSuccessful = false,
            ErrorMessage = errorMessage
        };
    }
}

public class SetProductPricesResultDto
{
    public string? FilePath { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public int ProcessedProducts { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public class ProductPriceQueryDto : PagedAndSortedResultRequestDto
{
    public string? ProductCode { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool? HasBillOfMaterials { get; set; }
    public DateTime? SyncedAfter { get; set; }
}

public class ProductPriceQueryResultDto : PagedResultDto<ProductPriceErp>
{
    public DateTime LastSyncTime { get; set; }
    public int TotalSyncedProducts { get; set; }
}

public class PriceSyncStatusDto
{
    public DateTime? LastSyncTime { get; set; }
    public SyncStatus LastSyncStatus { get; set; }
    public int TotalProducts { get; set; }
    public int SyncedProducts { get; set; }
    public string? LastErrorMessage { get; set; }
    public bool CacheActive { get; set; }
    public DateTime? CacheExpiry { get; set; }
}
```

## Implementation Details

### Application Service Implementation

```csharp
[Authorize]
public class ProductPriceAppService : ApplicationService, IProductPriceAppService
{
    private readonly IProductPriceEshopClient _eshopClient;
    private readonly IProductPriceErpClient _erpClient;
    private readonly ISynchronizationContext _syncContext;
    private readonly ILogger<ProductPriceAppService> _logger;

    public ProductPriceAppService(
        IProductPriceEshopClient eshopClient,
        IProductPriceErpClient erpClient,
        ISynchronizationContext syncContext,
        ILogger<ProductPriceAppService> logger)
    {
        _eshopClient = eshopClient;
        _erpClient = erpClient;
        _syncContext = syncContext;
        _logger = logger;
    }

    public async Task<SyncPricesResultDto> SyncPricesAsync(bool dryRun = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting price synchronization (DryRun: {DryRun})", dryRun);
        
        try
        {
            // Step 1: Get current e-shop data
            var eshopData = (await _eshopClient.GetAllAsync(cancellationToken)).ToList();
            _logger.LogDebug("Retrieved {Count} products from e-shop", eshopData.Count);

            // Step 2: Get authoritative ERP data
            var erpData = (await _erpClient.GetAllAsync(false, cancellationToken)).ToList();
            _logger.LogDebug("Retrieved {Count} products from ERP", erpData.Count);

            // Step 3: Merge data and apply business rules
            var mergedData = MergeErpAndEshopData(erpData, eshopData);
            _logger.LogDebug("Merged data for {Count} products", mergedData.Count);

            // Step 4: Validate and filter for synchronization
            var dataToSync = mergedData.Where(p => p.IsValidForExport()).ToList();
            _logger.LogInformation("Validated {ValidCount} products for sync from {TotalCount} total", 
                dataToSync.Count, mergedData.Count);

            SetProductPricesResultDto? result = null;

            // Step 5: Execute synchronization (unless dry run)
            if (!dryRun)
            {
                result = await _eshopClient.SetAllAsync(dataToSync, cancellationToken);
                
                if (result.IsSuccessful)
                {
                    // Record successful sync
                    _syncContext.Submit(new ProductPriceSyncData(erpData.Where(p => p.IsValidForSync())));
                    _logger.LogInformation("Successfully synchronized {Count} products to e-shop", dataToSync.Count);
                }
                else
                {
                    _logger.LogError("E-shop synchronization failed: {Error}", result.ErrorMessage);
                }
            }
            else
            {
                _logger.LogInformation("Dry run completed - no data synchronized");
            }

            return SyncPricesResultDto.CreateSuccess(mergedData, result?.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Price synchronization failed");
            return SyncPricesResultDto.CreateFailure(ex.Message);
        }
    }

    public async Task<ProductPriceQueryResultDto> GetProductPricesAsync(ProductPriceQueryDto query, CancellationToken cancellationToken = default)
    {
        var allPrices = await _erpClient.GetAllAsync(false, cancellationToken);
        
        // Apply filtering
        var filteredPrices = allPrices.AsQueryable();
        
        if (!string.IsNullOrEmpty(query.ProductCode))
        {
            filteredPrices = filteredPrices.Where(p => p.ProductCode.Contains(query.ProductCode, StringComparison.OrdinalIgnoreCase));
        }
        
        if (query.MinPrice.HasValue)
        {
            filteredPrices = filteredPrices.Where(p => p.PriceWithVat >= query.MinPrice.Value);
        }
        
        if (query.MaxPrice.HasValue)
        {
            filteredPrices = filteredPrices.Where(p => p.PriceWithVat <= query.MaxPrice.Value);
        }
        
        if (query.HasBillOfMaterials.HasValue)
        {
            filteredPrices = filteredPrices.Where(p => p.HasBillOfMaterials == query.HasBillOfMaterials.Value);
        }

        // Apply pagination
        var totalCount = filteredPrices.Count();
        var pagedResults = filteredPrices
            .Skip(query.SkipCount)
            .Take(query.MaxResultCount)
            .ToList();

        return new ProductPriceQueryResultDto
        {
            Items = pagedResults,
            TotalCount = totalCount,
            LastSyncTime = await GetLastSyncTimeAsync(),
            TotalSyncedProducts = allPrices.Count(p => p.IsValidForSync())
        };
    }

    public async Task<PriceSyncStatusDto> GetSyncStatusAsync(CancellationToken cancellationToken = default)
    {
        var lastSync = await _syncContext.GetLastSyncAsync<ProductPriceSyncData>();
        var allPrices = await _erpClient.GetAllAsync(false, cancellationToken);
        
        return new PriceSyncStatusDto
        {
            LastSyncTime = lastSync?.SyncTimestamp,
            LastSyncStatus = lastSync?.Status ?? SyncStatus.Pending,
            TotalProducts = allPrices.Count(),
            SyncedProducts = allPrices.Count(p => p.IsValidForSync()),
            LastErrorMessage = lastSync?.ErrorMessage,
            CacheActive = true, // Determined by cache implementation
            CacheExpiry = DateTime.Now.AddMinutes(5) // From cache settings
        };
    }

    private List<ProductPriceEshopDto> MergeErpAndEshopData(
        IEnumerable<ProductPriceErp> erpData, 
        IEnumerable<ProductPriceEshopDto> eshopData)
    {
        var eshopDict = eshopData.ToDictionary(e => e.Code, e => e, StringComparer.OrdinalIgnoreCase);
        var result = new List<ProductPriceEshopDto>();

        foreach (var erpProduct in erpData)
        {
            if (!erpProduct.IsValidForSync())
                continue;

            // Find existing e-shop data
            eshopDict.TryGetValue(erpProduct.ProductCode, out var existingEshopData);

            // Create merged product data
            var mergedProduct = ProductPriceEshopDto.CreateFromErpData(erpProduct, existingEshopData);
            result.Add(mergedProduct);
        }

        // Add e-shop products that don't exist in ERP (preserve existing)
        foreach (var eshopProduct in eshopData)
        {
            if (!erpData.Any(e => string.Equals(e.ProductCode, eshopProduct.Code, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(eshopProduct);
            }
        }

        return result;
    }

    private async Task<DateTime?> GetLastSyncTimeAsync()
    {
        var lastSync = await _syncContext.GetLastSyncAsync<ProductPriceSyncData>();
        return lastSync?.SyncTimestamp;
    }
}
```

### FlexiBee ERP Client Implementation

```csharp
public class FlexiProductPriceErpClient : UserQueryClient<ProductPriceFlexiDto>, IProductPriceErpClient
{
    private readonly IMemoryCache _cache;
    private readonly ISynchronizationContext _syncContext;
    private readonly ILogger<FlexiProductPriceErpClient> _logger;
    private const string CacheKey = "FlexiProductPrices";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);

    public FlexiProductPriceErpClient(
        FlexiBeeSettings connection,
        IHttpClientFactory httpClientFactory,
        IResultHandler resultHandler,
        IMemoryCache cache,
        ISynchronizationContext syncContext,
        ILogger<FlexiProductPriceErpClient> logger)
        : base(connection, httpClientFactory, resultHandler, logger)
    {
        _cache = cache;
        _syncContext = syncContext;
        _logger = logger;
    }

    protected override int QueryId => 41; // FlexiBee UserQuery for product prices

    public async Task<IEnumerable<ProductPriceErp>> GetAllAsync(bool forceReload = false, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(forceReload);
        
        if (!forceReload && _cache.TryGetValue(cacheKey, out IList<ProductPriceFlexiDto>? cachedData))
        {
            _logger.LogDebug("Retrieved {Count} products from cache", cachedData!.Count);
            return ConvertToDomainModel(cachedData);
        }

        try
        {
            _logger.LogDebug("Fetching product prices from FlexiBee (ForceReload: {ForceReload})", forceReload);
            
            var flexiData = await GetAsync(0, cancellationToken);
            _cache.Set(cacheKey, flexiData, DateTimeOffset.Now.Add(CacheExpiration));
            
            _logger.LogInformation("Retrieved {Count} products from FlexiBee ERP", flexiData.Count);
            
            var domainData = ConvertToDomainModel(flexiData);
            
            // Submit sync data for tracking
            _syncContext.Submit(new ProductPriceSyncData(domainData.Where(p => p.IsValidForSync())));
            
            return domainData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve product prices from FlexiBee");
            
            // Submit error for tracking
            _syncContext.Submit(new ProductPriceSyncData($"FlexiBee retrieval failed: {ex.Message}"));
            
            throw new BusinessException("Failed to retrieve product prices from ERP system", ex);
        }
    }

    public async Task<ProductPriceErp?> GetByProductCodeAsync(string productCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(productCode))
            return null;

        var allPrices = await GetAllAsync(false, cancellationToken);
        return allPrices.FirstOrDefault(p => string.Equals(p.ProductCode, productCode, StringComparison.OrdinalIgnoreCase));
    }

    public void InvalidateCache()
    {
        _cache.Remove(CacheKey);
        _cache.Remove(GetCacheKey(true));
        _logger.LogDebug("Product price cache invalidated");
    }

    private string GetCacheKey(bool forceReload) => forceReload ? $"{CacheKey}_ForceReload" : CacheKey;

    private IEnumerable<ProductPriceErp> ConvertToDomainModel(IEnumerable<ProductPriceFlexiDto> flexiData)
    {
        return flexiData
            .Where(f => !string.IsNullOrEmpty(f.ProductCode))
            .Select(f => ProductPriceErp.CreateFromFlexiData(f))
            .ToList();
    }

    public Task<IList<ProductPriceFlexiDto>> GetAsync(int limit = 0, CancellationToken cancellationToken = default) =>
        GetAsync(new Dictionary<string, string> { { LimitParamName, limit.ToString() } }, cancellationToken);
}
```

### Shoptet E-shop Client Implementation

```csharp
public class ShoptetPriceClient : IProductPriceEshopClient
{
    private readonly HttpClient _httpClient;
    private readonly ShoptetSettings _settings;
    private readonly ILogger<ShoptetPriceClient> _logger;
    private readonly Encoding _csvEncoding = Encoding.GetEncoding("windows-1250");

    public ShoptetPriceClient(
        HttpClient httpClient,
        ShoptetSettings settings,
        ILogger<ShoptetPriceClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IEnumerable<ProductPriceEshopDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Importing current prices from Shoptet");
            
            var response = await _httpClient.GetAsync(_settings.PriceImportUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var csvContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var products = ParseCsvContent(csvContent);
            
            _logger.LogInformation("Imported {Count} products from Shoptet", products.Count());
            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import prices from Shoptet");
            throw new BusinessException("Failed to import prices from e-shop", ex);
        }
    }

    public async Task<SetProductPricesResultDto> SetAllAsync(IEnumerable<ProductPriceEshopDto> prices, CancellationToken cancellationToken = default)
    {
        try
        {
            var pricesList = prices.ToList();
            _logger.LogDebug("Exporting {Count} products to Shoptet", pricesList.Count);

            var csvContent = GenerateCsvContent(pricesList);
            var fileName = $"prices_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);

            // Save to file with correct encoding
            await File.WriteAllTextAsync(filePath, csvContent, _csvEncoding, cancellationToken);

            // Upload to Shoptet
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(File.ReadAllBytes(filePath));
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
            content.Add(fileContent, "file", fileName);

            var response = await _httpClient.PostAsync(_settings.PriceExportUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Successfully exported {Count} products to Shoptet", pricesList.Count);

            return new SetProductPricesResultDto
            {
                FilePath = filePath,
                IsSuccessful = true,
                ProcessedProducts = pricesList.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export prices to Shoptet");
            return new SetProductPricesResultDto
            {
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<ProductPriceEshopDto?> GetByCodeAsync(string productCode, CancellationToken cancellationToken = default)
    {
        var allProducts = await GetAllAsync(cancellationToken);
        return allProducts.FirstOrDefault(p => string.Equals(p.Code, productCode, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<ProductPriceEshopDto> ParseCsvContent(string csvContent)
    {
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var products = new List<ProductPriceEshopDto>();

        foreach (var line in lines.Skip(1)) // Skip header
        {
            var fields = line.Split(';');
            if (fields.Length >= 5)
            {
                if (decimal.TryParse(fields[2], out var price) && 
                    decimal.TryParse(fields[3], out var purchasePrice))
                {
                    products.Add(new ProductPriceEshopDto
                    {
                        Code = fields[0].Trim('"'),
                        Name = fields[1].Trim('"'),
                        Price = price,
                        PurchasePrice = purchasePrice,
                        Category = fields.Length > 4 ? fields[4].Trim('"') : "",
                        Visible = fields.Length > 5 ? bool.Parse(fields[5].Trim('"')) : true
                    });
                }
            }
        }

        return products;
    }

    private string GenerateCsvContent(IEnumerable<ProductPriceEshopDto> products)
    {
        var csv = new StringBuilder();
        csv.AppendLine("\"Code\";\"Name\";\"Price\";\"PurchasePrice\";\"Category\";\"Visible\"");

        foreach (var product in products)
        {
            csv.AppendLine($"\"{product.Code}\";\"{product.Name}\";\"{product.Price:F2}\";\"{product.PurchasePrice:F2}\";\"{product.Category}\";\"{product.Visible}\"");
        }

        return csv.ToString();
    }
}
```

## Business Logic Implementation

### VAT Calculation Engine

```csharp
public static class VatCalculationEngine
{
    private static readonly Dictionary<string, decimal> CzechVatRates = new()
    {
        { "osvobozeno", 0m },    // VAT exempt
        { "snížená", 15m },      // Reduced VAT rate  
        { "základní", 21m }      // Standard VAT rate
    };

    public static decimal GetVatRate(string vatCategory)
    {
        if (string.IsNullOrEmpty(vatCategory))
            return 21m; // Default to standard rate

        var normalizedCategory = vatCategory.ToLowerInvariant().Trim();
        return CzechVatRates.TryGetValue(normalizedCategory, out var rate) ? rate : 21m;
    }

    public static decimal CalculatePriceWithVat(decimal basePrice, decimal vatRate)
    {
        if (basePrice < 0)
            throw new ArgumentException("Base price cannot be negative", nameof(basePrice));

        if (vatRate < 0 || vatRate > 100)
            throw new ArgumentException("VAT rate must be between 0 and 100", nameof(vatRate));

        var multiplier = (100 + vatRate) / 100;
        return Math.Round(basePrice * multiplier, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal CalculateVatAmount(decimal basePrice, decimal vatRate)
    {
        var priceWithVat = CalculatePriceWithVat(basePrice, vatRate);
        return priceWithVat - basePrice;
    }

    public static VatCalculationResult CalculateVatBreakdown(decimal basePrice, string vatCategory)
    {
        var vatRate = GetVatRate(vatCategory);
        var priceWithVat = CalculatePriceWithVat(basePrice, vatRate);
        var vatAmount = priceWithVat - basePrice;

        return new VatCalculationResult
        {
            BasePrice = basePrice,
            VatRate = vatRate,
            VatAmount = vatAmount,
            PriceWithVat = priceWithVat,
            VatCategory = vatCategory
        };
    }
}

public class VatCalculationResult
{
    public decimal BasePrice { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal PriceWithVat { get; set; }
    public string VatCategory { get; set; }
}
```

### Price Merge Engine

```csharp
public static class PriceMergeEngine
{
    public static ProductPriceEshopDto MergeErpAndEshopData(
        ProductPriceErp erpPrice, 
        ProductPriceEshopDto? eshopPrice = null)
    {
        if (erpPrice == null)
            throw new ArgumentNullException(nameof(erpPrice));

        return new ProductPriceEshopDto
        {
            Code = erpPrice.ProductCode,
            Name = eshopPrice?.Name ?? erpPrice.ProductCode,
            Price = Math.Round(erpPrice.PriceWithVat, 2),
            PurchasePrice = Math.Round(erpPrice.PurchasePriceWithVat, 2),
            Category = eshopPrice?.Category ?? "",
            Visible = eshopPrice?.Visible ?? true
        };
    }

    public static List<ProductPriceEshopDto> MergePriceLists(
        IEnumerable<ProductPriceErp> erpPrices,
        IEnumerable<ProductPriceEshopDto> eshopPrices)
    {
        var eshopDict = eshopPrices.ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);
        var result = new List<ProductPriceEshopDto>();

        // Process ERP prices (authoritative source)
        foreach (var erpPrice in erpPrices.Where(p => p.IsValidForSync()))
        {
            eshopDict.TryGetValue(erpPrice.ProductCode, out var existingEshopPrice);
            var mergedPrice = MergeErpAndEshopData(erpPrice, existingEshopPrice);
            result.Add(mergedPrice);
            
            // Remove from dictionary to track processed items
            eshopDict.Remove(erpPrice.ProductCode);
        }

        // Add remaining e-shop prices that don't exist in ERP
        result.AddRange(eshopDict.Values);

        return result;
    }

    public static SyncSummary AnalyzeSyncImpact(
        IEnumerable<ProductPriceErp> erpPrices,
        IEnumerable<ProductPriceEshopDto> eshopPrices)
    {
        var erpDict = erpPrices.ToDictionary(e => e.ProductCode, StringComparer.OrdinalIgnoreCase);
        var eshopDict = eshopPrices.ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);

        var summary = new SyncSummary
        {
            TotalErpProducts = erpPrices.Count(),
            TotalEshopProducts = eshopPrices.Count(),
            ValidErpProducts = erpPrices.Count(p => p.IsValidForSync()),
            MatchingProducts = erpDict.Keys.Intersect(eshopDict.Keys, StringComparer.OrdinalIgnoreCase).Count(),
            ErpOnlyProducts = erpDict.Keys.Except(eshopDict.Keys, StringComparer.OrdinalIgnoreCase).Count(),
            EshopOnlyProducts = eshopDict.Keys.Except(erpDict.Keys, StringComparer.OrdinalIgnoreCase).Count()
        };

        // Calculate price changes
        foreach (var commonProduct in erpDict.Keys.Intersect(eshopDict.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var erpPrice = erpDict[commonProduct];
            var eshopPrice = eshopDict[commonProduct];
            
            if (Math.Abs(erpPrice.PriceWithVat - eshopPrice.Price) > 0.01m)
            {
                summary.PriceChanges++;
            }
        }

        return summary;
    }
}

public class SyncSummary
{
    public int TotalErpProducts { get; set; }
    public int TotalEshopProducts { get; set; }
    public int ValidErpProducts { get; set; }
    public int MatchingProducts { get; set; }
    public int ErpOnlyProducts { get; set; }
    public int EshopOnlyProducts { get; set; }
    public int PriceChanges { get; set; }
}
```

## Happy Day Scenario

1. **Sync Initiation**: Pricing manager triggers daily price synchronization
2. **Cache Check**: System checks FlexiBee cache (5-minute expiration)
3. **ERP Data Retrieval**: If cache miss, fetch fresh data from FlexiBee UserQuery 41
4. **VAT Calculation**: Apply Czech VAT rates to calculate inclusive prices
5. **E-shop Data Import**: Retrieve current product data from Shoptet CSV
6. **Data Merging**: Merge ERP (authoritative) with e-shop data, preserving non-price fields
7. **Business Rule Application**: Filter products with PurchasePrice > 0
8. **Price Validation**: Round all prices to 2 decimal places
9. **CSV Generation**: Create Windows-1250 encoded export file
10. **E-shop Upload**: Upload updated prices to Shoptet platform
11. **Sync Tracking**: Record successful synchronization with metadata
12. **Cache Update**: Update cache with fresh ERP data (5-minute expiration)

## Error Handling

### Data Validation Errors
- **Invalid Product Codes**: Skip products with missing/invalid codes
- **Negative Prices**: Log warnings and exclude from sync
- **VAT Calculation Errors**: Use default 21% rate with warning
- **Currency Format Issues**: Apply standard 2-decimal rounding

### Integration Errors
- **FlexiBee API Failures**: Use cached data if available, otherwise fail gracefully
- **Shoptet Upload Failures**: Retry mechanism with exponential backoff
- **Network Timeouts**: Configurable timeout settings with circuit breaker
- **Authentication Issues**: Clear credentials and prompt re-authentication

### Business Logic Errors
- **Price Inconsistencies**: Log discrepancies for manual review
- **Missing Product Data**: Handle gracefully with default values
- **VAT Category Mapping**: Default to standard rate for unknown categories
- **Encoding Issues**: Ensure proper Windows-1250 encoding for Czech characters

### System Errors
- **Cache Failures**: Fallback to direct ERP calls
- **File System Errors**: Use alternative temporary directories
- **Memory Issues**: Implement chunked processing for large datasets
- **Database Connectivity**: Queue operations for later retry

## Business Rules

### Synchronization Rules
1. **ERP Authority**: FlexiBee ERP prices override e-shop prices
2. **Purchase Price Filter**: Only sync products with PurchasePrice > 0
3. **Price Rounding**: All prices rounded to 2 decimal places
4. **VAT Inclusion**: E-shop prices include VAT, ERP prices exclude VAT
5. **Product Matching**: Case-insensitive product code matching

### VAT Calculation Rules
1. **Czech VAT Rates**: 0% (exempt), 15% (reduced), 21% (standard)
2. **Default Rate**: 21% for unknown/invalid VAT categories
3. **Calculation Formula**: Price × ((100 + VAT%) / 100)
4. **Rounding Method**: Away from zero for consistency

### Caching Rules
1. **Cache Duration**: 5 minutes for ERP data
2. **Force Reload**: Bypass cache for critical operations
3. **Cache Invalidation**: Manual clearing capability
4. **Cache Isolation**: Separate cache keys for different reload types

### Performance Rules
1. **Batch Processing**: Process products in configurable batches
2. **Async Operations**: Non-blocking I/O for all external calls
3. **Timeout Handling**: Configurable timeouts for each integration point
4. **Resource Pooling**: HTTP client reuse and connection pooling

## Persistence Layer Requirements

### Temporary Storage
- CSV files stored in system temp directory
- File naming convention: `prices_yyyyMMdd_HHmmss.csv`
- Windows-1250 encoding for proper Czech character support
- Automatic cleanup of files older than 24 hours

### Cache Storage
- In-memory cache for ERP data (5-minute expiration)
- Configurable cache settings via application configuration
- Cache hit/miss metrics for monitoring
- Manual cache invalidation capability

### Sync Tracking
- Synchronization metadata stored in database
- Success/failure status with timestamps
- Error message logging for debugging
- Performance metrics (duration, product counts)

## Integration Requirements

### FlexiBee ERP Integration
- **UserQuery 41**: Dedicated query for product pricing data
- **Authentication**: FlexiBee API credentials
- **Rate Limiting**: Respect API limits with exponential backoff
- **Field Mapping**: kod→ProductCode, cena→Price, cenanakup→PurchasePrice

### Shoptet E-commerce Integration  
- **CSV Format**: Semicolon-delimited, Windows-1250 encoded
- **File Upload**: HTTP multipart/form-data
- **Authentication**: Shoptet API credentials
- **Error Handling**: Parse response codes and error messages

### Monitoring Integration
- **Logging**: Structured logging with Serilog
- **Metrics**: Sync performance and success rates
- **Alerting**: Failed sync notifications
- **Health Checks**: Integration endpoint availability

## Performance Requirements
- Process 10,000+ products within 2 minutes
- Support concurrent access by multiple users
- Handle file uploads up to 50MB
- Maintain sub-second cache response times
- Scale linearly with product catalog size
- Provide real-time sync status updates