# User Story: Warehouse Stock Taking

## Feature Description
The Warehouse Stock Taking feature manages comprehensive warehouse inventory audits with location-based counting, multi-user coordination, and real-time discrepancy detection. The system supports both scheduled and ad-hoc stock takes, integrates with transport box tracking, and provides complete audit trails for warehouse inventory verification operations.

## Business Requirements

### Primary Use Cases
1. **Location-Based Stock Taking**: Organize inventory counts by warehouse locations and zones
2. **Multi-User Coordination**: Enable multiple counters to work simultaneously on different areas
3. **Real-Time Discrepancy Detection**: Identify and flag inventory variances immediately
4. **Transport Box Integration**: Include transport boxes in warehouse inventory counts
5. **Audit Trail Management**: Maintain complete history of all counting activities

### User Stories
- As a warehouse manager, I want to organize stock taking by location so I can efficiently audit warehouse inventory
- As an inventory counter, I want to scan and count items by location so I can accurately record stock levels
- As a warehouse supervisor, I want to monitor counting progress so I can ensure complete coverage
- As an auditor, I want to review discrepancies and adjustments so I can verify inventory accuracy

## Technical Requirements

### Domain Models

#### WarehouseStockTaking
```csharp
public class WarehouseStockTaking : AuditedAggregateRoot<Guid>
{
    public string StockTakingNumber { get; set; } = "";
    public string StockTakingName { get; set; } = "";
    public WarehouseStockTakingType Type { get; set; } = WarehouseStockTakingType.Full;
    public WarehouseStockTakingStatus Status { get; set; } = WarehouseStockTakingStatus.Planned;
    public DateTime PlannedDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? CompletionDate { get; set; }
    public string ResponsibleUserId { get; set; } = "";
    public string? WarehouseCode { get; set; }
    public bool IncludeTransportBoxes { get; set; }
    public string? Notes { get; set; }
    
    // Navigation Properties
    public virtual ICollection<WarehouseLocation> Locations { get; set; } = new List<WarehouseLocation>();
    public virtual ICollection<StockCountRecord> CountRecords { get; set; } = new List<StockCountRecord>();
    public virtual ICollection<StockDiscrepancy> Discrepancies { get; set; } = new List<StockDiscrepancy>();
    public virtual ICollection<CountingAssignment> Assignments { get; set; } = new List<CountingAssignment>();
    
    // Computed Properties
    public TimeSpan? Duration => CompletionDate - StartDate;
    public int TotalLocations => Locations.Count;
    public int CompletedLocations => Locations.Count(l => l.Status == LocationCountStatus.Completed);
    public int TotalItems => CountRecords.Count;
    public int CountedItems => CountRecords.Count(r => r.Status == CountRecordStatus.Counted);
    public decimal CompletionPercentage => TotalItems > 0 ? (CountedItems / (decimal)TotalItems) * 100 : 0;
    public bool HasSignificantDiscrepancies => Discrepancies.Any(d => d.IsSignificant);
    public bool CanStart => Status == WarehouseStockTakingStatus.Planned && Locations.Any();
    public bool CanComplete => Status == WarehouseStockTakingStatus.InProgress && CountedItems == TotalItems;
    
    // Business Methods
    public void AddLocation(string locationCode, string locationName, string? zone = null, WarehouseLocationPriority priority = WarehouseLocationPriority.Normal)
    {
        if (Status != WarehouseStockTakingStatus.Planned)
            throw new BusinessException("Cannot add locations to stock taking in progress");
            
        var location = new WarehouseLocation
        {
            Id = Guid.NewGuid(),
            StockTakingId = Id,
            LocationCode = locationCode,
            LocationName = locationName,
            Zone = zone,
            Priority = priority,
            Status = LocationCountStatus.Pending
        };
        
        Locations.Add(location);
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void StartStockTaking(string userId)
    {
        if (!CanStart)
            throw new BusinessException("Stock taking cannot be started");
            
        Status = WarehouseStockTakingStatus.InProgress;
        StartDate = DateTime.UtcNow;
        ResponsibleUserId = userId;
        
        // Initialize count records for all locations
        InitializeCountRecords();
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void AssignCounter(string locationCode, string counterId, string counterName)
    {
        if (Status != WarehouseStockTakingStatus.InProgress)
            throw new BusinessException("Can only assign counters to active stock taking");
            
        var location = Locations.FirstOrDefault(l => l.LocationCode == locationCode);
        if (location == null)
            throw new BusinessException($"Location {locationCode} not found");
            
        var existingAssignment = Assignments.FirstOrDefault(a => a.LocationCode == locationCode);
        if (existingAssignment != null)
        {
            existingAssignment.ReassignCounter(counterId, counterName);
        }
        else
        {
            var assignment = new CountingAssignment
            {
                Id = Guid.NewGuid(),
                StockTakingId = Id,
                LocationCode = locationCode,
                CounterId = counterId,
                CounterName = counterName,
                AssignedDate = DateTime.UtcNow,
                Status = AssignmentStatus.Assigned
            };
            
            Assignments.Add(assignment);
        }
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void RecordCount(string locationCode, string productCode, decimal countedQuantity, string counterId, string? lotNumber = null, string? notes = null)
    {
        if (Status != WarehouseStockTakingStatus.InProgress)
            throw new BusinessException("Can only record counts during active stock taking");
            
        var record = CountRecords.FirstOrDefault(r => 
            r.LocationCode == locationCode && 
            r.ProductCode == productCode && 
            r.LotNumber == lotNumber);
            
        if (record == null)
            throw new BusinessException($"Count record not found for product {productCode} at location {locationCode}");
            
        record.RecordCount(countedQuantity, counterId, notes);
        
        // Check for discrepancies
        CheckAndCreateDiscrepancy(record);
        
        // Update location status if all items counted
        UpdateLocationStatus(locationCode);
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CompleteLocationCount(string locationCode, string counterId)
    {
        var location = Locations.FirstOrDefault(l => l.LocationCode == locationCode);
        if (location == null)
            throw new BusinessException($"Location {locationCode} not found");
            
        var locationRecords = CountRecords.Where(r => r.LocationCode == locationCode);
        if (locationRecords.Any(r => r.Status != CountRecordStatus.Counted))
            throw new BusinessException($"Location {locationCode} has uncounted items");
            
        location.CompleteCount(counterId);
        
        // Update assignment status
        var assignment = Assignments.FirstOrDefault(a => a.LocationCode == locationCode);
        assignment?.CompleteAssignment();
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CompleteStockTaking()
    {
        if (!CanComplete)
            throw new BusinessException("Cannot complete stock taking with uncounted items");
            
        Status = WarehouseStockTakingStatus.Completed;
        CompletionDate = DateTime.UtcNow;
        
        // Complete all assignments
        foreach (var assignment in Assignments.Where(a => a.Status == AssignmentStatus.InProgress))
        {
            assignment.CompleteAssignment();
        }
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void ValidateStockTaking()
    {
        if (Status != WarehouseStockTakingStatus.Completed)
            throw new BusinessException("Can only validate completed stock taking");
            
        // Validate all records and discrepancies
        foreach (var record in CountRecords)
        {
            record.ValidateRecord();
        }
        
        Status = WarehouseStockTakingStatus.Validated;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public WarehouseStockTakingSummary GetSummary()
    {
        return new WarehouseStockTakingSummary
        {
            StockTakingId = Id,
            StockTakingNumber = StockTakingNumber,
            Status = Status,
            StartDate = StartDate,
            CompletionDate = CompletionDate,
            Duration = Duration,
            TotalLocations = TotalLocations,
            CompletedLocations = CompletedLocations,
            TotalItems = TotalItems,
            CountedItems = CountedItems,
            CompletionPercentage = CompletionPercentage,
            TotalDiscrepancies = Discrepancies.Count,
            SignificantDiscrepancies = Discrepancies.Count(d => d.IsSignificant),
            AssignedCounters = Assignments.Count,
            LocationsByZone = Locations.GroupBy(l => l.Zone ?? "Unknown")
                                     .ToDictionary(g => g.Key, g => g.Count())
        };
    }
    
    private void InitializeCountRecords()
    {
        foreach (var location in Locations)
        {
            // Get current inventory for location from catalog
            var locationInventory = GetLocationInventory(location.LocationCode);
            
            foreach (var item in locationInventory)
            {
                var record = new StockCountRecord
                {
                    Id = Guid.NewGuid(),
                    StockTakingId = Id,
                    LocationCode = location.LocationCode,
                    ProductCode = item.ProductCode,
                    ProductName = item.ProductName,
                    SystemQuantity = item.Quantity,
                    LotNumber = item.LotNumber,
                    ExpirationDate = item.ExpirationDate,
                    Status = CountRecordStatus.Pending
                };
                
                CountRecords.Add(record);
            }
        }
        
        // Include transport boxes if enabled
        if (IncludeTransportBoxes)
        {
            InitializeTransportBoxRecords();
        }
    }
    
    private void InitializeTransportBoxRecords()
    {
        var transportBoxes = GetWarehouseTransportBoxes(WarehouseCode);
        
        foreach (var box in transportBoxes)
        {
            foreach (var item in box.Items)
            {
                var record = new StockCountRecord
                {
                    Id = Guid.NewGuid(),
                    StockTakingId = Id,
                    LocationCode = $"BOX-{box.Code}",
                    ProductCode = item.ProductCode,
                    ProductName = item.ProductName,
                    SystemQuantity = item.Quantity,
                    LotNumber = item.LotNumber,
                    Status = CountRecordStatus.Pending,
                    IsTransportBoxItem = true,
                    TransportBoxId = box.Id
                };
                
                CountRecords.Add(record);
            }
        }
    }
    
    private void CheckAndCreateDiscrepancy(StockCountRecord record)
    {
        if (!record.HasVariance) return;
        
        var existingDiscrepancy = Discrepancies.FirstOrDefault(d => 
            d.LocationCode == record.LocationCode && 
            d.ProductCode == record.ProductCode && 
            d.LotNumber == record.LotNumber);
            
        if (existingDiscrepancy != null)
        {
            existingDiscrepancy.UpdateDiscrepancy(record.Variance, record.VariancePercentage);
        }
        else
        {
            var discrepancy = new StockDiscrepancy
            {
                Id = Guid.NewGuid(),
                StockTakingId = Id,
                LocationCode = record.LocationCode,
                ProductCode = record.ProductCode,
                ProductName = record.ProductName,
                LotNumber = record.LotNumber,
                SystemQuantity = record.SystemQuantity,
                CountedQuantity = record.CountedQuantity ?? 0,
                Variance = record.Variance,
                VariancePercentage = record.VariancePercentage,
                IsSignificant = Math.Abs(record.VariancePercentage) > 5, // 5% threshold
                Status = DiscrepancyStatus.Identified,
                IdentifiedDate = DateTime.UtcNow
            };
            
            Discrepancies.Add(discrepancy);
        }
    }
    
    private void UpdateLocationStatus(string locationCode)
    {
        var location = Locations.FirstOrDefault(l => l.LocationCode == locationCode);
        if (location == null) return;
        
        var locationRecords = CountRecords.Where(r => r.LocationCode == locationCode);
        if (locationRecords.All(r => r.Status == CountRecordStatus.Counted))
        {
            location.Status = LocationCountStatus.CountingComplete;
        }
        else if (locationRecords.Any(r => r.Status == CountRecordStatus.Counted))
        {
            location.Status = LocationCountStatus.InProgress;
        }
    }
    
    private List<LocationInventoryItem> GetLocationInventory(string locationCode)
    {
        // Implementation to get current inventory for location
        return new List<LocationInventoryItem>(); // Placeholder
    }
    
    private List<TransportBoxInfo> GetWarehouseTransportBoxes(string? warehouseCode)
    {
        // Implementation to get transport boxes in warehouse
        return new List<TransportBoxInfo>(); // Placeholder
    }
}

public enum WarehouseStockTakingType
{
    Full,
    Partial,
    Cycle,
    Spot,
    TransportBoxOnly
}

public enum WarehouseStockTakingStatus
{
    Planned,
    InProgress,
    Completed,
    Validated,
    Committed,
    Cancelled
}
```

#### WarehouseLocation
```csharp
public class WarehouseLocation : AuditedEntity<Guid>
{
    public Guid StockTakingId { get; set; }
    public string LocationCode { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string? Zone { get; set; }
    public WarehouseLocationPriority Priority { get; set; } = WarehouseLocationPriority.Normal;
    public LocationCountStatus Status { get; set; } = LocationCountStatus.Pending;
    public DateTime? StartTime { get; set; }
    public DateTime? CompletionTime { get; set; }
    public string? CountedBy { get; set; }
    public string? Notes { get; set; }
    
    // Navigation Properties
    public virtual WarehouseStockTaking StockTaking { get; set; } = null!;
    
    // Computed Properties
    public TimeSpan? CountingDuration => CompletionTime - StartTime;
    public bool IsCompleted => Status == LocationCountStatus.Completed;
    public bool IsInProgress => Status == LocationCountStatus.InProgress;
    
    // Business Methods
    public void StartCount(string counterId)
    {
        if (Status != LocationCountStatus.Pending)
            throw new BusinessException("Location counting already started");
            
        Status = LocationCountStatus.InProgress;
        StartTime = DateTime.UtcNow;
        CountedBy = counterId;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CompleteCount(string counterId)
    {
        if (Status != LocationCountStatus.CountingComplete)
            throw new BusinessException("Location counting not ready for completion");
            
        Status = LocationCountStatus.Completed;
        CompletionTime = DateTime.UtcNow;
        CountedBy = counterId;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void ResetCount()
    {
        Status = LocationCountStatus.Pending;
        StartTime = null;
        CompletionTime = null;
        CountedBy = null;
        LastModificationTime = DateTime.UtcNow;
    }
}

public enum WarehouseLocationPriority
{
    Low,
    Normal,
    High,
    Critical
}

public enum LocationCountStatus
{
    Pending,
    InProgress,
    CountingComplete,
    Completed,
    Skipped
}
```

#### StockCountRecord
```csharp
public class StockCountRecord : AuditedEntity<Guid>
{
    public Guid StockTakingId { get; set; }
    public string LocationCode { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal SystemQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public CountRecordStatus Status { get; set; } = CountRecordStatus.Pending;
    public DateTime? CountingTime { get; set; }
    public string? CountedBy { get; set; }
    public string? Notes { get; set; }
    public bool IsTransportBoxItem { get; set; }
    public Guid? TransportBoxId { get; set; }
    public string? ValidationMessage { get; set; }
    
    // Navigation Properties
    public virtual WarehouseStockTaking StockTaking { get; set; } = null!;
    
    // Computed Properties
    public decimal Variance => (CountedQuantity ?? 0) - SystemQuantity;
    public decimal VariancePercentage => SystemQuantity > 0 ? (Variance / SystemQuantity) * 100 : 0;
    public bool HasVariance => Math.Abs(Variance) > 0.001m;
    public bool IsSignificantVariance => Math.Abs(VariancePercentage) > 5;
    public bool IsExpired => ExpirationDate.HasValue && ExpirationDate.Value < DateTime.Today;
    public bool IsNearExpiry => ExpirationDate.HasValue && ExpirationDate.Value <= DateTime.Today.AddDays(30);
    public bool IsCounted => CountedQuantity.HasValue;
    public string LocationDisplay => IsTransportBoxItem ? $"Transport Box: {LocationCode}" : LocationCode;
    
    // Business Methods
    public void RecordCount(decimal quantity, string counterId, string? notes = null)
    {
        if (Status == CountRecordStatus.Counted)
            throw new BusinessException("Record already counted");
            
        CountedQuantity = quantity;
        CountedBy = counterId;
        CountingTime = DateTime.UtcNow;
        Notes = notes;
        Status = CountRecordStatus.Counted;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void ValidateRecord()
    {
        var issues = new List<string>();
        
        if (!IsCounted)
            issues.Add("No count recorded");
            
        if (IsExpired)
            issues.Add("Product is expired");
            
        if (IsSignificantVariance)
            issues.Add($"Significant variance: {VariancePercentage:F1}%");
            
        if (CountedQuantity < 0)
            issues.Add("Negative quantity not allowed");
            
        ValidationMessage = issues.Any() ? string.Join("; ", issues) : null;
    }
    
    public void ResetCount()
    {
        CountedQuantity = null;
        CountedBy = null;
        CountingTime = null;
        Notes = null;
        Status = CountRecordStatus.Pending;
        ValidationMessage = null;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void SkipCount(string reason)
    {
        Status = CountRecordStatus.Skipped;
        Notes = $"Skipped: {reason}";
        LastModificationTime = DateTime.UtcNow;
    }
}

public enum CountRecordStatus
{
    Pending,
    Counted,
    Skipped,
    Validated,
    Rejected
}
```

#### CountingAssignment
```csharp
public class CountingAssignment : AuditedEntity<Guid>
{
    public Guid StockTakingId { get; set; }
    public string LocationCode { get; set; } = "";
    public string CounterId { get; set; } = "";
    public string CounterName { get; set; } = "";
    public DateTime AssignedDate { get; set; } = DateTime.UtcNow;
    public DateTime? StartTime { get; set; }
    public DateTime? CompletionTime { get; set; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Assigned;
    public string? Notes { get; set; }
    
    // Navigation Properties
    public virtual WarehouseStockTaking StockTaking { get; set; } = null!;
    
    // Computed Properties
    public TimeSpan? WorkingTime => CompletionTime - StartTime;
    public bool IsActive => Status == AssignmentStatus.InProgress;
    public bool IsCompleted => Status == AssignmentStatus.Completed;
    
    // Business Methods
    public void StartWork()
    {
        if (Status != AssignmentStatus.Assigned)
            throw new BusinessException("Assignment not ready to start");
            
        Status = AssignmentStatus.InProgress;
        StartTime = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CompleteAssignment()
    {
        Status = AssignmentStatus.Completed;
        CompletionTime = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void ReassignCounter(string newCounterId, string newCounterName)
    {
        CounterId = newCounterId;
        CounterName = newCounterName;
        AssignedDate = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
    }
}

public enum AssignmentStatus
{
    Assigned,
    InProgress,
    Completed,
    Cancelled
}
```

#### StockDiscrepancy
```csharp
public class StockDiscrepancy : AuditedEntity<Guid>
{
    public Guid StockTakingId { get; set; }
    public string LocationCode { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string? LotNumber { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
    public decimal Variance { get; set; }
    public decimal VariancePercentage { get; set; }
    public bool IsSignificant { get; set; }
    public DiscrepancyStatus Status { get; set; } = DiscrepancyStatus.Identified;
    public DateTime IdentifiedDate { get; set; } = DateTime.UtcNow;
    public string? Resolution { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedDate { get; set; }
    public DiscrepancyCategory Category { get; set; } = DiscrepancyCategory.Unknown;
    
    // Navigation Properties
    public virtual WarehouseStockTaking StockTaking { get; set; } = null!;
    
    // Computed Properties
    public bool IsResolved => Status == DiscrepancyStatus.Resolved;
    public bool IsIncrease => Variance > 0;
    public bool IsDecrease => Variance < 0;
    public string VarianceDirection => IsIncrease ? "Overage" : "Shortage";
    public decimal AbsoluteVariance => Math.Abs(Variance);
    
    // Business Methods
    public void CategorizeDiscrepancy(DiscrepancyCategory category)
    {
        Category = category;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void ResolveDiscrepancy(string resolution, string resolvedBy, DiscrepancyCategory category = DiscrepancyCategory.Unknown)
    {
        Resolution = resolution;
        ResolvedBy = resolvedBy;
        ResolvedDate = DateTime.UtcNow;
        Status = DiscrepancyStatus.Resolved;
        
        if (category != DiscrepancyCategory.Unknown)
            Category = category;
            
        LastModificationTime = DateTime.UtcNow;
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

public enum DiscrepancyCategory
{
    Unknown,
    CountingError,
    SystemError,
    Shrinkage,
    Damage,
    Theft,
    ReceivingError,
    ShippingError
}
```

#### WarehouseStockTakingSummary (Value Object)
```csharp
public class WarehouseStockTakingSummary : ValueObject
{
    public Guid StockTakingId { get; set; }
    public string StockTakingNumber { get; set; } = "";
    public WarehouseStockTakingStatus Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? CompletionDate { get; set; }
    public TimeSpan? Duration { get; set; }
    public int TotalLocations { get; set; }
    public int CompletedLocations { get; set; }
    public int TotalItems { get; set; }
    public int CountedItems { get; set; }
    public decimal CompletionPercentage { get; set; }
    public int TotalDiscrepancies { get; set; }
    public int SignificantDiscrepancies { get; set; }
    public int AssignedCounters { get; set; }
    public Dictionary<string, int> LocationsByZone { get; set; } = new();
    
    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return StockTakingId;
        yield return Status;
        yield return StartDate ?? DateTime.MinValue;
        yield return CompletionDate ?? DateTime.MinValue;
        yield return TotalLocations;
        yield return CompletedLocations;
        yield return TotalItems;
        yield return CountedItems;
    }
    
    public bool IsCompleted => Status == WarehouseStockTakingStatus.Completed;
    public bool HasIssues => SignificantDiscrepancies > 0;
    public double LocationCompletionPercentage => TotalLocations > 0 ? (CompletedLocations / (double)TotalLocations) * 100 : 0;
    public double DiscrepancyRate => TotalItems > 0 ? (TotalDiscrepancies / (double)TotalItems) * 100 : 0;
    public string OverallHealth => HasIssues ? "Issues Detected" : IsCompleted ? "Healthy" : "In Progress";
}
```

### Application Services

#### IWarehouseStockTakingAppService
```csharp
public interface IWarehouseStockTakingAppService : IApplicationService
{
    Task<WarehouseStockTakingDto> CreateStockTakingAsync(CreateWarehouseStockTakingDto input);
    Task<WarehouseStockTakingDto> GetStockTakingAsync(Guid stockTakingId);
    Task<PagedResultDto<WarehouseStockTakingDto>> GetStockTakingsAsync(GetWarehouseStockTakingsQuery query);
    Task<WarehouseStockTakingDto> AddLocationAsync(Guid stockTakingId, AddLocationDto input);
    Task<WarehouseStockTakingDto> StartStockTakingAsync(Guid stockTakingId);
    
    Task<WarehouseStockTakingDto> AssignCounterAsync(Guid stockTakingId, AssignCounterDto input);
    Task<WarehouseStockTakingDto> RecordCountAsync(Guid stockTakingId, RecordCountDto input);
    Task<WarehouseStockTakingDto> CompleteLocationAsync(Guid stockTakingId, CompleteLocationDto input);
    Task<WarehouseStockTakingDto> CompleteStockTakingAsync(Guid stockTakingId);
    Task<WarehouseStockTakingDto> ValidateStockTakingAsync(Guid stockTakingId);
    
    Task<List<StockDiscrepancyDto>> GetDiscrepanciesAsync(Guid stockTakingId);
    Task ResolveDiscrepancyAsync(Guid discrepancyId, ResolveDiscrepancyDto input);
    Task<List<CountingAssignmentDto>> GetAssignmentsAsync(Guid stockTakingId);
    
    Task<List<WarehouseLocationDto>> GetWarehouseLocationsAsync(string? warehouseCode = null, string? zone = null);
    Task<List<LocationInventoryDto>> GetLocationInventoryAsync(string locationCode);
    Task<List<TransportBoxDto>> GetWarehouseTransportBoxesAsync(string? warehouseCode = null);
    
    Task<WarehouseStockTakingSummaryDto> GetSummaryAsync(Guid stockTakingId);
    Task<StockTakingProgressReportDto> GetProgressReportAsync(Guid stockTakingId);
    Task<List<CounterPerformanceDto>> GetCounterPerformanceAsync(DateTime fromDate, DateTime toDate);
    Task<WarehouseStockAccuracyReportDto> GetAccuracyReportAsync(string? warehouseCode, DateTime fromDate, DateTime toDate);
}
```

#### WarehouseStockTakingAppService Implementation
```csharp
[Authorize]
public class WarehouseStockTakingAppService : ApplicationService, IWarehouseStockTakingAppService
{
    private readonly IWarehouseStockTakingRepository _stockTakingRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly ILogger<WarehouseStockTakingAppService> _logger;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public WarehouseStockTakingAppService(
        IWarehouseStockTakingRepository stockTakingRepository,
        ICatalogRepository catalogRepository,
        ITransportBoxRepository transportBoxRepository,
        ILogger<WarehouseStockTakingAppService> logger,
        IBackgroundJobManager backgroundJobManager)
    {
        _stockTakingRepository = stockTakingRepository;
        _catalogRepository = catalogRepository;
        _transportBoxRepository = transportBoxRepository;
        _logger = logger;
        _backgroundJobManager = backgroundJobManager;
    }

    public async Task<WarehouseStockTakingDto> CreateStockTakingAsync(CreateWarehouseStockTakingDto input)
    {
        var stockTaking = new WarehouseStockTaking
        {
            Id = Guid.NewGuid(),
            StockTakingNumber = await GenerateStockTakingNumberAsync(),
            StockTakingName = input.StockTakingName,
            Type = input.Type,
            PlannedDate = input.PlannedDate,
            WarehouseCode = input.WarehouseCode,
            IncludeTransportBoxes = input.IncludeTransportBoxes,
            ResponsibleUserId = CurrentUser.Id?.ToString() ?? ""
        };

        // Add locations to stock taking
        if (input.LocationCodes?.Any() == true)
        {
            foreach (var locationCode in input.LocationCodes)
            {
                var locationInfo = await _catalogRepository.GetLocationInfoAsync(locationCode);
                stockTaking.AddLocation(locationCode, locationInfo.Name, locationInfo.Zone, locationInfo.Priority);
            }
        }
        else
        {
            // Add all locations in warehouse
            var warehouseLocations = await GetWarehouseLocationsAsync(input.WarehouseCode);
            foreach (var location in warehouseLocations)
            {
                stockTaking.AddLocation(location.LocationCode, location.LocationName, location.Zone, location.Priority);
            }
        }

        await _stockTakingRepository.InsertAsync(stockTaking);
        
        _logger.LogInformation("Created warehouse stock taking {StockTakingNumber} with {LocationCount} locations", 
            stockTaking.StockTakingNumber, stockTaking.TotalLocations);

        return ObjectMapper.Map<WarehouseStockTaking, WarehouseStockTakingDto>(stockTaking);
    }

    public async Task<WarehouseStockTakingDto> StartStockTakingAsync(Guid stockTakingId)
    {
        var stockTaking = await _stockTakingRepository.GetAsync(stockTakingId);
        
        stockTaking.StartStockTaking(CurrentUser.Id?.ToString() ?? "");
        await _stockTakingRepository.UpdateAsync(stockTaking);
        
        // Queue background job for notifications
        await _backgroundJobManager.EnqueueAsync<NotifyStockTakingStartJob>(
            new NotifyStockTakingStartArgs { StockTakingId = stockTakingId });
        
        _logger.LogInformation("Started warehouse stock taking {StockTakingId} with {ItemCount} items", 
            stockTakingId, stockTaking.TotalItems);

        return ObjectMapper.Map<WarehouseStockTaking, WarehouseStockTakingDto>(stockTaking);
    }

    public async Task<WarehouseStockTakingDto> RecordCountAsync(Guid stockTakingId, RecordCountDto input)
    {
        var stockTaking = await _stockTakingRepository.GetAsync(stockTakingId);
        
        stockTaking.RecordCount(
            input.LocationCode,
            input.ProductCode,
            input.CountedQuantity,
            CurrentUser.UserName ?? "",
            input.LotNumber,
            input.Notes);

        await _stockTakingRepository.UpdateAsync(stockTaking);

        _logger.LogInformation("Recorded count for product {ProductCode} at location {LocationCode} in stock taking {StockTakingId}: {Quantity}", 
            input.ProductCode, input.LocationCode, stockTakingId, input.CountedQuantity);

        return ObjectMapper.Map<WarehouseStockTaking, WarehouseStockTakingDto>(stockTaking);
    }

    public async Task<WarehouseStockTakingDto> CompleteLocationAsync(Guid stockTakingId, CompleteLocationDto input)
    {
        var stockTaking = await _stockTakingRepository.GetAsync(stockTakingId);
        
        stockTaking.CompleteLocationCount(input.LocationCode, CurrentUser.UserName ?? "");
        await _stockTakingRepository.UpdateAsync(stockTaking);

        _logger.LogInformation("Completed location {LocationCode} counting in stock taking {StockTakingId}", 
            input.LocationCode, stockTakingId);

        return ObjectMapper.Map<WarehouseStockTaking, WarehouseStockTakingDto>(stockTaking);
    }

    public async Task<WarehouseStockTakingDto> CompleteStockTakingAsync(Guid stockTakingId)
    {
        var stockTaking = await _stockTakingRepository.GetAsync(stockTakingId);
        
        stockTaking.CompleteStockTaking();
        await _stockTakingRepository.UpdateAsync(stockTaking);

        // Queue background job for analysis
        await _backgroundJobManager.EnqueueAsync<AnalyzeStockTakingResultsJob>(
            new AnalyzeStockTakingResultsArgs { StockTakingId = stockTakingId });

        _logger.LogInformation("Completed warehouse stock taking {StockTakingId}", stockTakingId);

        return ObjectMapper.Map<WarehouseStockTaking, WarehouseStockTakingDto>(stockTaking);
    }

    public async Task<List<WarehouseLocationDto>> GetWarehouseLocationsAsync(string? warehouseCode = null, string? zone = null)
    {
        var locations = await _catalogRepository.GetWarehouseLocationsAsync(warehouseCode, zone);
        
        return locations.Select(l => new WarehouseLocationDto
        {
            LocationCode = l.LocationCode,
            LocationName = l.LocationName,
            Zone = l.Zone,
            Priority = l.Priority,
            ItemCount = l.ItemCount,
            IsActive = l.IsActive
        }).ToList();
    }

    public async Task<List<LocationInventoryDto>> GetLocationInventoryAsync(string locationCode)
    {
        var inventory = await _catalogRepository.GetLocationInventoryAsync(locationCode);
        
        return inventory.Select(i => new LocationInventoryDto
        {
            ProductCode = i.ProductCode,
            ProductName = i.ProductName,
            Quantity = i.Quantity,
            Unit = i.Unit,
            LotNumber = i.LotNumber,
            ExpirationDate = i.ExpirationDate,
            LastUpdated = i.LastUpdated
        }).ToList();
    }

    public async Task<List<TransportBoxDto>> GetWarehouseTransportBoxesAsync(string? warehouseCode = null)
    {
        var transportBoxes = await _transportBoxRepository.GetActiveBoxesByWarehouseAsync(warehouseCode);
        
        return ObjectMapper.Map<List<TransportBox>, List<TransportBoxDto>>(transportBoxes);
    }

    public async Task<WarehouseStockTakingSummaryDto> GetSummaryAsync(Guid stockTakingId)
    {
        var stockTaking = await _stockTakingRepository.GetAsync(stockTakingId);
        var summary = stockTaking.GetSummary();
        
        return ObjectMapper.Map<WarehouseStockTakingSummary, WarehouseStockTakingSummaryDto>(summary);
    }

    public async Task<StockTakingProgressReportDto> GetProgressReportAsync(Guid stockTakingId)
    {
        var stockTaking = await _stockTakingRepository.GetAsync(stockTakingId);
        
        var report = new StockTakingProgressReportDto
        {
            StockTakingId = stockTakingId,
            StockTakingNumber = stockTaking.StockTakingNumber,
            Status = stockTaking.Status,
            StartDate = stockTaking.StartDate,
            OverallProgress = stockTaking.CompletionPercentage,
            LocationProgress = stockTaking.Locations.Select(l => new LocationProgressDto
            {
                LocationCode = l.LocationCode,
                LocationName = l.LocationName,
                Zone = l.Zone,
                Status = l.Status,
                AssignedCounter = stockTaking.Assignments.FirstOrDefault(a => a.LocationCode == l.LocationCode)?.CounterName,
                ItemCount = stockTaking.CountRecords.Count(r => r.LocationCode == l.LocationCode),
                CountedItems = stockTaking.CountRecords.Count(r => r.LocationCode == l.LocationCode && r.Status == CountRecordStatus.Counted),
                StartTime = l.StartTime,
                CompletionTime = l.CompletionTime
            }).ToList(),
            DiscrepancySummary = new DiscrepancySummaryDto
            {
                TotalDiscrepancies = stockTaking.Discrepancies.Count,
                SignificantDiscrepancies = stockTaking.Discrepancies.Count(d => d.IsSignificant),
                OverageDiscrepancies = stockTaking.Discrepancies.Count(d => d.IsIncrease),
                ShortageDiscrepancies = stockTaking.Discrepancies.Count(d => d.IsDecrease),
                UnresolvedDiscrepancies = stockTaking.Discrepancies.Count(d => !d.IsResolved)
            }
        };
        
        return report;
    }

    private async Task<string> GenerateStockTakingNumberAsync()
    {
        var today = DateTime.Today;
        var dailyCount = await _stockTakingRepository.GetDailyStockTakingCountAsync(today);
        return $"WST{today:yyyyMMdd}{(dailyCount + 1):D3}";
    }
}
```

### Repository Interfaces

#### IWarehouseStockTakingRepository
```csharp
public interface IWarehouseStockTakingRepository : IRepository<WarehouseStockTaking, Guid>
{
    Task<List<WarehouseStockTaking>> GetByStatusAsync(WarehouseStockTakingStatus status);
    Task<List<WarehouseStockTaking>> GetByWarehouseAsync(string warehouseCode);
    Task<List<WarehouseStockTaking>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate);
    Task<int> GetDailyStockTakingCountAsync(DateTime date);
    Task<List<WarehouseStockTaking>> GetActiveStockTakingsAsync();
    Task<PagedResultDto<WarehouseStockTaking>> GetPagedStockTakingsAsync(
        ISpecification<WarehouseStockTaking> specification,
        int pageIndex,
        int pageSize,
        string sorting = null);
}
```

### Performance Requirements

#### Response Time Targets
- Stock taking creation: < 5 seconds
- Count recording: < 1 second
- Progress reports: < 3 seconds
- Completion operations: < 10 seconds

#### Scalability
- Support 100+ concurrent counters
- Handle 10,000+ count records per stock taking
- Process multiple warehouse stock takings simultaneously
- Maintain real-time progress tracking

### Happy Day Scenarios

#### Scenario 1: Full Warehouse Stock Taking
```
1. Warehouse manager creates comprehensive stock taking for entire warehouse
2. System generates count records for all locations and products
3. Multiple counters are assigned to different warehouse zones
4. Counters scan and record quantities using mobile devices
5. System identifies discrepancies in real-time
6. Supervisor reviews and resolves significant variances
7. Stock taking is completed and validated
8. Inventory adjustments are applied to system
```

#### Scenario 2: Zone-Based Counting
```
1. Manager organizes stock taking by warehouse zones
2. Different teams assigned to high-volume and sensitive areas
3. Priority locations (high-value items) counted first
4. Progress monitored in real-time dashboard
5. Zone completions trigger next phase assignments
6. Transport boxes included in final reconciliation
7. Complete audit trail maintained for compliance
```

#### Scenario 3: Cycle Count with Transport Box Integration
```
1. Periodic cycle count includes active transport boxes
2. System tracks both stationary and mobile inventory
3. Counters verify transport box contents against records
4. Items in transit are properly accounted for
5. Location transfers updated based on findings
6. Inventory accuracy improved through regular counting
```

### Error Scenarios

#### Scenario 1: Missing Location Inventory
```
User: Attempts to count location with no system inventory
System: Shows warning "No system inventory found for location"
Action: Allow manual entry, flag for investigation
```

#### Scenario 2: Transport Box State Conflict
```
User: Tries to count transport box that's in transit
System: Shows error "Transport box currently in transit"
Action: Skip box or wait for arrival, update status
```

#### Scenario 3: Significant Count Variance
```
User: Records count with 25% variance from system
System: Shows alert "Significant variance detected - requires supervisor approval"
Action: Prompt for recount, require supervisor verification
```