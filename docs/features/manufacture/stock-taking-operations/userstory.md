# User Story: Stock Taking Operations

## Feature Description
The Stock Taking Operations feature manages physical inventory counting for manufacturing materials with lot-based tracking, expiration date management, and ERP synchronization. The system supports both dry-run validation and live stock adjustments with complete audit trails and integration with FlexiBee ERP system.

## Business Requirements

### Primary Use Cases
1. **Physical Inventory Counting**: Record actual inventory counts for materials
2. **Lot-Based Tracking**: Manage individual lot identification and quantities
3. **Expiration Date Management**: Track expiration dates for perishable materials
4. **ERP Synchronization**: Sync stock adjustments with FlexiBee ERP
5. **Audit Trail Management**: Maintain complete history of stock taking activities

### User Stories
- As a warehouse supervisor, I want to conduct physical stock counts so I can maintain inventory accuracy
- As a quality manager, I want to track lot expiration dates so I can ensure material quality
- As an operations manager, I want to validate stock counts before committing so I can prevent errors
- As an auditor, I want to review stock taking history so I can verify compliance

## Technical Requirements

### Domain Models

#### StockTakingSession
```csharp
public class StockTakingSession : AuditedAggregateRoot<Guid>
{
    public string SessionName { get; set; } = "";
    public string Description { get; set; } = "";
    public StockTakingType Type { get; set; } = StockTakingType.Full;
    public StockTakingStatus Status { get; set; } = StockTakingStatus.Planned;
    public DateTime PlannedDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? CompletionDate { get; set; }
    public string ResponsibleUserId { get; set; } = "";
    public string? Notes { get; set; }
    public bool IsDryRun { get; set; }
    public string? WarehouseCode { get; set; }
    
    // Navigation Properties
    public virtual ICollection<StockTakingRecord> Records { get; set; } = new List<StockTakingRecord>();
    public virtual ICollection<StockTakingDiscrepancy> Discrepancies { get; set; } = new List<StockTakingDiscrepancy>();
    
    // Computed Properties
    public TimeSpan? Duration => CompletionDate - StartDate;
    public int TotalRecords => Records.Count;
    public int CompletedRecords => Records.Count(r => r.Status == RecordStatus.Completed);
    public int PendingRecords => Records.Count(r => r.Status == RecordStatus.Pending);
    public decimal CompletionPercentage => TotalRecords > 0 ? (CompletedRecords / (decimal)TotalRecords) * 100 : 0;
    public bool HasDiscrepancies => Discrepancies.Any(d => d.IsSignificant);
    public bool IsCompleted => Status == StockTakingStatus.Completed;
    public bool CanBeCommitted => Status == StockTakingStatus.Validated && !HasDiscrepancies;
    
    // Business Methods
    public void StartSession(string userId)
    {
        if (Status != StockTakingStatus.Planned)
            throw new BusinessException("Can only start sessions in Planned status");
            
        Status = StockTakingStatus.InProgress;
        StartDate = DateTime.UtcNow;
        ResponsibleUserId = userId;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void AddMaterial(string materialCode, string materialName, decimal systemQuantity, string? lotNumber = null)
    {
        if (Status != StockTakingStatus.Planned)
            throw new BusinessException("Cannot add materials to session in progress");
            
        var record = new StockTakingRecord
        {
            SessionId = Id,
            MaterialCode = materialCode,
            MaterialName = materialName,
            SystemQuantity = systemQuantity,
            LotNumber = lotNumber,
            Status = RecordStatus.Pending
        };
        
        Records.Add(record);
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void RecordCount(string materialCode, decimal countedQuantity, string countedBy, string? lotNumber = null, DateTime? expirationDate = null, string? notes = null)
    {
        if (Status != StockTakingStatus.InProgress)
            throw new BusinessException("Can only record counts in InProgress sessions");
            
        var record = Records.FirstOrDefault(r => r.MaterialCode == materialCode && r.LotNumber == lotNumber);
        if (record == null)
            throw new BusinessException($"Material {materialCode} not found in session");
            
        record.RecordCount(countedQuantity, countedBy, expirationDate, notes);
        
        // Check for discrepancies
        CheckForDiscrepancy(record);
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CompleteSession()
    {
        if (Status != StockTakingStatus.InProgress)
            throw new BusinessException("Can only complete InProgress sessions");
            
        if (PendingRecords > 0)
            throw new BusinessException("Cannot complete session with pending records");
            
        Status = StockTakingStatus.Completed;
        CompletionDate = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void ValidateSession()
    {
        if (Status != StockTakingStatus.Completed)
            throw new BusinessException("Can only validate completed sessions");
            
        // Validate all records
        foreach (var record in Records)
        {
            record.ValidateRecord();
        }
        
        Status = StockTakingStatus.Validated;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public StockAdjustmentResult CommitToERP()
    {
        if (!CanBeCommitted)
            throw new BusinessException("Session cannot be committed - has unresolved discrepancies");
            
        var adjustments = new List<StockAdjustment>();
        
        foreach (var record in Records.Where(r => r.HasVariance))
        {
            adjustments.Add(new StockAdjustment
            {
                MaterialCode = record.MaterialCode,
                LotNumber = record.LotNumber,
                SystemQuantity = record.SystemQuantity,
                CountedQuantity = record.CountedQuantity ?? 0,
                Variance = record.Variance,
                ExpirationDate = record.ExpirationDate,
                Reason = $"Stock taking session {SessionName}",
                SessionId = Id,
                RecordId = record.Id
            });
        }
        
        Status = StockTakingStatus.Committed;
        LastModificationTime = DateTime.UtcNow;
        
        return new StockAdjustmentResult
        {
            SessionId = Id,
            TotalAdjustments = adjustments.Count,
            Adjustments = adjustments,
            CommittedAt = DateTime.UtcNow
        };
    }
    
    public void RollbackSession(string reason)
    {
        if (Status == StockTakingStatus.Committed)
            throw new BusinessException("Cannot rollback committed sessions");
            
        Status = StockTakingStatus.Cancelled;
        Notes = $"Rolled back: {reason}";
        LastModificationTime = DateTime.UtcNow;
    }
    
    private void CheckForDiscrepancy(StockTakingRecord record)
    {
        if (!record.HasVariance) return;
        
        var existingDiscrepancy = Discrepancies.FirstOrDefault(d => 
            d.MaterialCode == record.MaterialCode && d.LotNumber == record.LotNumber);
            
        if (existingDiscrepancy != null)
        {
            existingDiscrepancy.UpdateDiscrepancy(record.Variance, record.VariancePercentage);
        }
        else
        {
            var discrepancy = new StockTakingDiscrepancy
            {
                SessionId = Id,
                MaterialCode = record.MaterialCode,
                MaterialName = record.MaterialName,
                LotNumber = record.LotNumber,
                SystemQuantity = record.SystemQuantity,
                CountedQuantity = record.CountedQuantity ?? 0,
                Variance = record.Variance,
                VariancePercentage = record.VariancePercentage,
                IsSignificant = Math.Abs(record.VariancePercentage) > 5, // 5% threshold
                Status = DiscrepancyStatus.Identified
            };
            
            Discrepancies.Add(discrepancy);
        }
    }
}

public enum StockTakingType
{
    Full,
    Partial,
    Cycle,
    Spot
}

public enum StockTakingStatus
{
    Planned,
    InProgress,
    Completed,
    Validated,
    Committed,
    Cancelled
}
```

#### StockTakingRecord
```csharp
public class StockTakingRecord : AuditedEntity<Guid>
{
    public Guid SessionId { get; set; }
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string? LotNumber { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public DateTime? CountDate { get; set; }
    public string? CountedBy { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public string? Notes { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Pending;
    public string? ValidationMessage { get; set; }
    
    // Navigation Properties
    public virtual StockTakingSession Session { get; set; } = null!;
    
    // Computed Properties
    public decimal Variance => (CountedQuantity ?? 0) - SystemQuantity;
    public decimal VariancePercentage => SystemQuantity > 0 ? (Variance / SystemQuantity) * 100 : 0;
    public bool HasVariance => Math.Abs(Variance) > 0.001m;
    public bool IsSignificantVariance => Math.Abs(VariancePercentage) > 5;
    public bool IsExpired => ExpirationDate.HasValue && ExpirationDate.Value < DateTime.Today;
    public bool IsNearExpiry => ExpirationDate.HasValue && ExpirationDate.Value <= DateTime.Today.AddDays(30);
    public bool IsCounted => CountedQuantity.HasValue;
    
    // Business Methods
    public void RecordCount(decimal quantity, string countedBy, DateTime? expirationDate = null, string? notes = null)
    {
        if (Status == RecordStatus.Completed)
            throw new BusinessException("Record already completed");
            
        CountedQuantity = quantity;
        CountedBy = countedBy;
        CountDate = DateTime.UtcNow;
        ExpirationDate = expirationDate;
        Notes = notes;
        Status = RecordStatus.Completed;
    }
    
    public void ValidateRecord()
    {
        var issues = new List<string>();
        
        if (!IsCounted)
            issues.Add("No count recorded");
            
        if (IsExpired)
            issues.Add("Material is expired");
            
        if (IsSignificantVariance)
            issues.Add($"Significant variance: {VariancePercentage:F1}%");
            
        if (CountedQuantity < 0)
            issues.Add("Negative quantity not allowed");
            
        ValidationMessage = issues.Any() ? string.Join("; ", issues) : null;
    }
    
    public void ResetCount()
    {
        if (Status == RecordStatus.Validated)
            throw new BusinessException("Cannot reset validated records");
            
        CountedQuantity = null;
        CountedBy = null;
        CountDate = null;
        Notes = null;
        Status = RecordStatus.Pending;
        ValidationMessage = null;
    }
}

public enum RecordStatus
{
    Pending,
    Completed,
    Validated,
    Rejected
}
```

#### StockTakingDiscrepancy
```csharp
public class StockTakingDiscrepancy : AuditedEntity<Guid>
{
    public Guid SessionId { get; set; }
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public string? LotNumber { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
    public decimal Variance { get; set; }
    public decimal VariancePercentage { get; set; }
    public bool IsSignificant { get; set; }
    public DiscrepancyStatus Status { get; set; } = DiscrepancyStatus.Identified;
    public string? Resolution { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedDate { get; set; }
    
    // Navigation Properties
    public virtual StockTakingSession Session { get; set; } = null!;
    
    // Business Methods
    public void ResolveDiscrepancy(string resolution, string resolvedBy)
    {
        Resolution = resolution;
        ResolvedBy = resolvedBy;
        ResolvedDate = DateTime.UtcNow;
        Status = DiscrepancyStatus.Resolved;
    }
    
    public void UpdateDiscrepancy(decimal newVariance, decimal newVariancePercentage)
    {
        Variance = newVariance;
        VariancePercentage = newVariancePercentage;
        IsSignificant = Math.Abs(newVariancePercentage) > 5;
        LastModificationTime = DateTime.UtcNow;
    }
}

public enum DiscrepancyStatus
{
    Identified,
    Investigating,
    Resolved,
    Accepted
}
```

#### StockAdjustment (Value Object)
```csharp
public class StockAdjustment : ValueObject
{
    public string MaterialCode { get; set; } = "";
    public string? LotNumber { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
    public decimal Variance { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public string Reason { get; set; } = "";
    public Guid SessionId { get; set; }
    public Guid RecordId { get; set; }
    
    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return MaterialCode;
        yield return LotNumber ?? "";
        yield return SystemQuantity;
        yield return CountedQuantity;
        yield return Variance;
        yield return ExpirationDate ?? DateTime.MinValue;
        yield return SessionId;
        yield return RecordId;
    }
    
    public bool IsIncrease => Variance > 0;
    public bool IsDecrease => Variance < 0;
    public decimal AbsoluteVariance => Math.Abs(Variance);
    public string VarianceDirection => IsIncrease ? "Increase" : "Decrease";
}
```

### Application Services

#### IManufactureStockTakingAppService
```csharp
public interface IManufactureStockTakingAppService : IApplicationService
{
    Task<StockTakingSessionDto> CreateSessionAsync(CreateStockTakingSessionDto input);
    Task<StockTakingSessionDto> GetSessionAsync(Guid sessionId);
    Task<PagedResultDto<StockTakingSessionDto>> GetSessionsAsync(GetStockTakingSessionsQuery query);
    Task<StockTakingSessionDto> StartSessionAsync(Guid sessionId);
    Task<StockTakingSessionDto> AddMaterialToSessionAsync(Guid sessionId, AddMaterialToSessionDto input);
    Task<StockTakingSessionDto> RecordCountAsync(Guid sessionId, RecordCountDto input);
    Task<StockTakingSessionDto> CompleteSessionAsync(Guid sessionId);
    Task<StockTakingSessionDto> ValidateSessionAsync(Guid sessionId);
    Task<StockAdjustmentResultDto> CommitSessionAsync(Guid sessionId, bool dryRun = false);
    Task RollbackSessionAsync(Guid sessionId, string reason);
    
    Task<List<StockTakingDiscrepancyDto>> GetSessionDiscrepanciesAsync(Guid sessionId);
    Task ResolveDiscrepancyAsync(Guid discrepancyId, ResolveDiscrepancyDto input);
    
    Task<List<MaterialForStockTakingDto>> GetMaterialsForStockTakingAsync(string? warehouseCode = null);
    Task<List<LotInfoDto>> GetMaterialLotsAsync(string materialCode);
    
    Task<StockTakingReportDto> GenerateSessionReportAsync(Guid sessionId);
    Task<List<StockTakingHistoryDto>> GetStockTakingHistoryAsync(string materialCode, int months = 12);
}
```

#### ManufactureStockTakingAppService Implementation
```csharp
[Authorize]
public class ManufactureStockTakingAppService : ApplicationService, IManufactureStockTakingAppService
{
    private readonly IStockTakingSessionRepository _sessionRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IFlexiManufactureRepository _flexiRepository;
    private readonly ILogger<ManufactureStockTakingAppService> _logger;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public ManufactureStockTakingAppService(
        IStockTakingSessionRepository sessionRepository,
        ICatalogRepository catalogRepository,
        IFlexiManufactureRepository flexiRepository,
        ILogger<ManufactureStockTakingAppService> logger,
        IBackgroundJobManager backgroundJobManager)
    {
        _sessionRepository = sessionRepository;
        _catalogRepository = catalogRepository;
        _flexiRepository = flexiRepository;
        _logger = logger;
        _backgroundJobManager = backgroundJobManager;
    }

    public async Task<StockTakingSessionDto> CreateSessionAsync(CreateStockTakingSessionDto input)
    {
        var session = new StockTakingSession
        {
            Id = Guid.NewGuid(),
            SessionName = input.SessionName,
            Description = input.Description,
            Type = input.Type,
            PlannedDate = input.PlannedDate,
            ResponsibleUserId = CurrentUser.Id?.ToString() ?? "",
            IsDryRun = input.IsDryRun,
            WarehouseCode = input.WarehouseCode
        };

        // Add materials to session
        if (input.MaterialCodes?.Any() == true)
        {
            foreach (var materialCode in input.MaterialCodes)
            {
                var materialInfo = await _catalogRepository.GetMaterialInfoAsync(materialCode);
                session.AddMaterial(materialCode, materialInfo.Name, materialInfo.CurrentStock);
            }
        }
        else
        {
            // Add all materials from warehouse
            var materials = await GetMaterialsForStockTakingAsync(input.WarehouseCode);
            foreach (var material in materials)
            {
                session.AddMaterial(material.MaterialCode, material.MaterialName, material.CurrentStock);
            }
        }

        await _sessionRepository.InsertAsync(session);
        
        _logger.LogInformation("Created stock taking session {SessionName} with {Count} materials", 
            session.SessionName, session.TotalRecords);

        return ObjectMapper.Map<StockTakingSession, StockTakingSessionDto>(session);
    }

    public async Task<StockTakingSessionDto> RecordCountAsync(Guid sessionId, RecordCountDto input)
    {
        var session = await _sessionRepository.GetAsync(sessionId);
        
        session.RecordCount(
            input.MaterialCode,
            input.CountedQuantity,
            CurrentUser.UserName ?? "",
            input.LotNumber,
            input.ExpirationDate,
            input.Notes);

        await _sessionRepository.UpdateAsync(session);

        _logger.LogInformation("Recorded count for material {MaterialCode} in session {SessionId}: {Quantity}", 
            input.MaterialCode, sessionId, input.CountedQuantity);

        return ObjectMapper.Map<StockTakingSession, StockTakingSessionDto>(session);
    }

    public async Task<StockAdjustmentResultDto> CommitSessionAsync(Guid sessionId, bool dryRun = false)
    {
        var session = await _sessionRepository.GetAsync(sessionId);
        
        if (dryRun)
        {
            // Validate what would happen without committing
            var dryRunResult = session.CommitToERP();
            
            _logger.LogInformation("Dry run commit for session {SessionId} would create {Count} adjustments", 
                sessionId, dryRunResult.TotalAdjustments);
                
            return ObjectMapper.Map<StockAdjustmentResult, StockAdjustmentResultDto>(dryRunResult);
        }

        // Actual commit
        var result = session.CommitToERP();
        await _sessionRepository.UpdateAsync(session);

        // Queue background job to sync with ERP
        await _backgroundJobManager.EnqueueAsync<SyncStockAdjustmentsJob>(
            new SyncStockAdjustmentsArgs { SessionId = sessionId });

        _logger.LogInformation("Committed stock taking session {SessionId} with {Count} adjustments", 
            sessionId, result.TotalAdjustments);

        return ObjectMapper.Map<StockAdjustmentResult, StockAdjustmentResultDto>(result);
    }

    public async Task<List<MaterialForStockTakingDto>> GetMaterialsForStockTakingAsync(string? warehouseCode = null)
    {
        var materials = await _catalogRepository.GetMaterialsAsync(warehouseCode);
        
        var result = materials.Select(m => new MaterialForStockTakingDto
        {
            MaterialCode = m.Code,
            MaterialName = m.Name,
            CurrentStock = m.Stock.CurrentQuantity,
            Unit = m.Unit,
            LastStockTaking = GetLastStockTakingDate(m.Code),
            HasLots = m.IsLotTracked,
            IsExpirable = m.IsExpirable
        }).ToList();

        return result;
    }

    public async Task<List<LotInfoDto>> GetMaterialLotsAsync(string materialCode)
    {
        var lots = await _catalogRepository.GetMaterialLotsAsync(materialCode);
        
        return lots.Select(l => new LotInfoDto
        {
            LotNumber = l.LotNumber,
            Quantity = l.Quantity,
            ExpirationDate = l.ExpirationDate,
            ReceivedDate = l.ReceivedDate,
            IsExpired = l.IsExpired,
            IsNearExpiry = l.IsNearExpiry
        }).ToList();
    }

    public async Task<StockTakingReportDto> GenerateSessionReportAsync(Guid sessionId)
    {
        var session = await _sessionRepository.GetAsync(sessionId);
        
        var report = new StockTakingReportDto
        {
            SessionId = sessionId,
            SessionName = session.SessionName,
            SessionType = session.Type,
            Status = session.Status,
            StartDate = session.StartDate,
            CompletionDate = session.CompletionDate,
            Duration = session.Duration,
            ResponsibleUser = session.ResponsibleUserId,
            
            TotalMaterials = session.TotalRecords,
            CompletedCounts = session.CompletedRecords,
            PendingCounts = session.PendingRecords,
            CompletionPercentage = session.CompletionPercentage,
            
            TotalDiscrepancies = session.Discrepancies.Count,
            SignificantDiscrepancies = session.Discrepancies.Count(d => d.IsSignificant),
            
            Records = session.Records.Select(r => new StockTakingRecordSummaryDto
            {
                MaterialCode = r.MaterialCode,
                MaterialName = r.MaterialName,
                LotNumber = r.LotNumber,
                SystemQuantity = r.SystemQuantity,
                CountedQuantity = r.CountedQuantity,
                Variance = r.Variance,
                VariancePercentage = r.VariancePercentage,
                Status = r.Status,
                CountedBy = r.CountedBy,
                CountDate = r.CountDate
            }).ToList()
        };

        return report;
    }

    private DateTime? GetLastStockTakingDate(string materialCode)
    {
        // Implementation to get last stock taking date for material
        // This would query historical stock taking records
        return null; // Placeholder
    }
}
```

### Background Jobs

#### SyncStockAdjustmentsJob
```csharp
public class SyncStockAdjustmentsJob : IAsyncBackgroundJob<SyncStockAdjustmentsArgs>
{
    private readonly IStockTakingSessionRepository _sessionRepository;
    private readonly IFlexiManufactureRepository _flexiRepository;
    private readonly ILogger<SyncStockAdjustmentsJob> _logger;

    public SyncStockAdjustmentsJob(
        IStockTakingSessionRepository sessionRepository,
        IFlexiManufactureRepository flexiRepository,
        ILogger<SyncStockAdjustmentsJob> logger)
    {
        _sessionRepository = sessionRepository;
        _flexiRepository = flexiRepository;
        _logger = logger;
    }

    public async Task ExecuteAsync(SyncStockAdjustmentsArgs args)
    {
        try
        {
            var session = await _sessionRepository.GetAsync(args.SessionId);
            
            if (session.Status != StockTakingStatus.Committed)
            {
                _logger.LogWarning("Session {SessionId} is not in committed status", args.SessionId);
                return;
            }

            var adjustments = session.CommitToERP().Adjustments;
            
            foreach (var adjustment in adjustments)
            {
                await _flexiRepository.CreateStockAdjustmentAsync(adjustment);
            }

            _logger.LogInformation("Synchronized {Count} stock adjustments for session {SessionId}", 
                adjustments.Count, args.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync stock adjustments for session {SessionId}", args.SessionId);
            throw;
        }
    }
}

public class SyncStockAdjustmentsArgs
{
    public Guid SessionId { get; set; }
}
```

### Repository Interfaces

#### IStockTakingSessionRepository
```csharp
public interface IStockTakingSessionRepository : IRepository<StockTakingSession, Guid>
{
    Task<List<StockTakingSession>> GetSessionsByStatusAsync(StockTakingStatus status);
    Task<List<StockTakingSession>> GetSessionsByDateRangeAsync(DateTime fromDate, DateTime toDate);
    Task<StockTakingSession?> GetActiveSessionForWarehouseAsync(string warehouseCode);
    Task<List<StockTakingSession>> GetSessionsForMaterialAsync(string materialCode);
    Task<PagedResultDto<StockTakingSession>> GetPagedSessionsAsync(
        ISpecification<StockTakingSession> specification,
        int pageIndex,
        int pageSize,
        string sorting = null);
}
```

### Performance Requirements

#### Response Time Targets
- Session creation: < 3 seconds
- Count recording: < 1 second
- Session validation: < 5 seconds
- ERP synchronization: < 30 seconds

#### Scalability
- Support 1000+ materials per session
- Handle 10+ concurrent counting sessions
- Process 10,000+ count records efficiently
- Maintain audit trail for compliance

### Happy Day Scenarios

#### Scenario 1: Planned Stock Taking
```
1. Supervisor creates stock taking session for warehouse
2. System adds all materials requiring counting
3. Session is started and counting teams assigned
4. Counters record physical quantities with lot numbers
5. System identifies discrepancies automatically
6. Supervisor validates counts and resolves discrepancies
7. Session is committed and ERP is updated
```

#### Scenario 2: Lot Expiration Management
```
1. Counter scans material with lot tracking
2. System displays existing lots and expiration dates
3. Counter records quantities per lot
4. System flags expired or near-expiry lots
5. Quality manager reviews flagged items
6. Appropriate disposal or usage actions taken
```

#### Scenario 3: Dry Run Validation
```
1. Supervisor runs dry-run commit
2. System shows what adjustments would be made
3. Significant variances are reviewed
4. Additional counts performed if needed
5. Actual commit executed after validation
```

### Error Scenarios

#### Scenario 1: ERP Synchronization Failure
```
User: Commits stock taking session
System: ERP connection fails during sync
Action: Log error, queue for retry, notify administrators
```

#### Scenario 2: Significant Discrepancy
```
User: Records count with 20% variance
System: Shows warning "Significant discrepancy detected"
Action: Require recount or supervisor approval
```

#### Scenario 3: Expired Material Found
```
User: Records count for expired lot
System: Shows alert "Material expired on [date]"
Action: Flag for quality review, require disposal action
```