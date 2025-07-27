# User Story: Inventory Reconciliation

## Feature Description
The Inventory Reconciliation feature provides comprehensive inventory variance analysis and correction processes across warehouse systems, transport boxes, and external integrations. It identifies discrepancies between physical inventory, system records, and external platforms, enabling automated and manual reconciliation workflows with complete audit trails.

## Business Requirements

### Primary Use Cases
1. **Multi-System Variance Detection**: Identify discrepancies between warehouse, transport boxes, and external systems
2. **Automated Reconciliation**: Automatically resolve minor variances based on configurable rules
3. **Manual Reconciliation Workflows**: Handle complex discrepancies requiring human intervention
4. **Audit Trail Management**: Maintain complete history of all reconciliation activities
5. **Root Cause Analysis**: Investigate and categorize variance sources for process improvement

### User Stories
- As an inventory manager, I want to detect variances across all systems so I can maintain accurate inventory
- As a warehouse supervisor, I want automated reconciliation so minor discrepancies are resolved efficiently
- As an operations manager, I want root cause analysis so I can improve inventory processes
- As an auditor, I want complete reconciliation trails so I can verify inventory accuracy compliance

## Technical Requirements

### Domain Models

#### InventoryReconciliation
```csharp
public class InventoryReconciliation : AuditedAggregateRoot<Guid>
{
    public string ReconciliationNumber { get; set; } = "";
    public string ReconciliationName { get; set; } = "";
    public ReconciliationType Type { get; set; } = ReconciliationType.FullInventory;
    public ReconciliationStatus Status { get; set; } = ReconciliationStatus.Planned;
    public DateTime PlannedDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? CompletionDate { get; set; }
    public string ResponsibleUserId { get; set; } = "";
    public string? WarehouseCode { get; set; }
    public bool IncludeTransportBoxes { get; set; } = true;
    public bool IncludeExternalSystems { get; set; } = true;
    public string? Notes { get; set; }
    public ReconciliationScope Scope { get; set; } = ReconciliationScope.All;
    
    // Navigation Properties
    public virtual ICollection<InventoryVariance> Variances { get; set; } = new List<InventoryVariance>();
    public virtual ICollection<ReconciliationAction> Actions { get; set; } = new List<ReconciliationAction>();
    public virtual ICollection<ReconciliationRule> Rules { get; set; } = new List<ReconciliationRule>();
    public virtual ICollection<SystemSnapshot> Snapshots { get; set; } = new List<SystemSnapshot>();
    
    // Computed Properties
    public TimeSpan? Duration => CompletionDate - StartDate;
    public int TotalVariances => Variances.Count;
    public int ResolvedVariances => Variances.Count(v => v.Status == VarianceStatus.Resolved);
    public int PendingVariances => Variances.Count(v => v.Status == VarianceStatus.Identified);
    public int AutoResolvedVariances => Actions.Count(a => a.ActionType == ReconciliationActionType.AutoResolve);
    public int ManualResolvedVariances => Actions.Count(a => a.ActionType == ReconciliationActionType.ManualResolve);
    public decimal TotalVarianceValue => Variances.Sum(v => Math.Abs(v.VarianceValue));
    public bool IsCompleted => Status == ReconciliationStatus.Completed;
    public bool HasSignificantVariances => Variances.Any(v => v.IsSignificant);
    public decimal ResolutionPercentage => TotalVariances > 0 ? (ResolvedVariances / (decimal)TotalVariances) * 100 : 100;
    
    // Business Methods
    public void StartReconciliation(string userId)
    {
        if (Status != ReconciliationStatus.Planned)
            throw new BusinessException("Can only start planned reconciliations");
            
        Status = ReconciliationStatus.InProgress;
        StartDate = DateTime.UtcNow;
        ResponsibleUserId = userId;
        
        // Create system snapshots
        CreateSystemSnapshots();
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void AddVariance(string productCode, string source1, string source2, decimal quantity1, decimal quantity2, string location, VarianceCategory category)
    {
        if (Status != ReconciliationStatus.InProgress)
            throw new BusinessException("Can only add variances to active reconciliations");
            
        var variance = quantity1 - quantity2;
        if (Math.Abs(variance) < 0.001m)
            return; // No variance
            
        var varianceRecord = new InventoryVariance
        {
            Id = Guid.NewGuid(),
            ReconciliationId = Id,
            ProductCode = productCode,
            Source1 = source1,
            Source2 = source2,
            Quantity1 = quantity1,
            Quantity2 = quantity2,
            Variance = variance,
            VariancePercentage = quantity1 > 0 ? (variance / quantity1) * 100 : 0,
            Location = location,
            Category = category,
            Status = VarianceStatus.Identified,
            IdentifiedDate = DateTime.UtcNow,
            IsSignificant = DetermineSignificance(variance, quantity1, category)
        };
        
        Variances.Add(varianceRecord);
        
        // Try auto-resolution if applicable
        TryAutoResolveVariance(varianceRecord);
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void ResolveVariance(Guid varianceId, ReconciliationActionType actionType, string resolvedBy, string resolution, string? targetSystem = null)
    {
        var variance = Variances.FirstOrDefault(v => v.Id == varianceId);
        if (variance == null)
            throw new BusinessException($"Variance {varianceId} not found");
            
        if (variance.Status == VarianceStatus.Resolved)
            throw new BusinessException("Variance already resolved");
            
        variance.Resolve(resolvedBy, resolution);
        
        var action = new ReconciliationAction
        {
            Id = Guid.NewGuid(),
            ReconciliationId = Id,
            VarianceId = varianceId,
            ActionType = actionType,
            TargetSystem = targetSystem,
            ActionDescription = resolution,
            PerformedBy = resolvedBy,
            ActionDate = DateTime.UtcNow,
            Status = ActionStatus.Completed
        };
        
        Actions.Add(action);
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void AddReconciliationRule(string productPattern, decimal toleranceAmount, decimal tolerancePercentage, ReconciliationActionType autoAction)
    {
        var rule = new ReconciliationRule
        {
            Id = Guid.NewGuid(),
            ReconciliationId = Id,
            ProductPattern = productPattern,
            ToleranceAmount = toleranceAmount,
            TolerancePercentage = tolerancePercentage,
            AutoAction = autoAction,
            IsActive = true
        };
        
        Rules.Add(rule);
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CompleteReconciliation()
    {
        if (Status != ReconciliationStatus.InProgress)
            throw new BusinessException("Can only complete in-progress reconciliations");
            
        if (PendingVariances > 0)
            throw new BusinessException("Cannot complete reconciliation with unresolved variances");
            
        Status = ReconciliationStatus.Completed;
        CompletionDate = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void ApproveReconciliation(string approvedBy)
    {
        if (Status != ReconciliationStatus.Completed)
            throw new BusinessException("Can only approve completed reconciliations");
            
        Status = ReconciliationStatus.Approved;
        Notes = $"Approved by {approvedBy} on {DateTime.UtcNow:yyyy-MM-dd HH:mm}";
        LastModificationTime = DateTime.UtcNow;
    }
    
    public ReconciliationSummary GetSummary()
    {
        return new ReconciliationSummary
        {
            ReconciliationId = Id,
            ReconciliationNumber = ReconciliationNumber,
            Type = Type,
            Status = Status,
            StartDate = StartDate,
            CompletionDate = CompletionDate,
            Duration = Duration,
            TotalVariances = TotalVariances,
            ResolvedVariances = ResolvedVariances,
            PendingVariances = PendingVariances,
            ResolutionPercentage = ResolutionPercentage,
            TotalVarianceValue = TotalVarianceValue,
            AutoResolvedCount = AutoResolvedVariances,
            ManualResolvedCount = ManualResolvedVariances,
            SignificantVariances = Variances.Count(v => v.IsSignificant),
            VariancesByCategory = Variances.GroupBy(v => v.Category)
                                         .ToDictionary(g => g.Key, g => g.Count()),
            VariancesBySource = Variances.GroupBy(v => $"{v.Source1} vs {v.Source2}")
                                        .ToDictionary(g => g.Key, g => g.Count())
        };
    }
    
    private void CreateSystemSnapshots()
    {
        var warehouseSnapshot = new SystemSnapshot
        {
            Id = Guid.NewGuid(),
            ReconciliationId = Id,
            SystemName = "Warehouse",
            SnapshotDate = DateTime.UtcNow,
            RecordCount = 0, // Will be populated by service
            TotalValue = 0   // Will be populated by service
        };
        
        Snapshots.Add(warehouseSnapshot);
        
        if (IncludeTransportBoxes)
        {
            var transportSnapshot = new SystemSnapshot
            {
                Id = Guid.NewGuid(),
                ReconciliationId = Id,
                SystemName = "TransportBoxes",
                SnapshotDate = DateTime.UtcNow,
                RecordCount = 0,
                TotalValue = 0
            };
            
            Snapshots.Add(transportSnapshot);
        }
        
        if (IncludeExternalSystems)
        {
            var externalSnapshot = new SystemSnapshot
            {
                Id = Guid.NewGuid(),
                ReconciliationId = Id,
                SystemName = "ExternalSystems",
                SnapshotDate = DateTime.UtcNow,
                RecordCount = 0,
                TotalValue = 0
            };
            
            Snapshots.Add(externalSnapshot);
        }
    }
    
    private void TryAutoResolveVariance(InventoryVariance variance)
    {
        var applicableRule = Rules
            .Where(r => r.IsActive && IsRuleApplicable(r, variance))
            .OrderBy(r => r.ToleranceAmount)
            .FirstOrDefault();
            
        if (applicableRule != null && CanAutoResolve(variance, applicableRule))
        {
            ResolveVariance(variance.Id, applicableRule.AutoAction, "System", 
                $"Auto-resolved using rule: {applicableRule.ProductPattern}");
        }
    }
    
    private bool IsRuleApplicable(ReconciliationRule rule, InventoryVariance variance)
    {
        // Check if product matches pattern
        if (!string.IsNullOrEmpty(rule.ProductPattern) && rule.ProductPattern != "*")
        {
            // Simple pattern matching - could be enhanced with regex
            if (!variance.ProductCode.Contains(rule.ProductPattern))
                return false;
        }
        
        return true;
    }
    
    private bool CanAutoResolve(InventoryVariance variance, ReconciliationRule rule)
    {
        var absVariance = Math.Abs(variance.Variance);
        var absPercentage = Math.Abs(variance.VariancePercentage);
        
        return absVariance <= rule.ToleranceAmount || absPercentage <= rule.TolerancePercentage;
    }
    
    private bool DetermineSignificance(decimal variance, decimal quantity, VarianceCategory category)
    {
        var absVariance = Math.Abs(variance);
        var absPercentage = quantity > 0 ? Math.Abs(variance / quantity) * 100 : 0;
        
        return category switch
        {
            VarianceCategory.HighValue => absVariance > 1000 || absPercentage > 5,
            VarianceCategory.Controlled => absVariance > 10 || absPercentage > 2,
            VarianceCategory.Regular => absVariance > 100 || absPercentage > 10,
            _ => absVariance > 50 || absPercentage > 15
        };
    }
}

public enum ReconciliationType
{
    FullInventory,
    CycleCount,
    SystemComparison,
    ExternalSync,
    TransportBoxAudit
}

public enum ReconciliationStatus
{
    Planned,
    InProgress,
    Completed,
    Approved,
    Cancelled
}

public enum ReconciliationScope
{
    All,
    Warehouse,
    TransportBoxes,
    ExternalSystems,
    SpecificProducts
}
```

#### InventoryVariance
```csharp
public class InventoryVariance : AuditedEntity<Guid>
{
    public Guid ReconciliationId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Source1 { get; set; } = "";
    public string Source2 { get; set; } = "";
    public decimal Quantity1 { get; set; }
    public decimal Quantity2 { get; set; }
    public decimal Variance { get; set; }
    public decimal VariancePercentage { get; set; }
    public decimal VarianceValue { get; set; }
    public string Location { get; set; } = "";
    public VarianceCategory Category { get; set; } = VarianceCategory.Regular;
    public VarianceStatus Status { get; set; } = VarianceStatus.Identified;
    public DateTime IdentifiedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedDate { get; set; }
    public string? ResolvedBy { get; set; }
    public string? Resolution { get; set; }
    public bool IsSignificant { get; set; }
    public string? RootCause { get; set; }
    public string? Notes { get; set; }
    
    // Navigation Properties
    public virtual InventoryReconciliation Reconciliation { get; set; } = null!;
    
    // Computed Properties
    public bool IsIncrease => Variance > 0;
    public bool IsDecrease => Variance < 0;
    public decimal AbsoluteVariance => Math.Abs(Variance);
    public decimal AbsoluteVariancePercentage => Math.Abs(VariancePercentage);
    public string VarianceDirection => IsIncrease ? "Increase" : "Decrease";
    public bool IsResolved => Status == VarianceStatus.Resolved;
    public TimeSpan? ResolutionTime => ResolvedDate - IdentifiedDate;
    
    // Business Methods
    public void Resolve(string resolvedBy, string resolution)
    {
        Status = VarianceStatus.Resolved;
        ResolvedBy = resolvedBy;
        Resolution = resolution;
        ResolvedDate = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void SetRootCause(string rootCause, string? notes = null)
    {
        RootCause = rootCause;
        if (!string.IsNullOrEmpty(notes))
            Notes = notes;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void Investigate(string investigatedBy, string findings)
    {
        Status = VarianceStatus.Investigating;
        Notes = $"Investigated by {investigatedBy}: {findings}";
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void Escalate(string reason)
    {
        Status = VarianceStatus.Escalated;
        IsSignificant = true;
        Notes = string.IsNullOrEmpty(Notes) ? $"Escalated: {reason}" : $"{Notes}\nEscalated: {reason}";
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void UpdateValue(decimal unitValue)
    {
        VarianceValue = Variance * unitValue;
        LastModificationTime = DateTime.UtcNow;
    }
}

public enum VarianceCategory
{
    Regular,
    HighValue,
    Controlled,
    Serialized,
    Hazardous
}

public enum VarianceStatus
{
    Identified,
    Investigating,
    Resolved,
    Escalated,
    Accepted
}
```

#### ReconciliationAction
```csharp
public class ReconciliationAction : AuditedEntity<Guid>
{
    public Guid ReconciliationId { get; set; }
    public Guid VarianceId { get; set; }
    public ReconciliationActionType ActionType { get; set; }
    public string? TargetSystem { get; set; }
    public string ActionDescription { get; set; } = "";
    public string PerformedBy { get; set; } = "";
    public DateTime ActionDate { get; set; } = DateTime.UtcNow;
    public ActionStatus Status { get; set; } = ActionStatus.Pending;
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime? CompletedDate { get; set; }
    
    // Navigation Properties
    public virtual InventoryReconciliation Reconciliation { get; set; } = null!;
    public virtual InventoryVariance Variance { get; set; } = null!;
    
    // Computed Properties
    public bool IsCompleted => Status == ActionStatus.Completed;
    public bool HasError => Status == ActionStatus.Failed;
    public bool IsAutomatic => ActionType == ReconciliationActionType.AutoResolve;
    public TimeSpan? ExecutionTime => CompletedDate - ActionDate;
    
    // Business Methods
    public void MarkCompleted(string result)
    {
        Status = ActionStatus.Completed;
        Result = result;
        CompletedDate = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void MarkFailed(string errorMessage)
    {
        Status = ActionStatus.Failed;
        ErrorMessage = errorMessage;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void Retry()
    {
        RetryCount++;
        Status = ActionStatus.Pending;
        ErrorMessage = null;
        LastModificationTime = DateTime.UtcNow;
    }
}

public enum ReconciliationActionType
{
    AutoResolve,
    ManualResolve,
    AdjustWarehouse,
    AdjustTransportBox,
    AdjustExternal,
    CreateStockTaking,
    Investigate
}

public enum ActionStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled
}
```

#### ReconciliationRule
```csharp
public class ReconciliationRule : AuditedEntity<Guid>
{
    public Guid ReconciliationId { get; set; }
    public string ProductPattern { get; set; } = ""; // "*" for all, or specific pattern
    public decimal ToleranceAmount { get; set; }
    public decimal TolerancePercentage { get; set; }
    public ReconciliationActionType AutoAction { get; set; }
    public string? TargetSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public int Priority { get; set; } = 100;
    
    // Navigation Properties
    public virtual InventoryReconciliation Reconciliation { get; set; } = null!;
    
    // Business Methods
    public void Activate()
    {
        IsActive = true;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void Deactivate()
    {
        IsActive = false;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void UpdateTolerance(decimal amount, decimal percentage)
    {
        ToleranceAmount = amount;
        TolerancePercentage = percentage;
        LastModificationTime = DateTime.UtcNow;
    }
}
```

#### SystemSnapshot
```csharp
public class SystemSnapshot : Entity<Guid>
{
    public Guid ReconciliationId { get; set; }
    public string SystemName { get; set; } = "";
    public DateTime SnapshotDate { get; set; } = DateTime.UtcNow;
    public int RecordCount { get; set; }
    public decimal TotalValue { get; set; }
    public string? DataHash { get; set; }
    public string? Notes { get; set; }
    
    // Navigation Properties
    public virtual InventoryReconciliation Reconciliation { get; set; } = null!;
    
    // Computed Properties
    public decimal AverageValue => RecordCount > 0 ? TotalValue / RecordCount : 0;
}
```

#### ReconciliationSummary (Value Object)
```csharp
public class ReconciliationSummary : ValueObject
{
    public Guid ReconciliationId { get; set; }
    public string ReconciliationNumber { get; set; } = "";
    public ReconciliationType Type { get; set; }
    public ReconciliationStatus Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? CompletionDate { get; set; }
    public TimeSpan? Duration { get; set; }
    public int TotalVariances { get; set; }
    public int ResolvedVariances { get; set; }
    public int PendingVariances { get; set; }
    public decimal ResolutionPercentage { get; set; }
    public decimal TotalVarianceValue { get; set; }
    public int AutoResolvedCount { get; set; }
    public int ManualResolvedCount { get; set; }
    public int SignificantVariances { get; set; }
    public Dictionary<VarianceCategory, int> VariancesByCategory { get; set; } = new();
    public Dictionary<string, int> VariancesBySource { get; set; } = new();
    
    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return ReconciliationId;
        yield return Type;
        yield return Status;
        yield return TotalVariances;
        yield return ResolvedVariances;
        yield return ResolutionPercentage;
        yield return TotalVarianceValue;
    }
    
    public bool IsCompleted => Status == ReconciliationStatus.Completed || Status == ReconciliationStatus.Approved;
    public bool HasIssues => SignificantVariances > 0 || PendingVariances > 0;
    public string OverallHealth => HasIssues ? "Issues Detected" : IsCompleted ? "Healthy" : "In Progress";
    public double AutoResolutionRate => TotalVariances > 0 ? (AutoResolvedCount / (double)TotalVariances) * 100 : 0;
    public double SignificanceRate => TotalVariances > 0 ? (SignificantVariances / (double)TotalVariances) * 100 : 0;
}
```

### Application Services

#### IInventoryReconciliationAppService
```csharp
public interface IInventoryReconciliationAppService : IApplicationService
{
    Task<InventoryReconciliationDto> CreateReconciliationAsync(CreateInventoryReconciliationDto input);
    Task<InventoryReconciliationDto> GetReconciliationAsync(Guid reconciliationId);
    Task<PagedResultDto<InventoryReconciliationDto>> GetReconciliationsAsync(GetInventoryReconciliationsQuery query);
    Task<InventoryReconciliationDto> StartReconciliationAsync(Guid reconciliationId);
    Task<InventoryReconciliationDto> CompleteReconciliationAsync(Guid reconciliationId);
    Task<InventoryReconciliationDto> ApproveReconciliationAsync(Guid reconciliationId);
    Task CancelReconciliationAsync(Guid reconciliationId, string reason);
    
    Task<VarianceDetectionResultDto> DetectVariancesAsync(Guid reconciliationId);
    Task<List<InventoryVarianceDto>> GetVariancesAsync(Guid reconciliationId, VarianceStatus? status = null);
    Task ResolveVarianceAsync(Guid varianceId, ResolveVarianceDto input);
    Task<InventoryVarianceDto> InvestigateVarianceAsync(Guid varianceId, InvestigateVarianceDto input);
    Task<InventoryVarianceDto> EscalateVarianceAsync(Guid varianceId, string reason);
    
    Task<InventoryReconciliationDto> AddRuleAsync(Guid reconciliationId, AddReconciliationRuleDto input);
    Task<InventoryReconciliationDto> UpdateRuleAsync(Guid ruleId, UpdateReconciliationRuleDto input);
    Task<InventoryReconciliationDto> RemoveRuleAsync(Guid ruleId);
    
    Task<List<ReconciliationActionDto>> GetActionsAsync(Guid reconciliationId);
    Task<ReconciliationActionDto> ExecuteActionAsync(Guid actionId);
    Task<ReconciliationActionDto> RetryActionAsync(Guid actionId);
    
    Task<ReconciliationSummaryDto> GetSummaryAsync(Guid reconciliationId);
    Task<VarianceAnalysisReportDto> GetVarianceAnalysisAsync(Guid reconciliationId);
    Task<RootCauseAnalysisReportDto> GetRootCauseAnalysisAsync(Guid reconciliationId);
    Task<ReconciliationComparisonReportDto> GetComparisonReportAsync(List<Guid> reconciliationIds);
    
    Task<List<SystemInventoryDto>> GetSystemInventoryAsync(string systemName, string? productFilter = null);
    Task<InventoryComparisonDto> CompareSystemsAsync(CompareSystemsDto input);
    Task<List<ReconciliationRecommendationDto>> GetRecommendationsAsync(string? warehouseCode = null);
}
```

#### InventoryReconciliationAppService Implementation
```csharp
[Authorize]
public class InventoryReconciliationAppService : ApplicationService, IInventoryReconciliationAppService
{
    private readonly IInventoryReconciliationRepository _reconciliationRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly IShoptetIntegrationRepository _shoptetRepository;
    private readonly IInventoryComparisonService _comparisonService;
    private readonly ILogger<InventoryReconciliationAppService> _logger;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public InventoryReconciliationAppService(
        IInventoryReconciliationRepository reconciliationRepository,
        ICatalogRepository catalogRepository,
        ITransportBoxRepository transportBoxRepository,
        IShoptetIntegrationRepository shoptetRepository,
        IInventoryComparisonService comparisonService,
        ILogger<InventoryReconciliationAppService> logger,
        IBackgroundJobManager backgroundJobManager)
    {
        _reconciliationRepository = reconciliationRepository;
        _catalogRepository = catalogRepository;
        _transportBoxRepository = transportBoxRepository;
        _shoptetRepository = shoptetRepository;
        _comparisonService = comparisonService;
        _logger = logger;
        _backgroundJobManager = backgroundJobManager;
    }

    public async Task<InventoryReconciliationDto> CreateReconciliationAsync(CreateInventoryReconciliationDto input)
    {
        var reconciliation = new InventoryReconciliation
        {
            Id = Guid.NewGuid(),
            ReconciliationNumber = await GenerateReconciliationNumberAsync(),
            ReconciliationName = input.ReconciliationName,
            Type = input.Type,
            PlannedDate = input.PlannedDate,
            WarehouseCode = input.WarehouseCode,
            IncludeTransportBoxes = input.IncludeTransportBoxes,
            IncludeExternalSystems = input.IncludeExternalSystems,
            Scope = input.Scope,
            ResponsibleUserId = CurrentUser.Id?.ToString() ?? ""
        };

        // Add default reconciliation rules
        if (input.UseDefaultRules)
        {
            AddDefaultRules(reconciliation);
        }

        // Add custom rules
        foreach (var ruleDto in input.Rules ?? new List<AddReconciliationRuleDto>())
        {
            reconciliation.AddReconciliationRule(
                ruleDto.ProductPattern,
                ruleDto.ToleranceAmount,
                ruleDto.TolerancePercentage,
                ruleDto.AutoAction);
        }

        await _reconciliationRepository.InsertAsync(reconciliation);
        
        _logger.LogInformation("Created inventory reconciliation {ReconciliationNumber} of type {Type}", 
            reconciliation.ReconciliationNumber, reconciliation.Type);

        return ObjectMapper.Map<InventoryReconciliation, InventoryReconciliationDto>(reconciliation);
    }

    public async Task<InventoryReconciliationDto> StartReconciliationAsync(Guid reconciliationId)
    {
        var reconciliation = await _reconciliationRepository.GetAsync(reconciliationId);
        
        reconciliation.StartReconciliation(CurrentUser.Id?.ToString() ?? "");
        await _reconciliationRepository.UpdateAsync(reconciliation);
        
        // Queue background job for variance detection
        await _backgroundJobManager.EnqueueAsync<DetectInventoryVariancesJob>(
            new DetectInventoryVariancesArgs { ReconciliationId = reconciliationId });
        
        _logger.LogInformation("Started inventory reconciliation {ReconciliationId}", reconciliationId);

        return ObjectMapper.Map<InventoryReconciliation, InventoryReconciliationDto>(reconciliation);
    }

    public async Task<VarianceDetectionResultDto> DetectVariancesAsync(Guid reconciliationId)
    {
        var reconciliation = await _reconciliationRepository.GetAsync(reconciliationId);
        
        if (reconciliation.Status != ReconciliationStatus.InProgress)
            throw new BusinessException("Can only detect variances for active reconciliations");

        var startTime = DateTime.UtcNow;
        var variancesDetected = 0;

        try
        {
            _logger.LogInformation("Starting variance detection for reconciliation {ReconciliationId}", reconciliationId);
            
            // Get inventory from all systems
            var warehouseInventory = await GetWarehouseInventoryAsync(reconciliation.WarehouseCode);
            var comparisons = new List<InventoryComparison>();
            
            if (reconciliation.IncludeTransportBoxes)
            {
                var transportBoxInventory = await GetTransportBoxInventoryAsync(reconciliation.WarehouseCode);
                comparisons.Add(new InventoryComparison
                {
                    Source1 = "Warehouse",
                    Source2 = "TransportBoxes",
                    Inventory1 = warehouseInventory,
                    Inventory2 = transportBoxInventory
                });
            }
            
            if (reconciliation.IncludeExternalSystems)
            {
                var externalInventory = await GetExternalSystemInventoryAsync();
                comparisons.Add(new InventoryComparison
                {
                    Source1 = "Warehouse",
                    Source2 = "External",
                    Inventory1 = warehouseInventory,
                    Inventory2 = externalInventory
                });
            }
            
            // Detect variances for each comparison
            foreach (var comparison in comparisons)
            {
                var variances = await _comparisonService.CompareInventoriesAsync(
                    comparison.Inventory1, 
                    comparison.Inventory2,
                    comparison.Source1,
                    comparison.Source2);
                
                foreach (var variance in variances)
                {
                    reconciliation.AddVariance(
                        variance.ProductCode,
                        variance.Source1,
                        variance.Source2,
                        variance.Quantity1,
                        variance.Quantity2,
                        variance.Location ?? "",
                        DetermineVarianceCategory(variance.ProductCode));
                    
                    variancesDetected++;
                }
            }
            
            await _reconciliationRepository.UpdateAsync(reconciliation);
            
            _logger.LogInformation("Completed variance detection for reconciliation {ReconciliationId}: {Variances} variances found", 
                reconciliationId, variancesDetected);

            return new VarianceDetectionResultDto
            {
                ReconciliationId = reconciliationId,
                VariancesDetected = variancesDetected,
                Duration = DateTime.UtcNow - startTime,
                AutoResolvedCount = reconciliation.AutoResolvedVariances,
                PendingCount = reconciliation.PendingVariances
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect variances for reconciliation {ReconciliationId}", reconciliationId);
            throw;
        }
    }

    public async Task ResolveVarianceAsync(Guid varianceId, ResolveVarianceDto input)
    {
        var variance = await GetVarianceAsync(varianceId);
        var reconciliation = variance.Reconciliation;
        
        reconciliation.ResolveVariance(
            varianceId,
            input.ActionType,
            CurrentUser.UserName ?? "",
            input.Resolution,
            input.TargetSystem);
        
        await _reconciliationRepository.UpdateAsync(reconciliation);
        
        // Execute the resolution action if needed
        if (input.ExecuteImmediately)
        {
            var action = reconciliation.Actions.First(a => a.VarianceId == varianceId);
            await ExecuteReconciliationActionAsync(action);
        }
        
        _logger.LogInformation("Resolved variance {VarianceId} with action {ActionType}", 
            varianceId, input.ActionType);
    }

    public async Task<InventoryVarianceDto> InvestigateVarianceAsync(Guid varianceId, InvestigateVarianceDto input)
    {
        var variance = await GetVarianceAsync(varianceId);
        
        variance.Investigate(CurrentUser.UserName ?? "", input.Findings);
        
        if (!string.IsNullOrEmpty(input.RootCause))
        {
            variance.SetRootCause(input.RootCause, input.Notes);
        }
        
        await _reconciliationRepository.UpdateAsync(variance.Reconciliation);
        
        _logger.LogInformation("Investigated variance {VarianceId}", varianceId);

        return ObjectMapper.Map<InventoryVariance, InventoryVarianceDto>(variance);
    }

    public async Task<ReconciliationSummaryDto> GetSummaryAsync(Guid reconciliationId)
    {
        var reconciliation = await _reconciliationRepository.GetAsync(reconciliationId);
        var summary = reconciliation.GetSummary();
        
        return ObjectMapper.Map<ReconciliationSummary, ReconciliationSummaryDto>(summary);
    }

    public async Task<VarianceAnalysisReportDto> GetVarianceAnalysisAsync(Guid reconciliationId)
    {
        var reconciliation = await _reconciliationRepository.GetAsync(reconciliationId);
        
        var report = new VarianceAnalysisReportDto
        {
            ReconciliationId = reconciliationId,
            ReconciliationNumber = reconciliation.ReconciliationNumber,
            AnalysisDate = DateTime.UtcNow,
            TotalVariances = reconciliation.TotalVariances,
            TotalVarianceValue = reconciliation.TotalVarianceValue
        };
        
        // Analyze variances by various dimensions
        report.VariancesByCategory = reconciliation.Variances
            .GroupBy(v => v.Category)
            .Select(g => new VarianceCategoryAnalysisDto
            {
                Category = g.Key,
                Count = g.Count(),
                TotalValue = g.Sum(v => Math.Abs(v.VarianceValue)),
                AverageVariance = g.Average(v => Math.Abs(v.Variance)),
                SignificantCount = g.Count(v => v.IsSignificant)
            }).ToList();
        
        report.VariancesByDirection = new VarianceDirectionAnalysisDto
        {
            Increases = reconciliation.Variances.Count(v => v.IsIncrease),
            Decreases = reconciliation.Variances.Count(v => v.IsDecrease),
            IncreaseValue = reconciliation.Variances.Where(v => v.IsIncrease).Sum(v => v.VarianceValue),
            DecreaseValue = reconciliation.Variances.Where(v => v.IsDecrease).Sum(v => Math.Abs(v.VarianceValue))
        };
        
        report.TopVariancesByValue = reconciliation.Variances
            .OrderByDescending(v => Math.Abs(v.VarianceValue))
            .Take(10)
            .Select(v => ObjectMapper.Map<InventoryVariance, InventoryVarianceDto>(v))
            .ToList();
        
        return report;
    }

    private async Task<List<SystemInventoryItem>> GetWarehouseInventoryAsync(string? warehouseCode)
    {
        var inventory = await _catalogRepository.GetWarehouseInventoryAsync(warehouseCode);
        
        return inventory.Select(i => new SystemInventoryItem
        {
            ProductCode = i.ProductCode,
            ProductName = i.ProductName,
            Quantity = i.AvailableQuantity,
            Location = i.LocationCode,
            UnitValue = i.UnitValue,
            LastUpdated = i.LastUpdated
        }).ToList();
    }

    private async Task<List<SystemInventoryItem>> GetTransportBoxInventoryAsync(string? warehouseCode)
    {
        var transportBoxes = await _transportBoxRepository.GetActiveBoxesByWarehouseAsync(warehouseCode);
        var inventory = new List<SystemInventoryItem>();
        
        foreach (var box in transportBoxes)
        {
            foreach (var item in box.Items)
            {
                var existingItem = inventory.FirstOrDefault(i => i.ProductCode == item.ProductCode);
                if (existingItem != null)
                {
                    existingItem.Quantity += item.Quantity;
                }
                else
                {
                    inventory.Add(new SystemInventoryItem
                    {
                        ProductCode = item.ProductCode,
                        ProductName = item.ProductName,
                        Quantity = item.Quantity,
                        Location = $"TransportBox-{box.Code}",
                        UnitValue = item.Value / Math.Max(item.Quantity, 1),
                        LastUpdated = item.LastModificationTime ?? item.CreationTime
                    });
                }
            }
        }
        
        return inventory;
    }

    private async Task<List<SystemInventoryItem>> GetExternalSystemInventoryAsync()
    {
        var integrations = await _shoptetRepository.GetActiveIntegrationsAsync();
        var inventory = new List<SystemInventoryItem>();
        
        foreach (var integration in integrations)
        {
            foreach (var product in integration.Products.Where(p => p.SyncEnabled))
            {
                inventory.Add(new SystemInventoryItem
                {
                    ProductCode = product.ProductCode,
                    ProductName = product.ProductName,
                    Quantity = product.StockQuantity,
                    Location = $"Shoptet-{integration.IntegrationName}",
                    UnitValue = product.Price,
                    LastUpdated = product.LastSyncDate
                });
            }
        }
        
        return inventory;
    }

    private VarianceCategory DetermineVarianceCategory(string productCode)
    {
        // This would be enhanced with actual product categorization logic
        if (productCode.StartsWith("HV-")) return VarianceCategory.HighValue;
        if (productCode.StartsWith("CT-")) return VarianceCategory.Controlled;
        if (productCode.StartsWith("SR-")) return VarianceCategory.Serialized;
        if (productCode.StartsWith("HZ-")) return VarianceCategory.Hazardous;
        return VarianceCategory.Regular;
    }

    private void AddDefaultRules(InventoryReconciliation reconciliation)
    {
        // Default rules for auto-resolution
        reconciliation.AddReconciliationRule("*", 1, 1, ReconciliationActionType.AutoResolve); // 1 unit or 1% tolerance
        reconciliation.AddReconciliationRule("HV-*", 0.1m, 0.1m, ReconciliationActionType.Investigate); // High value items
        reconciliation.AddReconciliationRule("CT-*", 0, 0, ReconciliationActionType.Investigate); // Controlled items
    }

    private async Task<string> GenerateReconciliationNumberAsync()
    {
        var today = DateTime.Today;
        var dailyCount = await _reconciliationRepository.GetDailyReconciliationCountAsync(today);
        return $"REC{today:yyyyMMdd}{(dailyCount + 1):D3}";
    }

    private async Task<InventoryVariance> GetVarianceAsync(Guid varianceId)
    {
        var reconciliations = await _reconciliationRepository.GetListAsync();
        var variance = reconciliations.SelectMany(r => r.Variances).FirstOrDefault(v => v.Id == varianceId);
        
        if (variance == null)
            throw new EntityNotFoundException($"Variance {varianceId} not found");
            
        return variance;
    }

    private async Task ExecuteReconciliationActionAsync(ReconciliationAction action)
    {
        // Implementation would execute the actual reconciliation action
        // This could involve updating inventory systems, creating adjustments, etc.
    }
}
```

### Background Jobs

#### DetectInventoryVariancesJob
```csharp
public class DetectInventoryVariancesJob : IAsyncBackgroundJob<DetectInventoryVariancesArgs>
{
    private readonly IInventoryReconciliationAppService _reconciliationService;
    private readonly ILogger<DetectInventoryVariancesJob> _logger;

    public DetectInventoryVariancesJob(
        IInventoryReconciliationAppService reconciliationService,
        ILogger<DetectInventoryVariancesJob> logger)
    {
        _reconciliationService = reconciliationService;
        _logger = logger;
    }

    public async Task ExecuteAsync(DetectInventoryVariancesArgs args)
    {
        try
        {
            _logger.LogInformation("Starting variance detection job for reconciliation {ReconciliationId}", 
                args.ReconciliationId);
            
            var result = await _reconciliationService.DetectVariancesAsync(args.ReconciliationId);
            
            _logger.LogInformation("Completed variance detection job: {VariancesDetected} variances detected", 
                result.VariancesDetected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect variances for reconciliation {ReconciliationId}", 
                args.ReconciliationId);
            throw;
        }
    }
}

public class DetectInventoryVariancesArgs
{
    public Guid ReconciliationId { get; set; }
}
```

### Integration Services

#### IInventoryComparisonService
```csharp
public interface IInventoryComparisonService
{
    Task<List<InventoryVarianceData>> CompareInventoriesAsync(
        List<SystemInventoryItem> inventory1,
        List<SystemInventoryItem> inventory2,
        string source1Name,
        string source2Name);
    
    Task<InventoryComparisonResult> PerformDetailedComparisonAsync(
        List<SystemInventoryItem> inventory1,
        List<SystemInventoryItem> inventory2,
        ComparisonSettings settings);
    
    Task<List<RecommendedAction>> GetRecommendedActionsAsync(List<InventoryVarianceData> variances);
}
```

### Repository Interfaces

#### IInventoryReconciliationRepository
```csharp
public interface IInventoryReconciliationRepository : IRepository<InventoryReconciliation, Guid>
{
    Task<List<InventoryReconciliation>> GetByStatusAsync(ReconciliationStatus status);
    Task<List<InventoryReconciliation>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate);
    Task<int> GetDailyReconciliationCountAsync(DateTime date);
    Task<List<InventoryReconciliation>> GetReconciliationsWithPendingVariancesAsync();
    Task<PagedResultDto<InventoryReconciliation>> GetPagedReconciliationsAsync(
        ISpecification<InventoryReconciliation> specification,
        int pageIndex,
        int pageSize,
        string sorting = null);
}
```

### Performance Requirements

#### Response Time Targets
- Reconciliation creation: < 5 seconds
- Variance detection: < 60 seconds for 10,000 items
- Variance resolution: < 3 seconds
- Report generation: < 10 seconds

#### Scalability
- Support 100,000+ inventory items per reconciliation
- Handle 10+ concurrent reconciliation processes
- Process variance detection efficiently across multiple systems
- Maintain complete audit trails for compliance

### Happy Day Scenarios

#### Scenario 1: Automated Full Inventory Reconciliation
```
1. Inventory manager creates monthly full reconciliation
2. System starts reconciliation and takes snapshots of all systems
3. Variance detection compares warehouse, transport boxes, and external systems
4. Minor variances are automatically resolved using predefined rules
5. Significant variances are flagged for manual investigation
6. Manager reviews and resolves remaining variances
7. Reconciliation is completed and approved with full audit trail
```

#### Scenario 2: Transport Box Reconciliation
```
1. Warehouse supervisor initiates transport box reconciliation
2. System compares physical transport box contents with system records
3. Variances identified for items in transit or received boxes
4. Auto-resolution updates box states and inventory levels
5. Manual resolution handles boxes with significant discrepancies
6. Transport box inventory is synchronized with warehouse system
```

#### Scenario 3: External System Synchronization
```
1. E-commerce manager runs external system reconciliation
2. System compares warehouse inventory with Shoptet product quantities
3. Variances detected between warehouse availability and online display
4. Automatic updates push correct inventory levels to e-commerce platform
5. Manual resolution handles products with sync issues
6. Online store reflects accurate product availability
```

### Error Scenarios

#### Scenario 1: System Unavailable During Reconciliation
```
User: Starts reconciliation when external system is down
System: Shows error "External system unavailable for comparison"
Action: Use cached data where possible, flag for retry, continue with available systems
```

#### Scenario 2: Massive Variance Detection
```
User: Reconciliation detects 1000+ significant variances
System: Shows warning "Unusual variance volume detected - possible system issue"
Action: Escalate to management, investigate system problems, pause auto-resolution
```

#### Scenario 3: Conflicting Resolution Actions
```
User: Multiple systems have different "correct" quantities
System: Shows alert "Conflicting inventory data across systems"
Action: Present comparison view, require manual decision, create investigation task
```