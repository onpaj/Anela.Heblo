# Manufacturing Cost Recalculation User Story

## Feature Overview
The Manufacturing Cost Recalculation feature provides Bill of Materials (BoM) based cost calculation for manufactured products. This integration with FlexiBee ERP enables automatic recalculation of purchase prices based on current material costs, supports both individual product and bulk recalculation operations, and provides comprehensive validation and error handling for manufacturing cost management.

## Business Requirements

### Primary Use Case
As a manufacturing cost controller, I want to automatically recalculate purchase prices for manufactured products based on their Bill of Materials and current material costs so that product pricing accurately reflects current manufacturing costs, enabling accurate profitability analysis and competitive pricing strategies.

### Acceptance Criteria
1. The system shall recalculate purchase prices for products with valid Bill of Materials
2. The system shall support individual product recalculation by product code
3. The system shall support bulk recalculation for all products with BoM
4. The system shall integrate with FlexiBee ERP BoM API for cost calculations
5. The system shall validate product existence before recalculation attempts
6. The system shall provide clear error messages for invalid operations
7. The system shall preserve original purchase prices during the process
8. The system shall handle missing or invalid BoM data gracefully
9. The system shall support force reload to ensure current ERP data
10. The system shall provide operation status feedback with appropriate HTTP responses

## Technical Contracts

### Domain Model

```csharp
// Manufacturing cost recalculation request
public class RecalculatePurchasePriceRequestDto
{
    // Individual product recalculation
    public string? ProductCode { get; set; }
    
    // Bulk recalculation flag
    public bool RecalculateEverything { get; set; } = false;
    
    // Force reload ERP data
    public bool ForceReload { get; set; } = false;
    
    // Validation method
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(ProductCode) || RecalculateEverything;
    }
    
    // Business rule validation
    public void Validate()
    {
        if (!IsValid())
        {
            throw new BusinessException(
                $"{nameof(ProductCode)} or {nameof(RecalculateEverything)}=true must be set");
        }
    }
}

// Manufacturing cost recalculation result
public class ManufacturingCostRecalculationResult
{
    public string ProductCode { get; set; }
    public decimal OriginalPurchasePrice { get; set; }
    public decimal RecalculatedPurchasePrice { get; set; }
    public decimal CostDifference => RecalculatedPurchasePrice - OriginalPurchasePrice;
    public decimal CostDifferencePercentage => OriginalPurchasePrice > 0 
        ? (CostDifference / OriginalPurchasePrice) * 100 
        : 0;
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime RecalculatedAt { get; set; } = DateTime.UtcNow;
    public int? BoMId { get; set; }
    public List<MaterialCostComponent> MaterialComponents { get; set; } = new();
    
    public static ManufacturingCostRecalculationResult CreateSuccess(
        string productCode,
        decimal originalPrice,
        decimal recalculatedPrice,
        int bomId,
        List<MaterialCostComponent> components)
    {
        return new ManufacturingCostRecalculationResult
        {
            ProductCode = productCode,
            OriginalPurchasePrice = originalPrice,
            RecalculatedPurchasePrice = recalculatedPrice,
            IsSuccessful = true,
            BoMId = bomId,
            MaterialComponents = components
        };
    }
    
    public static ManufacturingCostRecalculationResult CreateFailure(
        string productCode,
        string errorMessage)
    {
        return new ManufacturingCostRecalculationResult
        {
            ProductCode = productCode,
            IsSuccessful = false,
            ErrorMessage = errorMessage
        };
    }
}

// Material cost component for BoM breakdown
public class MaterialCostComponent
{
    public string MaterialCode { get; set; }
    public string MaterialName { get; set; }
    public double Quantity { get; set; }
    public string Unit { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost => (decimal)Quantity * UnitCost;
    public decimal CostPercentage { get; set; }
    
    public static MaterialCostComponent Create(
        string materialCode,
        string materialName,
        double quantity,
        string unit,
        decimal unitCost,
        decimal totalProductCost)
    {
        var component = new MaterialCostComponent
        {
            MaterialCode = materialCode,
            MaterialName = materialName,
            Quantity = quantity,
            Unit = unit,
            UnitCost = unitCost
        };
        
        component.CostPercentage = totalProductCost > 0 
            ? (component.TotalCost / totalProductCost) * 100 
            : 0;
        
        return component;
    }
}

// Bulk recalculation summary
public class BulkRecalculationSummaryDto
{
    public int TotalProductsProcessed { get; set; }
    public int SuccessfulRecalculations { get; set; }
    public int FailedRecalculations { get; set; }
    public List<ManufacturingCostRecalculationResult> Results { get; set; } = new();
    public decimal TotalCostAdjustment { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan ProcessingDuration { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    
    public bool IsSuccessful => FailedRecalculations == 0;
    public decimal AverageCostChange => SuccessfulRecalculations > 0 
        ? Results.Where(r => r.IsSuccessful).Average(r => r.CostDifferencePercentage) 
        : 0;
}

// Enhanced ProductPriceErp with recalculation support
public partial class ProductPriceErp
{
    public decimal OriginalPurchasePrice { get; set; }
    public DateTime? LastRecalculationDate { get; set; }
    public bool HasBeenRecalculated => LastRecalculationDate.HasValue;
    
    // Business validation for recalculation eligibility
    public bool CanBeRecalculated()
    {
        return HasBillOfMaterials && !string.IsNullOrEmpty(ProductCode);
    }
    
    // Update purchase price with audit trail
    public void UpdatePurchasePrice(decimal newPurchasePrice, decimal vatRate = 21m)
    {
        if (newPurchasePrice < 0)
            throw new BusinessException("Purchase price cannot be negative");
        
        OriginalPurchasePrice = PurchasePrice;
        PurchasePrice = newPurchasePrice;
        PurchasePriceWithVat = VatCalculationEngine.CalculatePriceWithVat(newPurchasePrice, vatRate);
        LastRecalculationDate = DateTime.UtcNow;
    }
    
    // Reset to original price
    public void ResetToOriginalPrice()
    {
        if (OriginalPurchasePrice > 0)
        {
            PurchasePrice = OriginalPurchasePrice;
            PurchasePriceWithVat = VatCalculationEngine.CalculatePriceWithVat(OriginalPurchasePrice, 21m);
            LastRecalculationDate = null;
        }
    }
}
```

### Application Layer Contracts

```csharp
// Enhanced application service interface
public interface IProductPriceAppService : IApplicationService
{
    // Existing price sync method
    Task<SyncPricesResultDto> SyncPricesAsync(bool dryRun = false, CancellationToken cancellationToken = default);
    
    // Manufacturing cost recalculation methods
    Task<IActionResult> RecalculatePurchasePriceAsync(RecalculatePurchasePriceRequestDto request, CancellationToken cancellationToken = default);
    Task<ManufacturingCostRecalculationResult> RecalculateProductCostAsync(string productCode, CancellationToken cancellationToken = default);
    Task<BulkRecalculationSummaryDto> RecalculateAllProductCostsAsync(bool dryRun = false, CancellationToken cancellationToken = default);
    Task<List<ProductPriceErp>> GetProductsWithBomAsync(CancellationToken cancellationToken = default);
    Task<ManufacturingCostRecalculationResult> GetRecalculationHistoryAsync(string productCode, CancellationToken cancellationToken = default);
}

// Bill of Materials client interface
public interface IBoMClient
{
    Task<bool> RecalculatePurchasePrice(int bomId, CancellationToken cancellationToken = default);
    Task<BoMCostBreakdownDto> GetCostBreakdown(int bomId, CancellationToken cancellationToken = default);
    Task<bool> ValidateBoM(int bomId, CancellationToken cancellationToken = default);
    Task<List<MaterialCostComponent>> GetMaterialComponents(int bomId, CancellationToken cancellationToken = default);
}

// BoM cost breakdown DTO
public class BoMCostBreakdownDto
{
    public int BoMId { get; set; }
    public string ProductCode { get; set; }
    public decimal TotalMaterialCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal OverheadCost { get; set; }
    public decimal TotalManufacturingCost { get; set; }
    public List<MaterialCostComponent> Materials { get; set; } = new();
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    
    public bool IsValid => TotalManufacturingCost > 0 && Materials.Any();
}

// Manufacturing cost analysis DTO
public class ManufacturingCostAnalysisDto
{
    public string ProductCode { get; set; }
    public decimal CurrentPurchasePrice { get; set; }
    public decimal CalculatedManufacturingCost { get; set; }
    public decimal CostVariance => CalculatedManufacturingCost - CurrentPurchasePrice;
    public decimal VariancePercentage => CurrentPurchasePrice > 0 
        ? (CostVariance / CurrentPurchasePrice) * 100 
        : 0;
    public bool RequiresRecalculation => Math.Abs(VariancePercentage) > 5; // 5% threshold
    public List<MaterialCostComponent> TopCostDrivers { get; set; } = new();
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}
```

## Implementation Details

### Enhanced Application Service Implementation

```csharp
[Authorize]
public partial class ProductPriceAppService : ApplicationService, IProductPriceAppService
{
    private readonly IProductPriceEshopClient _eshopClient;
    private readonly IProductPriceErpClient _erpClient;
    private readonly IBoMClient _bomClient;
    private readonly ISynchronizationContext _syncContext;
    private readonly ILogger<ProductPriceAppService> _logger;

    public ProductPriceAppService(
        IProductPriceEshopClient eshopClient,
        IProductPriceErpClient erpClient,
        IBoMClient bomClient,
        ISynchronizationContext syncContext,
        ILogger<ProductPriceAppService> logger)
    {
        _eshopClient = eshopClient;
        _erpClient = erpClient;
        _bomClient = bomClient;
        _syncContext = syncContext;
        _logger = logger;
    }

    public async Task<IActionResult> RecalculatePurchasePriceAsync(RecalculatePurchasePriceRequestDto request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting purchase price recalculation (ProductCode: {ProductCode}, BulkRecalc: {Bulk})", 
            request.ProductCode, request.RecalculateEverything);
        
        try
        {
            // Validate request
            request.Validate();
            
            // Get products from ERP
            var products = await _erpClient.GetAllAsync(request.ForceReload, cancellationToken);
            
            if (!string.IsNullOrEmpty(request.ProductCode))
            {
                // Individual product recalculation
                return await RecalculateIndividualProduct(request.ProductCode, products, cancellationToken);
            }
            else if (request.RecalculateEverything)
            {
                // Bulk recalculation
                return await RecalculateBulkProducts(products, cancellationToken);
            }
            
            return new BadRequestObjectResult("Invalid recalculation request");
        }
        catch (BusinessException ex)
        {
            _logger.LogWarning("Business validation failed: {Message}", ex.Message);
            return new BadRequestObjectResult(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Purchase price recalculation failed");
            return new StatusCodeResult(500);
        }
    }

    public async Task<ManufacturingCostRecalculationResult> RecalculateProductCostAsync(string productCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(productCode))
            return ManufacturingCostRecalculationResult.CreateFailure("", "Product code is required");
        
        try
        {
            var products = await _erpClient.GetAllAsync(false, cancellationToken);
            var product = products.FirstOrDefault(p => string.Equals(p.ProductCode, productCode, StringComparison.OrdinalIgnoreCase));
            
            if (product == null)
            {
                return ManufacturingCostRecalculationResult.CreateFailure(productCode, "Product not found");
            }
            
            if (!product.CanBeRecalculated())
            {
                return ManufacturingCostRecalculationResult.CreateFailure(productCode, "Product does not have a valid Bill of Materials");
            }
            
            // Get cost breakdown from BoM
            var costBreakdown = await _bomClient.GetCostBreakdown(product.BoMId!.Value, cancellationToken);
            
            if (!costBreakdown.IsValid)
            {
                return ManufacturingCostRecalculationResult.CreateFailure(productCode, "Invalid BoM cost data");
            }
            
            // Perform recalculation
            var recalculationSuccess = await _bomClient.RecalculatePurchasePrice(product.BoMId.Value, cancellationToken);
            
            if (!recalculationSuccess)
            {
                return ManufacturingCostRecalculationResult.CreateFailure(productCode, "BoM recalculation failed in ERP system");
            }
            
            var result = ManufacturingCostRecalculationResult.CreateSuccess(
                productCode,
                product.PurchasePrice,
                costBreakdown.TotalManufacturingCost,
                product.BoMId.Value,
                costBreakdown.Materials);
            
            _logger.LogInformation("Successfully recalculated cost for product {ProductCode}: {Original} → {New} ({Percentage:F2}%)", 
                productCode, result.OriginalPurchasePrice, result.RecalculatedPurchasePrice, result.CostDifferencePercentage);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recalculate cost for product {ProductCode}", productCode);
            return ManufacturingCostRecalculationResult.CreateFailure(productCode, ex.Message);
        }
    }

    public async Task<BulkRecalculationSummaryDto> RecalculateAllProductCostsAsync(bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var summary = new BulkRecalculationSummaryDto();
        
        try
        {
            _logger.LogInformation("Starting bulk recalculation (DryRun: {DryRun})", dryRun);
            
            var products = await _erpClient.GetAllAsync(false, cancellationToken);
            var bomProducts = products.Where(p => p.CanBeRecalculated()).ToList();
            
            summary.TotalProductsProcessed = bomProducts.Count;
            
            foreach (var product in bomProducts)
            {
                try
                {
                    var result = await RecalculateProductCostAsync(product.ProductCode, cancellationToken);
                    summary.Results.Add(result);
                    
                    if (result.IsSuccessful)
                    {
                        summary.SuccessfulRecalculations++;
                        summary.TotalCostAdjustment += result.CostDifference;
                        
                        if (!dryRun)
                        {
                            // Update product in ERP (actual recalculation already done in RecalculateProductCostAsync)
                            _logger.LogDebug("Updated cost for {ProductCode} in ERP", product.ProductCode);
                        }
                    }
                    else
                    {
                        summary.FailedRecalculations++;
                        summary.Errors.Add($"{product.ProductCode}: {result.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    summary.FailedRecalculations++;
                    summary.Errors.Add($"{product.ProductCode}: {ex.Message}");
                    _logger.LogError(ex, "Failed to process product {ProductCode}", product.ProductCode);
                }
            }
            
            stopwatch.Stop();
            summary.ProcessingDuration = stopwatch.Elapsed;
            
            _logger.LogInformation("Bulk recalculation completed: {Success}/{Total} successful in {Duration}", 
                summary.SuccessfulRecalculations, summary.TotalProductsProcessed, summary.ProcessingDuration);
            
            return summary;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            summary.ProcessingDuration = stopwatch.Elapsed;
            summary.Errors.Add($"Bulk operation failed: {ex.Message}");
            _logger.LogError(ex, "Bulk recalculation failed");
            return summary;
        }
    }

    public async Task<List<ProductPriceErp>> GetProductsWithBomAsync(CancellationToken cancellationToken = default)
    {
        var products = await _erpClient.GetAllAsync(false, cancellationToken);
        return products.Where(p => p.HasBillOfMaterials).ToList();
    }

    public async Task<ManufacturingCostRecalculationResult> GetRecalculationHistoryAsync(string productCode, CancellationToken cancellationToken = default)
    {
        // This would typically fetch from a database or audit log
        // For now, return current calculation status
        return await RecalculateProductCostAsync(productCode, cancellationToken);
    }

    private async Task<IActionResult> RecalculateIndividualProduct(string productCode, IEnumerable<ProductPriceErp> products, CancellationToken cancellationToken)
    {
        var product = products.FirstOrDefault(p => string.Equals(p.ProductCode, productCode, StringComparison.OrdinalIgnoreCase));
        
        if (product == null)
        {
            _logger.LogWarning("Product {ProductCode} not found", productCode);
            return new NotFoundObjectResult($"Product {productCode} not found");
        }
        
        if (!product.CanBeRecalculated())
        {
            _logger.LogWarning("Product {ProductCode} does not have a valid BoM", productCode);
            return new BadRequestObjectResult($"Product {productCode} does not have a valid Bill of Materials");
        }
        
        var recalculationSuccess = await _bomClient.RecalculatePurchasePrice(product.BoMId!.Value, cancellationToken);
        
        if (recalculationSuccess)
        {
            _logger.LogInformation("Successfully recalculated purchase price for product {ProductCode}", productCode);
            return new OkResult();
        }
        else
        {
            _logger.LogError("Failed to recalculate purchase price for product {ProductCode}", productCode);
            return new BadRequestObjectResult($"Failed to recalculate purchase price for product {productCode}");
        }
    }

    private async Task<IActionResult> RecalculateBulkProducts(IEnumerable<ProductPriceErp> products, CancellationToken cancellationToken)
    {
        var bomProducts = products.Where(p => p.CanBeRecalculated()).ToList();
        var results = new List<IStatusCodeActionResult>();
        
        _logger.LogInformation("Starting bulk recalculation for {Count} products with BoM", bomProducts.Count);
        
        foreach (var product in bomProducts)
        {
            try
            {
                var result = await _bomClient.RecalculatePurchasePrice(product.BoMId!.Value, cancellationToken);
                results.Add(result ? new OkResult() : new BadRequestObjectResult($"Failed: {product.ProductCode}"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recalculating {ProductCode}", product.ProductCode);
                results.Add(new StatusCodeResult(500));
            }
        }
        
        // Return the worst status code (highest status code indicates most severe error)
        var worstResult = results.MaxBy(r => r.StatusCode ?? 200);
        
        _logger.LogInformation("Bulk recalculation completed with status: {Status}", worstResult?.StatusCode);
        
        return worstResult ?? new NoContentResult();
    }
}
```

### Bill of Materials Client Implementation

```csharp
public class FlexiBeeBoMClient : IBoMClient
{
    private readonly IBoMApiClient _apiClient;
    private readonly ILogger<FlexiBeeBoMClient> _logger;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(10);

    public FlexiBeeBoMClient(
        IBoMApiClient apiClient,
        ILogger<FlexiBeeBoMClient> logger,
        IMemoryCache cache)
    {
        _apiClient = apiClient;
        _logger = logger;
        _cache = cache;
    }

    public async Task<bool> RecalculatePurchasePrice(int bomId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Recalculating purchase price for BoM {BoMId}", bomId);
            
            var result = await _apiClient.RecalculatePurchasePriceAsync(bomId, cancellationToken);
            
            if (result)
            {
                // Invalidate cache after successful recalculation
                InvalidateBomCache(bomId);
                _logger.LogInformation("Successfully recalculated BoM {BoMId}", bomId);
            }
            else
            {
                _logger.LogWarning("Failed to recalculate BoM {BoMId}", bomId);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recalculating BoM {BoMId}", bomId);
            throw new BusinessException($"Failed to recalculate BoM {bomId}: {ex.Message}", ex);
        }
    }

    public async Task<BoMCostBreakdownDto> GetCostBreakdown(int bomId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"BoM_Breakdown_{bomId}";
        
        if (_cache.TryGetValue(cacheKey, out BoMCostBreakdownDto? cachedBreakdown))
        {
            return cachedBreakdown!;
        }
        
        try
        {
            _logger.LogDebug("Fetching cost breakdown for BoM {BoMId}", bomId);
            
            var breakdown = await _apiClient.GetCostBreakdownAsync(bomId, cancellationToken);
            
            if (breakdown != null && breakdown.IsValid)
            {
                _cache.Set(cacheKey, breakdown, CacheExpiration);
                _logger.LogDebug("Cached cost breakdown for BoM {BoMId}", bomId);
            }
            
            return breakdown ?? new BoMCostBreakdownDto { BoMId = bomId };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching BoM breakdown for {BoMId}", bomId);
            throw new BusinessException($"Failed to get BoM breakdown for {bomId}: {ex.Message}", ex);
        }
    }

    public async Task<bool> ValidateBoM(int bomId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating BoM {BoMId}", bomId);
            
            var isValid = await _apiClient.ValidateBoMAsync(bomId, cancellationToken);
            
            _logger.LogDebug("BoM {BoMId} validation result: {IsValid}", bomId, isValid);
            
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating BoM {BoMId}", bomId);
            return false;
        }
    }

    public async Task<List<MaterialCostComponent>> GetMaterialComponents(int bomId, CancellationToken cancellationToken = default)
    {
        var breakdown = await GetCostBreakdown(bomId, cancellationToken);
        return breakdown.Materials;
    }

    private void InvalidateBomCache(int bomId)
    {
        var cacheKey = $"BoM_Breakdown_{bomId}";
        _cache.Remove(cacheKey);
        _logger.LogDebug("Invalidated cache for BoM {BoMId}", bomId);
    }
}
```

### Manufacturing Cost Analysis Engine

```csharp
public static class ManufacturingCostAnalysisEngine
{
    public static ManufacturingCostAnalysisDto AnalyzeProductCost(
        ProductPriceErp product, 
        BoMCostBreakdownDto costBreakdown)
    {
        if (product == null || costBreakdown == null)
            throw new ArgumentNullException("Product and cost breakdown are required");
        
        var analysis = new ManufacturingCostAnalysisDto
        {
            ProductCode = product.ProductCode,
            CurrentPurchasePrice = product.PurchasePrice,
            CalculatedManufacturingCost = costBreakdown.TotalManufacturingCost
        };
        
        // Identify top cost drivers (materials contributing >10% of total cost)
        analysis.TopCostDrivers = costBreakdown.Materials
            .Where(m => m.CostPercentage > 10)
            .OrderByDescending(m => m.CostPercentage)
            .Take(5)
            .ToList();
        
        return analysis;
    }
    
    public static List<ManufacturingCostAnalysisDto> AnalyzeCostVariances(
        IEnumerable<ProductPriceErp> products,
        Dictionary<int, BoMCostBreakdownDto> costBreakdowns,
        decimal varianceThreshold = 5m)
    {
        var analyses = new List<ManufacturingCostAnalysisDto>();
        
        foreach (var product in products.Where(p => p.HasBillOfMaterials))
        {
            if (costBreakdowns.TryGetValue(product.BoMId!.Value, out var breakdown))
            {
                var analysis = AnalyzeProductCost(product, breakdown);
                
                if (Math.Abs(analysis.VariancePercentage) >= varianceThreshold)
                {
                    analyses.Add(analysis);
                }
            }
        }
        
        return analyses.OrderByDescending(a => Math.Abs(a.VariancePercentage)).ToList();
    }
    
    public static CostVarianceSummary SummarizeCostVariances(IEnumerable<ManufacturingCostAnalysisDto> analyses)
    {
        var analysesList = analyses.ToList();
        
        return new CostVarianceSummary
        {
            TotalProductsAnalyzed = analysesList.Count,
            ProductsRequiringRecalculation = analysesList.Count(a => a.RequiresRecalculation),
            AverageVariancePercentage = analysesList.Any() 
                ? analysesList.Average(a => Math.Abs(a.VariancePercentage)) 
                : 0,
            MaxVariancePercentage = analysesList.Any() 
                ? analysesList.Max(a => Math.Abs(a.VariancePercentage)) 
                : 0,
            TotalCostImpact = analysesList.Sum(a => a.CostVariance),
            TopVarianceProducts = analysesList
                .OrderByDescending(a => Math.Abs(a.VariancePercentage))
                .Take(10)
                .ToList()
        };
    }
}

public class CostVarianceSummary
{
    public int TotalProductsAnalyzed { get; set; }
    public int ProductsRequiringRecalculation { get; set; }
    public decimal AverageVariancePercentage { get; set; }
    public decimal MaxVariancePercentage { get; set; }
    public decimal TotalCostImpact { get; set; }
    public List<ManufacturingCostAnalysisDto> TopVarianceProducts { get; set; } = new();
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}
```

## Happy Day Scenario

1. **Request Initiation**: Manufacturing cost controller triggers cost recalculation for a specific product
2. **Product Validation**: System validates product exists and has valid Bill of Materials
3. **ERP Data Retrieval**: Fetch current product pricing and BoM data from FlexiBee ERP
4. **Cost Breakdown Analysis**: Retrieve detailed material costs and calculate total manufacturing cost
5. **Price Comparison**: Compare current purchase price with calculated manufacturing cost
6. **Recalculation Execution**: Execute BoM-based price recalculation in FlexiBee ERP
7. **Result Validation**: Verify recalculation completed successfully
8. **Audit Logging**: Record recalculation details with before/after values
9. **Cache Invalidation**: Clear relevant caches to ensure fresh data
10. **Success Response**: Return operation status and cost analysis details

## Error Handling

### Product Validation Errors
- **Product Not Found**: Clear error message with product code
- **Missing BoM**: Validate Bill of Materials existence before processing
- **Invalid BoM Data**: Handle corrupted or incomplete BoM structures
- **Product Code Validation**: Ensure non-empty, valid product identifiers

### Integration Errors
- **FlexiBee API Failures**: Retry mechanism with exponential backoff
- **BoM Client Errors**: Graceful degradation with detailed error messages
- **Network Timeouts**: Configurable timeout with circuit breaker pattern
- **Authentication Issues**: Clear credential validation and error reporting

### Business Logic Errors
- **Negative Costs**: Validate cost calculations for business rule compliance
- **Zero Material Costs**: Handle products with missing material cost data
- **Calculation Precision**: Ensure proper decimal handling and rounding
- **Concurrent Modifications**: Handle simultaneous recalculation attempts

### System Errors
- **Cache Failures**: Fallback to direct API calls when cache unavailable
- **Memory Issues**: Implement chunked processing for bulk operations
- **Database Connectivity**: Queue operations for later retry
- **Processing Limits**: Prevent resource exhaustion during bulk operations

## Business Rules

### Recalculation Eligibility
1. **BoM Requirement**: Product must have valid Bill of Materials ID
2. **Cost Validation**: Material costs must be positive and current
3. **Product Status**: Only active, manufactured products eligible
4. **Authorization**: Appropriate permissions required for cost updates

### Cost Calculation Rules
1. **Material Costs**: Sum of (quantity × unit cost) for all materials
2. **Labor Costs**: Include direct labor based on BoM specifications
3. **Overhead Allocation**: Apply overhead percentage based on business rules
4. **Rounding Standards**: Round to 2 decimal places for consistency

### Bulk Operation Rules
1. **Batch Size**: Process products in configurable batches
2. **Error Tolerance**: Continue processing if individual products fail
3. **Progress Reporting**: Provide real-time status updates
4. **Rollback Capability**: Support operation reversal if needed

### Audit and Tracking Rules
1. **Change Logging**: Record all price changes with timestamps
2. **User Attribution**: Track who initiated recalculations
3. **Original Value Preservation**: Maintain original prices for comparison
4. **History Retention**: Keep recalculation history for analysis

## Performance Requirements
- Process individual product recalculation within 5 seconds
- Handle bulk recalculation of 1,000+ products within 10 minutes
- Support concurrent recalculations by multiple users
- Maintain cache performance with 10-minute expiration
- Scale linearly with product catalog size
- Provide real-time progress feedback for bulk operations