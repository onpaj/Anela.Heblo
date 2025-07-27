# User Story: Production Planning

## Feature Description
The Production Planning feature orchestrates production scheduling based on stock analysis, demand forecasting, and batch optimization. It integrates stock severity analysis with manufacturing templates to generate optimized production schedules that minimize stockouts while maximizing production efficiency through advanced batch distribution algorithms.

## Business Requirements

### Primary Use Cases
1. **Production Schedule Generation**: Create optimal production schedules based on stock analysis
2. **Material Requirements Planning**: Calculate material needs for planned production
3. **Batch Distribution Optimization**: Optimize batch compositions for maximum efficiency
4. **Capacity Planning**: Manage production capacity and resource allocation
5. **Production Order Management**: Create and track production orders through completion

### User Stories
- As a production planner, I want to generate production schedules so I can ensure adequate stock levels
- As a production manager, I want to optimize batch compositions so I can maximize production efficiency
- As a materials manager, I want to see material requirements so I can plan procurement
- As an operations manager, I want to track production orders so I can monitor progress

## Technical Requirements

### Domain Models

#### ProductionPlan
```csharp
public class ProductionPlan : AuditedAggregateRoot<Guid>
{
    public string PlanName { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime PlanDate { get; set; }
    public DateTime PlannedStartDate { get; set; }
    public DateTime PlannedEndDate { get; set; }
    public ProductionPlanStatus Status { get; set; } = ProductionPlanStatus.Draft;
    public string ResponsibleUserId { get; set; } = "";
    public string? Notes { get; set; }
    public ProductionPlanningMethod Method { get; set; } = ProductionPlanningMethod.StockBased;
    public int PlanningHorizonDays { get; set; } = 30;
    
    // Navigation Properties
    public virtual ICollection<ProductionOrder> ProductionOrders { get; set; } = new List<ProductionOrder>();
    public virtual ICollection<MaterialRequirement> MaterialRequirements { get; set; } = new List<MaterialRequirement>();
    public virtual ICollection<ProductionBatch> Batches { get; set; } = new List<ProductionBatch>();
    
    // Computed Properties
    public int TotalOrders => ProductionOrders.Count;
    public int CompletedOrders => ProductionOrders.Count(o => o.Status == ProductionOrderStatus.Completed);
    public int ActiveOrders => ProductionOrders.Count(o => o.Status == ProductionOrderStatus.InProgress);
    public decimal TotalPlannedQuantity => ProductionOrders.Sum(o => o.PlannedQuantity);
    public decimal TotalActualQuantity => ProductionOrders.Sum(o => o.ActualQuantity ?? 0);
    public TimeSpan PlannedDuration => PlannedEndDate - PlannedStartDate;
    public decimal CompletionPercentage => TotalOrders > 0 ? (CompletedOrders / (decimal)TotalOrders) * 100 : 0;
    public bool IsOverdue => PlannedEndDate < DateTime.Today && Status != ProductionPlanStatus.Completed;
    
    // Business Methods
    public void AddProductionOrder(string productCode, decimal quantity, DateTime scheduledDate, ProductionPriority priority = ProductionPriority.Normal)
    {
        if (Status != ProductionPlanStatus.Draft)
            throw new BusinessException("Cannot add orders to non-draft plans");
            
        var order = new ProductionOrder
        {
            Id = Guid.NewGuid(),
            PlanId = Id,
            OrderNumber = GenerateOrderNumber(),
            ProductCode = productCode,
            PlannedQuantity = quantity,
            ScheduledDate = scheduledDate,
            Priority = priority,
            Status = ProductionOrderStatus.Planned
        };
        
        ProductionOrders.Add(order);
        CalculateMaterialRequirements();
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void OptimizeBatches(Dictionary<string, decimal> salesVelocity, decimal maxBatchWeight = 1000)
    {
        var optimizer = new BatchDistributionCalculator();
        var productVariants = new List<ProductVariant>();
        
        foreach (var order in ProductionOrders.Where(o => o.Status == ProductionOrderStatus.Planned))
        {
            productVariants.Add(new ProductVariant
            {
                ProductCode = order.ProductCode,
                CurrentAmount = 0, // Starting fresh
                DailySales = salesVelocity.GetValueOrDefault(order.ProductCode, 1),
                Volume = GetProductVolume(order.ProductCode),
                Weight = GetProductWeight(order.ProductCode)
            });
        }
        
        var productBatch = new ProductBatch
        {
            TotalWeight = maxBatchWeight,
            ProductVariants = productVariants
        };
        
        var optimizedBatch = optimizer.OptimizeBatch(productBatch, salesVelocity);
        
        // Create production batches from optimization
        CreateProductionBatches(optimizedBatch);
    }
    
    public void ApprovePlan()
    {
        if (Status != ProductionPlanStatus.Draft)
            throw new BusinessException("Can only approve draft plans");
            
        if (!ProductionOrders.Any())
            throw new BusinessException("Cannot approve plan without production orders");
            
        ValidatePlan();
        Status = ProductionPlanStatus.Approved;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void StartExecution()
    {
        if (Status != ProductionPlanStatus.Approved)
            throw new BusinessException("Can only start approved plans");
            
        Status = ProductionPlanStatus.InProgress;
        
        // Set first orders to ready status
        var firstOrders = ProductionOrders
            .Where(o => o.Status == ProductionOrderStatus.Planned)
            .OrderBy(o => o.ScheduledDate)
            .Take(5); // Start with first 5 orders
            
        foreach (var order in firstOrders)
        {
            order.SetReady();
        }
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CompletePlan()
    {
        if (ProductionOrders.Any(o => o.Status != ProductionOrderStatus.Completed && o.Status != ProductionOrderStatus.Cancelled))
            throw new BusinessException("Cannot complete plan with active orders");
            
        Status = ProductionPlanStatus.Completed;
        LastModificationTime = DateTime.UtcNow;
    }
    
    private void CalculateMaterialRequirements()
    {
        MaterialRequirements.Clear();
        var materialNeeds = new Dictionary<string, MaterialRequirement>();
        
        foreach (var order in ProductionOrders)
        {
            var template = GetManufacturingTemplate(order.ProductCode);
            if (template == null) continue;
            
            var scaledTemplate = template.ScaleToAmount(order.PlannedQuantity);
            
            foreach (var ingredient in scaledTemplate.ScaledIngredients)
            {
                if (materialNeeds.ContainsKey(ingredient.IngredientCode))
                {
                    materialNeeds[ingredient.IngredientCode].RequiredQuantity += ingredient.ScaledAmount;
                    materialNeeds[ingredient.IngredientCode].TotalCost += ingredient.TotalCost;
                }
                else
                {
                    materialNeeds[ingredient.IngredientCode] = new MaterialRequirement
                    {
                        PlanId = Id,
                        MaterialCode = ingredient.IngredientCode,
                        MaterialName = ingredient.IngredientName,
                        RequiredQuantity = ingredient.ScaledAmount,
                        Unit = ingredient.Unit,
                        CostPerUnit = ingredient.CostPerUnit,
                        TotalCost = ingredient.TotalCost,
                        RequiredDate = PlannedStartDate
                    };
                }
            }
        }
        
        foreach (var requirement in materialNeeds.Values)
        {
            MaterialRequirements.Add(requirement);
        }
    }
    
    private void ValidatePlan()
    {
        var issues = new List<string>();
        
        if (PlannedStartDate >= PlannedEndDate)
            issues.Add("Start date must be before end date");
            
        if (PlannedStartDate < DateTime.Today)
            issues.Add("Cannot plan for past dates");
            
        // Check material availability
        foreach (var requirement in MaterialRequirements)
        {
            var available = GetAvailableMaterial(requirement.MaterialCode);
            if (available < requirement.RequiredQuantity)
            {
                issues.Add($"Insufficient {requirement.MaterialCode}: need {requirement.RequiredQuantity}, have {available}");
            }
        }
        
        if (issues.Any())
            throw new BusinessException($"Plan validation failed: {string.Join("; ", issues)}");
    }
    
    private string GenerateOrderNumber()
    {
        var today = DateTime.Today;
        var orderCount = ProductionOrders.Count + 1;
        return $"PO{today:yyyyMMdd}{orderCount:D3}";
    }
    
    private void CreateProductionBatches(OptimizedBatch optimizedBatch)
    {
        // Implementation for creating production batches from optimization result
        // This would create ProductionBatch entities based on the optimization
    }
    
    private ManufactureTemplate? GetManufacturingTemplate(string productCode)
    {
        // Implementation to get manufacturing template for product
        return null; // Placeholder
    }
    
    private decimal GetProductVolume(string productCode)
    {
        // Implementation to get product volume
        return 1.0m; // Placeholder
    }
    
    private decimal GetProductWeight(string productCode)
    {
        // Implementation to get product weight
        return 1.0m; // Placeholder
    }
    
    private decimal GetAvailableMaterial(string materialCode)
    {
        // Implementation to get available material quantity
        return 0; // Placeholder
    }
}

public enum ProductionPlanStatus
{
    Draft,
    Approved,
    InProgress,
    Completed,
    Cancelled
}

public enum ProductionPlanningMethod
{
    StockBased,
    DemandBased,
    Hybrid,
    Manual
}
```

#### ProductionOrder
```csharp
public class ProductionOrder : AuditedEntity<Guid>
{
    public Guid PlanId { get; set; }
    public string OrderNumber { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal PlannedQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
    public DateTime ScheduledDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? CompletionDate { get; set; }
    public ProductionOrderStatus Status { get; set; } = ProductionOrderStatus.Planned;
    public ProductionPriority Priority { get; set; } = ProductionPriority.Normal;
    public string? Notes { get; set; }
    public string? AssignedUserId { get; set; }
    public string? BatchNumber { get; set; }
    
    // Navigation Properties
    public virtual ProductionPlan Plan { get; set; } = null!;
    
    // Computed Properties
    public decimal QuantityVariance => (ActualQuantity ?? 0) - PlannedQuantity;
    public decimal QuantityVariancePercentage => PlannedQuantity > 0 ? (QuantityVariance / PlannedQuantity) * 100 : 0;
    public TimeSpan? ActualDuration => CompletionDate - StartDate;
    public bool IsOverdue => ScheduledDate < DateTime.Today && Status != ProductionOrderStatus.Completed;
    public bool IsStarted => StartDate.HasValue;
    public bool IsCompleted => Status == ProductionOrderStatus.Completed;
    public bool HasVariance => Math.Abs(QuantityVariance) > 0.001m;
    
    // Business Methods
    public void SetReady()
    {
        if (Status != ProductionOrderStatus.Planned)
            throw new BusinessException("Can only set planned orders to ready");
            
        Status = ProductionOrderStatus.Ready;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void StartProduction(string assignedUserId, string? batchNumber = null)
    {
        if (Status != ProductionOrderStatus.Ready)
            throw new BusinessException("Can only start ready orders");
            
        Status = ProductionOrderStatus.InProgress;
        StartDate = DateTime.UtcNow;
        AssignedUserId = assignedUserId;
        BatchNumber = batchNumber ?? GenerateBatchNumber();
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CompleteProduction(decimal actualQuantity, string? notes = null)
    {
        if (Status != ProductionOrderStatus.InProgress)
            throw new BusinessException("Can only complete in-progress orders");
            
        ActualQuantity = actualQuantity;
        CompletionDate = DateTime.UtcNow;
        Status = ProductionOrderStatus.Completed;
        Notes = notes;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CancelOrder(string reason)
    {
        if (Status == ProductionOrderStatus.Completed)
            throw new BusinessException("Cannot cancel completed orders");
            
        Status = ProductionOrderStatus.Cancelled;
        Notes = $"Cancelled: {reason}";
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void UpdateSchedule(DateTime newScheduledDate)
    {
        if (Status == ProductionOrderStatus.Completed || Status == ProductionOrderStatus.Cancelled)
            throw new BusinessException("Cannot reschedule completed or cancelled orders");
            
        ScheduledDate = newScheduledDate;
        LastModificationTime = DateTime.UtcNow;
    }
    
    private string GenerateBatchNumber()
    {
        var today = DateTime.Today;
        return $"B{today:yyyyMMdd}{DateTime.Now:HHmm}";
    }
}

public enum ProductionOrderStatus
{
    Planned,
    Ready,
    InProgress,
    Completed,
    Cancelled,
    OnHold
}

public enum ProductionPriority
{
    Low,
    Normal,
    High,
    Critical
}
```

#### MaterialRequirement
```csharp
public class MaterialRequirement : Entity<Guid>
{
    public Guid PlanId { get; set; }
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public decimal RequiredQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
    public decimal ShortfallQuantity => Math.Max(0, RequiredQuantity - AvailableQuantity);
    public string Unit { get; set; } = "";
    public decimal CostPerUnit { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime RequiredDate { get; set; }
    public MaterialRequirementStatus Status { get; set; } = MaterialRequirementStatus.Planned;
    public string? SupplierCode { get; set; }
    public DateTime? OrderedDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    
    // Navigation Properties
    public virtual ProductionPlan Plan { get; set; } = null!;
    
    // Computed Properties
    public bool HasShortfall => ShortfallQuantity > 0;
    public bool IsAvailable => AvailableQuantity >= RequiredQuantity;
    public decimal AvailabilityPercentage => RequiredQuantity > 0 ? (AvailableQuantity / RequiredQuantity) * 100 : 100;
    public bool IsOverdue => RequiredDate < DateTime.Today && Status != MaterialRequirementStatus.Fulfilled;
    
    // Business Methods
    public void UpdateAvailability(decimal newAvailableQuantity)
    {
        AvailableQuantity = newAvailableQuantity;
        Status = IsAvailable ? MaterialRequirementStatus.Available : MaterialRequirementStatus.Shortfall;
    }
    
    public void OrderMaterial(string supplierCode, DateTime expectedDelivery)
    {
        SupplierCode = supplierCode;
        OrderedDate = DateTime.UtcNow;
        ExpectedDeliveryDate = expectedDelivery;
        Status = MaterialRequirementStatus.Ordered;
    }
    
    public void MarkFulfilled()
    {
        Status = MaterialRequirementStatus.Fulfilled;
        AvailableQuantity = RequiredQuantity;
    }
}

public enum MaterialRequirementStatus
{
    Planned,
    Shortfall,
    Ordered,
    Available,
    Fulfilled
}
```

### Application Services

#### IProductionPlanningAppService
```csharp
public interface IProductionPlanningAppService : IApplicationService
{
    Task<ProductionPlanDto> CreatePlanAsync(CreateProductionPlanDto input);
    Task<ProductionPlanDto> GetPlanAsync(Guid planId);
    Task<PagedResultDto<ProductionPlanDto>> GetPlansAsync(GetProductionPlansQuery query);
    Task<ProductionPlanDto> UpdatePlanAsync(Guid planId, UpdateProductionPlanDto input);
    Task DeletePlanAsync(Guid planId);
    
    Task<ProductionPlanDto> GenerateStockBasedPlanAsync(GenerateStockBasedPlanDto input);
    Task<ProductionPlanDto> GenerateDemandBasedPlanAsync(GenerateDemandBasedPlanDto input);
    Task<ProductionPlanDto> OptimizePlanBatchesAsync(Guid planId, OptimizeBatchesDto input);
    
    Task<ProductionPlanDto> AddProductionOrderAsync(Guid planId, AddProductionOrderDto input);
    Task<ProductionPlanDto> UpdateProductionOrderAsync(Guid planId, Guid orderId, UpdateProductionOrderDto input);
    Task<ProductionPlanDto> RemoveProductionOrderAsync(Guid planId, Guid orderId);
    
    Task<ProductionPlanDto> ApprovePlanAsync(Guid planId);
    Task<ProductionPlanDto> StartPlanExecutionAsync(Guid planId);
    Task<ProductionPlanDto> CompletePlanAsync(Guid planId);
    
    Task<ProductionOrderDto> StartProductionOrderAsync(Guid orderId, StartProductionOrderDto input);
    Task<ProductionOrderDto> CompleteProductionOrderAsync(Guid orderId, CompleteProductionOrderDto input);
    Task<ProductionOrderDto> CancelProductionOrderAsync(Guid orderId, CancelProductionOrderDto input);
    
    Task<List<MaterialRequirementDto>> GetMaterialRequirementsAsync(Guid planId);
    Task<MaterialAvailabilityReportDto> CheckMaterialAvailabilityAsync(Guid planId);
    Task<ProductionCapacityAnalysisDto> AnalyzeProductionCapacityAsync(DateTime fromDate, DateTime toDate);
    
    Task<List<ProductionRecommendationDto>> GetProductionRecommendationsAsync(GetProductionRecommendationsQuery query);
    Task<ProductionScheduleDto> GetProductionScheduleAsync(DateTime fromDate, DateTime toDate);
}
```

#### ProductionPlanningAppService Implementation
```csharp
[Authorize]
public class ProductionPlanningAppService : ApplicationService, IProductionPlanningAppService
{
    private readonly IProductionPlanRepository _planRepository;
    private readonly IManufactureStockRepository _stockRepository;
    private readonly IManufactureTemplateRepository _templateRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IBatchDistributionCalculator _batchCalculator;
    private readonly ILogger<ProductionPlanningAppService> _logger;

    public ProductionPlanningAppService(
        IProductionPlanRepository planRepository,
        IManufactureStockRepository stockRepository,
        IManufactureTemplateRepository templateRepository,
        ICatalogRepository catalogRepository,
        IBatchDistributionCalculator batchCalculator,
        ILogger<ProductionPlanningAppService> logger)
    {
        _planRepository = planRepository;
        _stockRepository = stockRepository;
        _templateRepository = templateRepository;
        _catalogRepository = catalogRepository;
        _batchCalculator = batchCalculator;
        _logger = logger;
    }

    public async Task<ProductionPlanDto> GenerateStockBasedPlanAsync(GenerateStockBasedPlanDto input)
    {
        _logger.LogInformation("Generating stock-based production plan for {Days} days", input.PlanningHorizonDays);
        
        var plan = new ProductionPlan
        {
            Id = Guid.NewGuid(),
            PlanName = input.PlanName,
            Description = "Auto-generated stock-based plan",
            PlanDate = DateTime.UtcNow,
            PlannedStartDate = input.StartDate,
            PlannedEndDate = input.StartDate.AddDays(input.PlanningHorizonDays),
            Method = ProductionPlanningMethod.StockBased,
            PlanningHorizonDays = input.PlanningHorizonDays,
            ResponsibleUserId = CurrentUser.Id?.ToString() ?? ""
        };

        // Get critical and major stock items
        var criticalStocks = await _stockRepository.GetBySeverityAsync(StockSeverity.Critical);
        var majorStocks = await _stockRepository.GetBySeverityAsync(StockSeverity.Major);
        var stocksToProcess = criticalStocks.Concat(majorStocks).ToList();

        foreach (var stock in stocksToProcess)
        {
            var recommendation = stock.GetRestockingRecommendation();
            if (recommendation.IsActionRequired)
            {
                var scheduledDate = CalculateScheduledDate(input.StartDate, recommendation.Priority);
                plan.AddProductionOrder(
                    stock.ProductCode,
                    recommendation.RecommendedQuantity,
                    scheduledDate,
                    MapToProductionPriority(recommendation.Priority));
            }
        }

        await _planRepository.InsertAsync(plan);
        
        _logger.LogInformation("Generated production plan {PlanId} with {OrderCount} orders", 
            plan.Id, plan.TotalOrders);

        return ObjectMapper.Map<ProductionPlan, ProductionPlanDto>(plan);
    }

    public async Task<ProductionPlanDto> OptimizePlanBatchesAsync(Guid planId, OptimizeBatchesDto input)
    {
        var plan = await _planRepository.GetAsync(planId);
        
        // Get sales velocity data
        var salesVelocity = await GetSalesVelocityData(plan.ProductionOrders.Select(o => o.ProductCode));
        
        // Optimize batches
        plan.OptimizeBatches(salesVelocity, input.MaxBatchWeight);
        
        await _planRepository.UpdateAsync(plan);
        
        _logger.LogInformation("Optimized batches for production plan {PlanId}", planId);
        
        return ObjectMapper.Map<ProductionPlan, ProductionPlanDto>(plan);
    }

    public async Task<ProductionOrderDto> StartProductionOrderAsync(Guid orderId, StartProductionOrderDto input)
    {
        var order = await GetProductionOrderAsync(orderId);
        
        order.StartProduction(CurrentUser.Id?.ToString() ?? "", input.BatchNumber);
        
        await _planRepository.UpdateAsync(order.Plan);
        
        _logger.LogInformation("Started production order {OrderId} with batch {BatchNumber}", 
            orderId, input.BatchNumber);
        
        return ObjectMapper.Map<ProductionOrder, ProductionOrderDto>(order);
    }

    public async Task<ProductionOrderDto> CompleteProductionOrderAsync(Guid orderId, CompleteProductionOrderDto input)
    {
        var order = await GetProductionOrderAsync(orderId);
        
        order.CompleteProduction(input.ActualQuantity, input.Notes);
        
        await _planRepository.UpdateAsync(order.Plan);
        
        // Update stock levels
        await UpdateStockLevels(order.ProductCode, input.ActualQuantity);
        
        _logger.LogInformation("Completed production order {OrderId} with quantity {Quantity}", 
            orderId, input.ActualQuantity);
        
        return ObjectMapper.Map<ProductionOrder, ProductionOrderDto>(order);
    }

    public async Task<List<ProductionRecommendationDto>> GetProductionRecommendationsAsync(GetProductionRecommendationsQuery query)
    {
        var recommendations = new List<ProductionRecommendationDto>();
        
        // Get stocks by severity
        var criticalStocks = await _stockRepository.GetBySeverityAsync(StockSeverity.Critical);
        var majorStocks = await _stockRepository.GetBySeverityAsync(StockSeverity.Major);
        
        foreach (var stock in criticalStocks.Concat(majorStocks))
        {
            var restockRec = stock.GetRestockingRecommendation();
            if (restockRec.IsActionRequired)
            {
                recommendations.Add(new ProductionRecommendationDto
                {
                    ProductCode = stock.ProductCode,
                    ProductName = stock.ProductName,
                    CurrentStock = stock.OnStockSum,
                    MinimumStock = stock.StockMinSetup,
                    OptimalStock = (int)(stock.DailySalesSum * stock.OptimalStockDaysSetup),
                    RecommendedQuantity = restockRec.RecommendedQuantity,
                    Priority = restockRec.Priority,
                    Reason = restockRec.Reason,
                    DaysOfStock = stock.OptimalStockDaysForecasted,
                    DailySales = stock.DailySalesSum,
                    Severity = stock.Severity
                });
            }
        }
        
        return recommendations.OrderBy(r => r.Priority).ThenBy(r => r.DaysOfStock).ToList();
    }

    public async Task<MaterialAvailabilityReportDto> CheckMaterialAvailabilityAsync(Guid planId)
    {
        var plan = await _planRepository.GetAsync(planId);
        var report = new MaterialAvailabilityReportDto
        {
            PlanId = planId,
            PlanName = plan.PlanName,
            CheckDate = DateTime.UtcNow
        };

        foreach (var requirement in plan.MaterialRequirements)
        {
            var available = await _catalogRepository.GetMaterialStockAsync(requirement.MaterialCode);
            requirement.UpdateAvailability(available);
            
            report.Requirements.Add(new MaterialRequirementSummaryDto
            {
                MaterialCode = requirement.MaterialCode,
                MaterialName = requirement.MaterialName,
                RequiredQuantity = requirement.RequiredQuantity,
                AvailableQuantity = requirement.AvailableQuantity,
                ShortfallQuantity = requirement.ShortfallQuantity,
                AvailabilityPercentage = requirement.AvailabilityPercentage,
                Status = requirement.Status
            });
        }

        report.TotalRequirements = report.Requirements.Count;
        report.AvailableRequirements = report.Requirements.Count(r => r.Status == MaterialRequirementStatus.Available);
        report.ShortfallRequirements = report.Requirements.Count(r => r.Status == MaterialRequirementStatus.Shortfall);
        report.OverallAvailabilityPercentage = report.Requirements.Any() 
            ? report.Requirements.Average(r => r.AvailabilityPercentage) : 100;

        await _planRepository.UpdateAsync(plan);
        
        return report;
    }

    private async Task<Dictionary<string, decimal>> GetSalesVelocityData(IEnumerable<string> productCodes)
    {
        var salesVelocity = new Dictionary<string, decimal>();
        
        foreach (var productCode in productCodes)
        {
            var stock = await _stockRepository.GetByProductCodeAsync(productCode);
            salesVelocity[productCode] = stock?.DailySalesSum ?? 1;
        }
        
        return salesVelocity;
    }

    private DateTime CalculateScheduledDate(DateTime startDate, RestockingPriority priority)
    {
        return priority switch
        {
            RestockingPriority.Urgent => startDate,
            RestockingPriority.High => startDate.AddDays(1),
            RestockingPriority.Normal => startDate.AddDays(3),
            _ => startDate.AddDays(7)
        };
    }

    private ProductionPriority MapToProductionPriority(RestockingPriority restockingPriority)
    {
        return restockingPriority switch
        {
            RestockingPriority.Urgent => ProductionPriority.Critical,
            RestockingPriority.High => ProductionPriority.High,
            RestockingPriority.Normal => ProductionPriority.Normal,
            _ => ProductionPriority.Low
        };
    }

    private async Task<ProductionOrder> GetProductionOrderAsync(Guid orderId)
    {
        var plans = await _planRepository.GetListAsync();
        var order = plans.SelectMany(p => p.ProductionOrders).FirstOrDefault(o => o.Id == orderId);
        
        if (order == null)
            throw new EntityNotFoundException($"Production order {orderId} not found");
            
        return order;
    }

    private async Task UpdateStockLevels(string productCode, decimal quantity)
    {
        // Implementation to update stock levels after production completion
        await _catalogRepository.UpdateProductStockAsync(productCode, quantity);
    }
}
```

### Batch Distribution Integration

#### IBatchDistributionCalculator
```csharp
public interface IBatchDistributionCalculator
{
    OptimizedBatch OptimizeBatch(ProductBatch batch, Dictionary<string, decimal> salesVelocity);
    OptimizedBatch OptimizeBatch2(ProductBatch batch, Dictionary<string, decimal> salesVelocity);
    List<OptimizedBatch> OptimizeMultipleBatches(List<ProductBatch> batches, Dictionary<string, decimal> salesVelocity);
    BatchOptimizationReport GenerateOptimizationReport(ProductBatch originalBatch, OptimizedBatch optimizedBatch);
}
```

### Performance Requirements

#### Response Time Targets
- Plan generation: < 10 seconds
- Batch optimization: < 5 seconds
- Order operations: < 2 seconds
- Material availability check: < 3 seconds

#### Scalability
- Support 1000+ production orders per plan
- Handle 50+ concurrent production plans
- Process complex batch optimization efficiently
- Scale with manufacturing complexity

### Happy Day Scenarios

#### Scenario 1: Stock-Based Plan Generation
```
1. Production planner requests new plan for next 30 days
2. System analyzes critical and major stock items
3. Generates production orders based on restocking recommendations
4. Calculates material requirements for all orders
5. Optimizes batch compositions for efficiency
6. Presents plan for approval with material availability check
```

#### Scenario 2: Production Order Execution
```
1. Production manager reviews approved plan
2. Starts first batch of production orders
3. Assigns operators and allocates resources
4. Tracks production progress in real-time
5. Records actual quantities upon completion
6. Updates stock levels and triggers next orders
```

#### Scenario 3: Material Requirements Planning
```
1. Plan calculates material needs from all orders
2. Checks current material availability
3. Identifies shortfalls and procurement needs
4. Generates purchase recommendations
5. Tracks material deliveries and availability
6. Updates plan feasibility continuously
```

### Error Scenarios

#### Scenario 1: Insufficient Materials
```
User: Approves plan with material shortfalls
System: Shows error "Insufficient materials for production"
Action: Display shortfall details, suggest procurement or plan adjustment
```

#### Scenario 2: Production Order Conflicts
```
User: Schedules orders exceeding capacity
System: Shows warning "Production capacity exceeded"
Action: Suggest rescheduling or capacity optimization
```

#### Scenario 3: Batch Optimization Failure
```
User: Requests batch optimization with invalid constraints
System: Shows error "Cannot optimize with current constraints"
Action: Suggest constraint adjustments, provide alternative approaches
```