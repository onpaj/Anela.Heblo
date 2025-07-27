# Batch Distribution Optimization User Story

## Feature Overview
The Batch Distribution Optimization feature enables intelligent production planning by optimizing the distribution of product variants within manufacturing batch constraints. This sophisticated algorithm maximizes production efficiency while respecting weight limits, demand patterns, and inventory levels to ensure optimal resource utilization and minimize production waste.

## Business Requirements

### Primary Use Case
As a production planner, I want to automatically optimize the distribution of different product variants within a manufacturing batch so that I can maximize production days coverage while respecting batch weight constraints and demand patterns, ensuring efficient resource utilization and meeting customer demand.

### Acceptance Criteria
1. The system shall calculate optimal production quantities for each product variant within batch weight constraints
2. The system shall prioritize production based on sales velocity and current stock levels
3. The system shall maximize the number of production days covered by the batch
4. The system shall distribute remaining batch capacity to minimize waste
5. The system shall support multiple optimization algorithms (primary and alternative)
6. The system shall validate that total production weight does not exceed batch capacity
7. The system shall provide detailed production recommendations with stock coverage analysis

## Technical Contracts

### Domain Model

```csharp
// Value object representing a production batch
public class ProductBatch
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double BatchSize { get; set; } // Number of batches
    public double BatchCount { get; set; } // Batch multiplier
    public double TotalWeight { get; set; } // Maximum batch weight capacity (kg)
    public List<ProductVariant> Variants { get; set; } = new();
    
    // Business logic
    public List<ProductVariant> ValidVariants => 
        Variants.Where(v => v.DailySales > 0 && v.Weight > 0).ToList();
    
    public static ProductBatch Create(
        string productCode,
        string productName,
        double totalWeight,
        List<ProductVariant> variants)
    {
        if (string.IsNullOrEmpty(productCode))
            throw new BusinessException("Product code is required");
        
        if (totalWeight <= 0)
            throw new BusinessException("Total weight must be positive");
        
        if (variants == null || !variants.Any())
            throw new BusinessException("At least one variant is required");
        
        return new ProductBatch
        {
            ProductCode = productCode,
            ProductName = productName,
            TotalWeight = totalWeight,
            Variants = variants
        };
    }
    
    public double GetTotalProducedWeight()
    {
        return Variants.Sum(v => v.SuggestedAmount * v.Weight);
    }
    
    public double GetRemainingCapacity()
    {
        return TotalWeight - GetTotalProducedWeight();
    }
    
    public bool IsWithinCapacity()
    {
        return GetTotalProducedWeight() <= TotalWeight;
    }
}

// Value object representing a product variant
public class ProductVariant
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double Volume { get; set; } // Product volume (ml)
    public double Weight { get; set; } // Unit weight (kg)
    public double DailySales { get; set; } // Average daily sales velocity
    public double CurrentStock { get; set; } // Current inventory level
    public double SuggestedAmount { get; set; } // Optimized production quantity
    
    // Computed properties for analysis
    public double UpstockSuggested => DailySales > 0 ? SuggestedAmount / DailySales : 0;
    public double UpstockTotal => DailySales > 0 ? (SuggestedAmount + CurrentStock) / DailySales : 0;
    public double UpstockCurrent => DailySales > 0 ? CurrentStock / DailySales : 0;
    
    public static ProductVariant Create(
        string productCode,
        string productName,
        double volume,
        double weight,
        double dailySales,
        double currentStock)
    {
        if (string.IsNullOrEmpty(productCode))
            throw new BusinessException("Product code is required");
        
        if (weight <= 0)
            throw new BusinessException("Weight must be positive");
        
        if (dailySales < 0)
            throw new BusinessException("Daily sales cannot be negative");
        
        if (currentStock < 0)
            throw new BusinessException("Current stock cannot be negative");
        
        return new ProductVariant
        {
            ProductCode = productCode,
            ProductName = productName,
            Volume = volume,
            Weight = weight,
            DailySales = dailySales,
            CurrentStock = currentStock,
            SuggestedAmount = 0
        };
    }
    
    public double GetRequiredQuantityForDays(double days)
    {
        return Math.Max(days * DailySales - CurrentStock, 0);
    }
    
    public double GetTotalStockAfterProduction()
    {
        return CurrentStock + SuggestedAmount;
    }
    
    public double GetDaysCoverageAfterProduction()
    {
        return DailySales > 0 ? GetTotalStockAfterProduction() / DailySales : 0;
    }
}
```

### Application Layer Contracts

```csharp
// Service interface for batch optimization
public interface IBatchDistributionCalculator
{
    void OptimizeBatch(ProductBatch batch, bool minimizeResidue = true);
    void OptimizeBatch2(ProductBatch batch);
    ProductBatch CalculateOptimalDistribution(ProductBatch batch, OptimizationStrategy strategy = OptimizationStrategy.MaximizeDays);
}

// Request/Response DTOs
public class BatchDistributionRequestDto
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double TotalWeight { get; set; }
    public List<ProductVariantDto> Variants { get; set; } = new();
    public OptimizationStrategy Strategy { get; set; } = OptimizationStrategy.MaximizeDays;
    public bool MinimizeResidue { get; set; } = true;
}

public class BatchDistributionResultDto
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double TotalWeight { get; set; }
    public double UsedWeight { get; set; }
    public double RemainingWeight { get; set; }
    public double UtilizationPercentage { get; set; }
    public double OptimalDaysCoverage { get; set; }
    public List<ProductVariantDto> Variants { get; set; } = new();
    public OptimizationMetrics Metrics { get; set; }
}

public class ProductVariantDto
{
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double Volume { get; set; }
    public double Weight { get; set; }
    public double DailySales { get; set; }
    public double CurrentStock { get; set; }
    public double SuggestedAmount { get; set; }
    public double UpstockSuggested { get; set; }
    public double UpstockTotal { get; set; }
    public double UpstockCurrent { get; set; }
    public double TotalWeightProduced { get; set; }
    public double DaysCoverageAfterProduction { get; set; }
}

public class OptimizationMetrics
{
    public double TotalDailyConsumption { get; set; }
    public double MaxPossibleDays { get; set; }
    public double ActualDaysAchieved { get; set; }
    public int IterationsPerformed { get; set; }
    public double OptimizationEfficiency { get; set; }
    public double WastePercentage { get; set; }
}

public enum OptimizationStrategy
{
    MaximizeDays = 0,     // Maximize production days coverage
    MaximizeUtilization = 1, // Maximize batch weight utilization
    BalancedApproach = 2   // Balance between days and utilization
}
```

## Implementation Details

### Primary Optimization Algorithm: Binary Search with Days Maximization

```csharp
public class BatchDistributionCalculator : IBatchDistributionCalculator
{
    private readonly ILogger<BatchDistributionCalculator> _logger;
    
    public BatchDistributionCalculator(ILogger<BatchDistributionCalculator> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Primary optimization algorithm using binary search to maximize production days
    /// </summary>
    public void OptimizeBatch(ProductBatch batch, bool minimizeResidue = true)
    {
        var variants = batch.ValidVariants;
        double totalWeight = batch.TotalWeight;
        
        if (!variants.Any())
        {
            _logger.LogWarning("No valid variants found for batch {ProductCode}", batch.ProductCode);
            return;
        }
        
        // Binary search for maximum achievable days
        double low = 0;
        double high = CalculateUpperBound(variants, totalWeight);
        double bestDays = 0;
        
        _logger.LogDebug("Starting binary search for batch {ProductCode}, bounds: {Low}-{High}", 
            batch.ProductCode, low, high);
        
        while (high - low > 0.1) // Precision threshold
        {
            double mid = (low + high) / 2;
            
            double requiredWeight = CalculateRequiredWeight(variants, mid);
            
            if (requiredWeight <= totalWeight)
            {
                bestDays = mid;
                low = mid;
            }
            else
            {
                high = mid;
            }
        }
        
        // Calculate final production quantities
        foreach (var variant in variants)
        {
            double needed = Math.Max(bestDays * variant.DailySales - variant.CurrentStock, 0);
            variant.SuggestedAmount = Math.Floor(needed);
        }
        
        _logger.LogInformation("Optimized batch {ProductCode} for {Days} days coverage", 
            batch.ProductCode, bestDays);
        
        // Distribute remaining capacity to minimize waste
        if (minimizeResidue)
        {
            DistributeRemainingWeight(batch);
        }
    }
    
    /// <summary>
    /// Alternative optimization algorithm focusing on maximum weight utilization
    /// </summary>
    public void OptimizeBatch2(ProductBatch batch)
    {
        var variants = batch.ValidVariants;
        
        // Calculate total daily consumption in weight
        var dailyConsumption = variants.Sum(v => v.Weight * v.DailySales);
        
        if (dailyConsumption <= 0)
        {
            _logger.LogWarning("No daily consumption found for batch {ProductCode}", batch.ProductCode);
            return;
        }
        
        // Include existing stock in weight calculation
        double existingWeight = variants.Sum(v => v.CurrentStock * v.Weight);
        
        // Calculate maximum possible days with total capacity
        double maxDays = (batch.TotalWeight + existingWeight) / dailyConsumption;
        
        foreach (var variant in variants)
        {
            // Calculate total needed quantity for max days
            double neededTotal = variant.DailySales * maxDays;
            double toProduce = Math.Floor(Math.Max(neededTotal - variant.CurrentStock, 0));
            
            // Respect individual variant weight constraints
            double maxVariantProduction = Math.Floor(batch.TotalWeight / variant.Weight);
            variant.SuggestedAmount = Math.Min(toProduce, maxVariantProduction);
        }
        
        // Distribute any remaining capacity
        DistributeRemainingWeight(batch);
        
        _logger.LogInformation("Optimized batch {ProductCode} using utilization strategy, max days: {Days}", 
            batch.ProductCode, maxDays);
    }
    
    private double CalculateUpperBound(List<ProductVariant> variants, double totalWeight)
    {
        // Conservative upper bound: assume all weight goes to fastest-moving, lightest variant
        var lightestVariant = variants.OrderBy(v => v.Weight).First();
        double maxUnits = totalWeight / lightestVariant.Weight;
        return maxUnits / lightestVariant.DailySales;
    }
    
    private double CalculateRequiredWeight(List<ProductVariant> variants, double days)
    {
        double totalWeight = 0;
        
        foreach (var variant in variants)
        {
            double needed = Math.Max(days * variant.DailySales - variant.CurrentStock, 0);
            totalWeight += Math.Ceiling(needed) * variant.Weight;
        }
        
        return totalWeight;
    }
    
    private void DistributeRemainingWeight(ProductBatch batch)
    {
        double remainingWeight = batch.GetRemainingCapacity();
        
        if (remainingWeight <= 0)
            return;
        
        var variants = batch.Variants.OrderByDescending(v => v.Weight).ToList();
        
        _logger.LogDebug("Distributing remaining weight {Weight}kg for batch {ProductCode}", 
            remainingWeight, batch.ProductCode);
        
        foreach (var variant in variants)
        {
            if (remainingWeight < variant.Weight)
                continue;
                
            int additional = (int)(remainingWeight / variant.Weight);
            
            if (additional > 0)
            {
                variant.SuggestedAmount += additional;
                remainingWeight -= additional * variant.Weight;
                
                _logger.LogDebug("Added {Additional} units of {ProductCode}, remaining: {Remaining}kg", 
                    additional, variant.ProductCode, remainingWeight);
            }
            
            // Stop if remaining weight can't fill any more variants
            if (remainingWeight < variants.Min(v => v.Weight))
                break;
        }
        
        _logger.LogDebug("Final remaining weight: {Weight}kg", remainingWeight);
    }
}
```

### Application Service Integration

```csharp
public class ManufactureAppService : ApplicationService, IManufactureAppService
{
    private readonly IBatchDistributionCalculator _batchCalculator;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<ManufactureAppService> _logger;
    
    public async Task<BatchDistributionResultDto> GetBatchDistributionAsync(
        BatchDistributionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        if (request.TotalWeight <= 0)
            throw new UserFriendlyException("Total weight must be positive");
        
        if (!request.Variants.Any())
            throw new UserFriendlyException("At least one variant is required");
        
        // Create domain objects
        var variants = request.Variants.Select(dto => ProductVariant.Create(
            dto.ProductCode,
            dto.ProductName,
            dto.Volume,
            dto.Weight,
            dto.DailySales,
            dto.CurrentStock)).ToList();
        
        var batch = ProductBatch.Create(
            request.ProductCode,
            request.ProductName,
            request.TotalWeight,
            variants);
        
        // Perform optimization
        try
        {
            switch (request.Strategy)
            {
                case OptimizationStrategy.MaximizeDays:
                    _batchCalculator.OptimizeBatch(batch, request.MinimizeResidue);
                    break;
                case OptimizationStrategy.MaximizeUtilization:
                    _batchCalculator.OptimizeBatch2(batch);
                    break;
                case OptimizationStrategy.BalancedApproach:
                    await OptimizeWithBalancedApproach(batch);
                    break;
                default:
                    _batchCalculator.OptimizeBatch(batch, request.MinimizeResidue);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to optimize batch {ProductCode}", request.ProductCode);
            throw new UserFriendlyException("Optimization failed: " + ex.Message);
        }
        
        // Calculate metrics and return result
        var result = new BatchDistributionResultDto
        {
            ProductCode = batch.ProductCode,
            ProductName = batch.ProductName,
            TotalWeight = batch.TotalWeight,
            UsedWeight = batch.GetTotalProducedWeight(),
            RemainingWeight = batch.GetRemainingCapacity(),
            UtilizationPercentage = (batch.GetTotalProducedWeight() / batch.TotalWeight) * 100,
            OptimalDaysCoverage = CalculateOptimalDaysCoverage(batch),
            Variants = ObjectMapper.Map<List<ProductVariant>, List<ProductVariantDto>>(batch.Variants),
            Metrics = CalculateOptimizationMetrics(batch)
        };
        
        _logger.LogInformation("Batch optimization completed for {ProductCode}: {Utilization}% utilization, {Days} days coverage",
            result.ProductCode, result.UtilizationPercentage, result.OptimalDaysCoverage);
        
        return result;
    }
    
    private async Task OptimizeWithBalancedApproach(ProductBatch batch)
    {
        // Run both algorithms and select best balance
        var originalVariants = batch.Variants.Select(v => new ProductVariant
        {
            ProductCode = v.ProductCode,
            ProductName = v.ProductName,
            Volume = v.Volume,
            Weight = v.Weight,
            DailySales = v.DailySales,
            CurrentStock = v.CurrentStock
        }).ToList();
        
        // Try primary algorithm
        _batchCalculator.OptimizeBatch(batch, true);
        var primaryMetrics = CalculateOptimizationMetrics(batch);
        var primaryResults = batch.Variants.Select(v => v.SuggestedAmount).ToList();
        
        // Reset and try alternative algorithm
        for (int i = 0; i < batch.Variants.Count; i++)
        {
            batch.Variants[i].SuggestedAmount = 0;
        }
        
        _batchCalculator.OptimizeBatch2(batch);
        var alternativeMetrics = CalculateOptimizationMetrics(batch);
        
        // Select best approach based on balanced score
        double primaryScore = (primaryMetrics.OptimizationEfficiency * 0.6) + 
                            ((100 - primaryMetrics.WastePercentage) * 0.4);
        double alternativeScore = (alternativeMetrics.OptimizationEfficiency * 0.6) + 
                                ((100 - alternativeMetrics.WastePercentage) * 0.4);
        
        if (primaryScore > alternativeScore)
        {
            // Restore primary results
            for (int i = 0; i < batch.Variants.Count; i++)
            {
                batch.Variants[i].SuggestedAmount = primaryResults[i];
            }
        }
        // Alternative results are already in place
        
        _logger.LogDebug("Balanced optimization: Primary score {Primary}, Alternative score {Alternative}, Selected {Selected}",
            primaryScore, alternativeScore, primaryScore > alternativeScore ? "Primary" : "Alternative");
    }
    
    private double CalculateOptimalDaysCoverage(ProductBatch batch)
    {
        if (!batch.Variants.Any(v => v.DailySales > 0))
            return 0;
        
        return batch.Variants
            .Where(v => v.DailySales > 0)
            .Average(v => v.GetDaysCoverageAfterProduction());
    }
    
    private OptimizationMetrics CalculateOptimizationMetrics(ProductBatch batch)
    {
        var validVariants = batch.ValidVariants;
        
        return new OptimizationMetrics
        {
            TotalDailyConsumption = validVariants.Sum(v => v.Weight * v.DailySales),
            MaxPossibleDays = CalculateMaxPossibleDays(batch),
            ActualDaysAchieved = CalculateOptimalDaysCoverage(batch),
            OptimizationEfficiency = CalculateEfficiency(batch),
            WastePercentage = (batch.GetRemainingCapacity() / batch.TotalWeight) * 100
        };
    }
    
    private double CalculateMaxPossibleDays(ProductBatch batch)
    {
        var dailyConsumption = batch.ValidVariants.Sum(v => v.Weight * v.DailySales);
        return dailyConsumption > 0 ? batch.TotalWeight / dailyConsumption : 0;
    }
    
    private double CalculateEfficiency(ProductBatch batch)
    {
        var maxDays = CalculateMaxPossibleDays(batch);
        var actualDays = CalculateOptimalDaysCoverage(batch);
        return maxDays > 0 ? (actualDays / maxDays) * 100 : 0;
    }
}
```

## Algorithm Details

### Binary Search Optimization (Primary Algorithm)

#### Objective
Maximize the number of production days covered while respecting weight constraints.

#### Process
1. **Initialize Bounds**: Set lower bound to 0, upper bound to theoretical maximum
2. **Binary Search**: Find maximum feasible days using binary search
3. **Feasibility Check**: For each candidate days value, calculate required weight
4. **Convergence**: Continue until precision threshold is reached
5. **Final Calculation**: Compute production quantities for optimal days
6. **Residue Distribution**: Allocate remaining capacity to minimize waste

#### Complexity
- Time: O(n log m) where n = variants, m = search space
- Space: O(1) additional space

### Weight Utilization Optimization (Alternative Algorithm)

#### Objective
Maximize batch weight utilization and production efficiency.

#### Process
1. **Daily Consumption**: Calculate total daily weight consumption
2. **Maximum Days**: Determine theoretical maximum with full capacity
3. **Proportional Allocation**: Distribute production based on demand ratios
4. **Constraint Validation**: Ensure individual variant limits
5. **Residue Distribution**: Fill remaining capacity optimally

#### Use Cases
- High-velocity products with predictable demand
- Scenarios where weight utilization is critical
- Products with similar weight-to-demand ratios

## Happy Day Scenario

1. **Input Validation**: Validate batch parameters and variant data
2. **Algorithm Selection**: Choose optimization strategy based on requirements
3. **Binary Search Execution**: Find optimal production days coverage
4. **Production Calculation**: Calculate exact quantities for each variant
5. **Residue Distribution**: Allocate remaining capacity efficiently
6. **Metrics Computation**: Calculate optimization efficiency and waste metrics
7. **Result Generation**: Return comprehensive optimization results
8. **Logging**: Record optimization decisions and performance metrics

## Error Handling

### Input Validation Errors
- **Invalid Weight**: Total weight must be positive
- **Empty Variants**: At least one variant required
- **Invalid Sales Data**: Daily sales cannot be negative
- **Weight Constraints**: Variant weights must be positive

### Optimization Errors
- **No Valid Variants**: All variants have zero sales or weight
- **Infeasible Constraints**: Weight requirements exceed capacity
- **Algorithm Failures**: Numerical instability or convergence issues
- **Memory Constraints**: Large datasets causing performance issues

### System Errors
- **Calculation Overflow**: Handle extremely large numbers
- **Precision Loss**: Maintain numerical accuracy
- **Performance Degradation**: Timeout for complex optimizations

## Business Rules

### Production Constraints
1. **Weight Limit**: Total production weight ≤ batch capacity
2. **Minimum Quantities**: Respect economical production runs
3. **Demand Alignment**: Production proportional to sales velocity
4. **Inventory Consideration**: Account for existing stock levels

### Optimization Principles
1. **Days Maximization**: Prioritize longer production coverage
2. **Waste Minimization**: Utilize available capacity efficiently
3. **Demand Satisfaction**: Ensure high-velocity products are prioritized
4. **Balance Maintenance**: Avoid overproduction of slow-moving items

### Quality Assurance
1. **Feasibility Verification**: All solutions must be implementable
2. **Consistency Checks**: Results must be mathematically sound
3. **Performance Standards**: Optimization must complete within time limits
4. **Accuracy Requirements**: Maintain precision in calculations

## Performance Requirements

### Optimization Speed
- Complete optimization within 2 seconds for standard batches
- Handle up to 50 variants per batch efficiently
- Maintain sub-second response for simple optimizations
- Scale linearly with variant count

### Memory Efficiency
- Minimize memory allocation during optimization
- Support concurrent optimization requests
- Handle large datasets without performance degradation
- Maintain garbage collection efficiency

### Numerical Stability
- Maintain precision through iterative calculations
- Handle edge cases gracefully
- Avoid floating-point precision issues
- Ensure reproducible results

### Scalability
- Support multiple concurrent optimization requests
- Cache frequent calculations for performance
- Optimize database queries for large datasets
- Implement efficient algorithms with good complexity

## Integration Requirements

### Catalog Domain Integration
- Retrieve current stock levels and sales data
- Validate product codes and variants
- Access product master data for weight/volume information
- Sync with inventory management systems

### Manufacturing Domain Integration
- Access Bill of Materials for ingredient requirements
- Integrate with production scheduling systems
- Update manufacturing templates with optimization results
- Coordinate with capacity planning systems

### Reporting Integration
- Generate optimization reports and analytics
- Track optimization performance metrics
- Provide production planning dashboards
- Export results for external systems