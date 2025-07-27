# User Story: Transport Box State Management

## Feature Description
The Transport Box State Management feature implements a sophisticated finite state machine for tracking shipping containers through their complete lifecycle from creation to closure. It manages transport boxes with state transitions, item tracking, user audit trails, and automated workflow enforcement with carrier integration and inventory synchronization.

## Business Requirements

### Primary Use Cases
1. **Transport Box Lifecycle Management**: Track boxes through complete shipping lifecycle
2. **State Machine Enforcement**: Ensure proper state transitions with business rule validation
3. **Item Tracking**: Manage products within transport boxes with quantities and states
4. **Audit Trail Maintenance**: Record all state changes with user and timestamp tracking
5. **Carrier Integration**: Support multiple shipping providers with tracking numbers

### User Stories
- As a warehouse manager, I want to track transport boxes through their lifecycle so I can monitor inventory movement
- As a shipping coordinator, I want to manage box states so I can ensure proper shipping workflow
- As an inventory clerk, I want to add items to boxes so I can prepare shipments efficiently
- As an auditor, I want to review box state history so I can verify compliance and investigate issues

## Technical Requirements

### Domain Models

#### TransportBox
```csharp
public class TransportBox : AuditedAggregateRoot<Guid>
{
    public string Code { get; set; } = "";
    public TransportBoxState State { get; set; } = TransportBoxState.New;
    public string? DeliveryType { get; set; }
    public string? TrackingNumber { get; set; }
    public decimal Weight { get; set; }
    public string? Location { get; set; }
    public string? UserId { get; set; }
    public string? OpenUserId { get; set; }
    public string? ReceivedUserId { get; set; }
    
    // Temporal Tracking
    public DateTime? OpenedDate { get; set; }
    public DateTime? TransitDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public DateTime? ClosedDate { get; set; }
    
    // Navigation Properties
    public virtual ICollection<TransportBoxItem> Items { get; set; } = new List<TransportBoxItem>();
    public virtual ICollection<TransportBoxStateLog> StateHistory { get; set; } = new List<TransportBoxStateLog>();
    
    // Computed Properties
    public int ItemCount => Items.Count;
    public decimal TotalItemValue => Items.Sum(i => i.Value);
    public bool HasItems => Items.Any();
    public bool IsActive => State != TransportBoxState.Closed && State != TransportBoxState.Error;
    public bool CanAddItems => State == TransportBoxState.Opened;
    public bool CanTransition => State != TransportBoxState.Error;
    public TimeSpan? TransitDuration => ReceivedDate - TransitDate;
    public TimeSpan? TotalProcessingTime => ClosedDate - CreationTime;
    
    // Business Methods
    public void OpenBox(string userId)
    {
        if (!CanTransitionTo(TransportBoxState.Opened))
            throw new BusinessException($"Cannot open box in {State} state");
            
        // Auto-close any existing box with same code
        CloseConflictingBox();
        
        var previousState = State;
        State = TransportBoxState.Opened;
        OpenUserId = userId;
        OpenedDate = DateTime.UtcNow;
        
        LogStateChange(previousState, State, userId, "Box opened for item management");
    }
    
    public void AddItem(string productCode, string productName, int quantity, string userId, TransportBoxItemType itemType = TransportBoxItemType.Product)
    {
        if (!CanAddItems)
            throw new BusinessException($"Cannot add items to box in {State} state");
            
        var existingItem = Items.FirstOrDefault(i => i.ProductCode == productCode && i.ItemType == itemType);
        
        if (existingItem != null)
        {
            existingItem.UpdateQuantity(existingItem.Quantity + quantity, userId);
        }
        else
        {
            var item = new TransportBoxItem
            {
                Id = Guid.NewGuid(),
                TransportBoxId = Id,
                ProductCode = productCode,
                ProductName = productName,
                Quantity = quantity,
                ItemType = itemType,
                State = TransportBoxItemState.Added,
                UserId = userId
            };
            
            Items.Add(item);
        }
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void RemoveItem(Guid itemId, string userId)
    {
        if (!CanAddItems)
            throw new BusinessException($"Cannot remove items from box in {State} state");
            
        var item = Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null)
            throw new EntityNotFoundException($"Item {itemId} not found in box");
            
        Items.Remove(item);
        LastModificationTime = DateTime.UtcNow;
        
        LogStateChange(State, State, userId, $"Removed item {item.ProductCode}");
    }
    
    public void TransitBox(string deliveryType, string trackingNumber, string userId)
    {
        if (!CanTransitionTo(TransportBoxState.InTransit))
            throw new BusinessException($"Cannot transit box in {State} state");
            
        if (!HasItems)
            throw new BusinessException("Cannot transit empty box");
            
        var previousState = State;
        State = TransportBoxState.InTransit;
        DeliveryType = deliveryType;
        TrackingNumber = trackingNumber;
        TransitDate = DateTime.UtcNow;
        
        // Update all items to in-transit state
        foreach (var item in Items)
        {
            item.SetState(TransportBoxItemState.InTransit, userId);
        }
        
        LogStateChange(previousState, State, userId, $"Box sent for delivery via {deliveryType}");
    }
    
    public void ReceiveBox(decimal weight, string userId)
    {
        if (!CanTransitionTo(TransportBoxState.Received))
            throw new BusinessException($"Cannot receive box in {State} state");
            
        var previousState = State;
        State = TransportBoxState.Received;
        Weight = weight;
        ReceivedUserId = userId;
        ReceivedDate = DateTime.UtcNow;
        
        // Update all items to received state
        foreach (var item in Items)
        {
            item.SetState(TransportBoxItemState.Received, userId);
        }
        
        LogStateChange(previousState, State, userId, $"Box received with weight {weight}kg");
    }
    
    public void StockUpBox(string userId)
    {
        if (!CanTransitionTo(TransportBoxState.Stocked))
            throw new BusinessException($"Cannot stock box in {State} state");
            
        var previousState = State;
        State = TransportBoxState.Stocked;
        
        // Update all items to stocked state
        foreach (var item in Items)
        {
            item.SetState(TransportBoxItemState.Stocked, userId);
            // Here would integrate with catalog to update stock levels
        }
        
        LogStateChange(previousState, State, userId, "Box items moved to stock");
    }
    
    public void SwapIn(string userId)
    {
        if (!CanTransitionTo(TransportBoxState.InSwap))
            throw new BusinessException($"Cannot swap box in {State} state");
            
        var previousState = State;
        State = TransportBoxState.InSwap;
        
        LogStateChange(previousState, State, userId, "Box items being reconciled");
    }
    
    public void ReserveBox(string reservationReference, string userId)
    {
        if (!CanTransitionTo(TransportBoxState.Reserve))
            throw new BusinessException($"Cannot reserve box in {State} state");
            
        var previousState = State;
        State = TransportBoxState.Reserve;
        
        // Update all items to reserved state
        foreach (var item in Items)
        {
            item.SetState(TransportBoxItemState.Reserved, userId);
            item.Notes = $"Reserved for: {reservationReference}";
        }
        
        LogStateChange(previousState, State, userId, $"Box reserved for {reservationReference}");
    }
    
    public void CloseBox(string userId, string? reason = null)
    {
        if (State == TransportBoxState.Closed)
            return; // Already closed
            
        var previousState = State;
        State = TransportBoxState.Closed;
        ClosedDate = DateTime.UtcNow;
        
        // Final state for all items
        foreach (var item in Items)
        {
            item.SetState(TransportBoxItemState.Processed, userId);
        }
        
        LogStateChange(previousState, State, userId, reason ?? "Box lifecycle completed");
    }
    
    public void SetError(string errorMessage, string userId)
    {
        var previousState = State;
        State = TransportBoxState.Error;
        
        LogStateChange(previousState, State, userId, $"Error: {errorMessage}");
    }
    
    public bool CanTransitionTo(TransportBoxState targetState)
    {
        return GetValidTransitions().Contains(targetState);
    }
    
    public List<TransportBoxState> GetValidTransitions()
    {
        return State switch
        {
            TransportBoxState.New => new List<TransportBoxState> { TransportBoxState.Opened, TransportBoxState.Error },
            TransportBoxState.Opened => new List<TransportBoxState> { TransportBoxState.InTransit, TransportBoxState.Closed, TransportBoxState.Error },
            TransportBoxState.InTransit => new List<TransportBoxState> { TransportBoxState.Received, TransportBoxState.Error },
            TransportBoxState.Received => new List<TransportBoxState> { TransportBoxState.InSwap, TransportBoxState.Stocked, TransportBoxState.Reserve, TransportBoxState.Error },
            TransportBoxState.InSwap => new List<TransportBoxState> { TransportBoxState.Stocked, TransportBoxState.Error },
            TransportBoxState.Stocked => new List<TransportBoxState> { TransportBoxState.Reserve, TransportBoxState.Closed, TransportBoxState.Error },
            TransportBoxState.Reserve => new List<TransportBoxState> { TransportBoxState.Stocked, TransportBoxState.Closed, TransportBoxState.Error },
            TransportBoxState.Closed => new List<TransportBoxState>(), // Terminal state
            TransportBoxState.Error => new List<TransportBoxState> { TransportBoxState.Closed }, // Can only close from error
            _ => new List<TransportBoxState>()
        };
    }
    
    public TransportBoxValidationResult Validate()
    {
        var result = new TransportBoxValidationResult();
        
        if (string.IsNullOrEmpty(Code))
            result.AddError("Box code is required");
            
        if (State == TransportBoxState.InTransit && string.IsNullOrEmpty(TrackingNumber))
            result.AddWarning("Transit boxes should have tracking numbers");
            
        if (State == TransportBoxState.Received && Weight <= 0)
            result.AddWarning("Received boxes should have recorded weight");
            
        if (HasItems && Items.Any(i => i.Quantity <= 0))
            result.AddError("Items cannot have zero or negative quantities");
            
        return result;
    }
    
    private void LogStateChange(TransportBoxState from, TransportBoxState to, string userId, string? note = null)
    {
        var stateLog = new TransportBoxStateLog
        {
            Id = Guid.NewGuid(),
            TransportBoxId = Id,
            PreviousState = from,
            CurrentState = to,
            UserId = userId,
            Timestamp = DateTime.UtcNow,
            Note = note
        };
        
        StateHistory.Add(stateLog);
        LastModificationTime = DateTime.UtcNow;
    }
    
    private void CloseConflictingBox()
    {
        // This would be implemented to auto-close existing boxes with same code
        // Logic would be handled in the application service
    }
}

public enum TransportBoxState
{
    New,
    Opened,
    InTransit,
    Received,
    InSwap,
    Stocked,
    Reserve,
    Closed,
    Error
}
```

#### TransportBoxItem
```csharp
public class TransportBoxItem : AuditedEntity<Guid>
{
    public Guid TransportBoxId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public TransportBoxItemType ItemType { get; set; } = TransportBoxItemType.Product;
    public TransportBoxItemState State { get; set; } = TransportBoxItemState.Added;
    public string? UserId { get; set; }
    public string? Notes { get; set; }
    public decimal Value { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpirationDate { get; set; }
    
    // Navigation Properties
    public virtual TransportBox TransportBox { get; set; } = null!;
    
    // Computed Properties
    public decimal UnitValue => Quantity > 0 ? Value / Quantity : 0;
    public bool IsExpired => ExpirationDate.HasValue && ExpirationDate.Value < DateTime.Today;
    public bool IsNearExpiry => ExpirationDate.HasValue && ExpirationDate.Value <= DateTime.Today.AddDays(30);
    public bool IsProcessed => State == TransportBoxItemState.Processed;
    
    // Business Methods
    public void UpdateQuantity(int newQuantity, string userId)
    {
        if (newQuantity <= 0)
            throw new BusinessException("Quantity must be positive");
            
        Quantity = newQuantity;
        UserId = userId;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void SetState(TransportBoxItemState newState, string userId)
    {
        State = newState;
        UserId = userId;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void UpdateValue(decimal unitValue)
    {
        Value = unitValue * Quantity;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void SetLotInfo(string lotNumber, DateTime? expirationDate)
    {
        LotNumber = lotNumber;
        ExpirationDate = expirationDate;
        LastModificationTime = DateTime.UtcNow;
    }
}

public enum TransportBoxItemType
{
    Product,
    Material,
    Tool,
    Document,
    Sample
}

public enum TransportBoxItemState
{
    Added,
    InTransit,
    Received,
    Stocked,
    Reserved,
    Processed
}
```

#### TransportBoxStateLog
```csharp
public class TransportBoxStateLog : Entity<Guid>
{
    public Guid TransportBoxId { get; set; }
    public TransportBoxState PreviousState { get; set; }
    public TransportBoxState CurrentState { get; set; }
    public string UserId { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
    
    // Navigation Properties
    public virtual TransportBox TransportBox { get; set; } = null!;
    
    // Computed Properties
    public string StateTransition => $"{PreviousState} → {CurrentState}";
    public bool IsErrorTransition => CurrentState == TransportBoxState.Error;
    public TimeSpan TimeInPreviousState => Timestamp - CreationTime;
}
```

#### TransportBoxValidationResult (Value Object)
```csharp
public class TransportBoxValidationResult : ValueObject
{
    public bool IsValid { get; private set; } = true;
    public List<string> Errors { get; private set; } = new();
    public List<string> Warnings { get; private set; } = new();
    
    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return IsValid;
        foreach (var error in Errors) yield return error;
        foreach (var warning in Warnings) yield return warning;
    }
    
    public void AddError(string error)
    {
        Errors.Add(error);
        IsValid = false;
    }
    
    public void AddWarning(string warning)
    {
        Warnings.Add(warning);
    }
    
    public bool HasErrors => Errors.Any();
    public bool HasWarnings => Warnings.Any();
    public int TotalIssues => Errors.Count + Warnings.Count;
}
```

### Application Services

#### ITransportBoxAppService
```csharp
public interface ITransportBoxAppService : IApplicationService
{
    Task<TransportBoxDto> OpenBoxAsync(string code, string? userId = null);
    Task<TransportBoxDto> GetBoxAsync(Guid boxId);
    Task<TransportBoxDto> GetBoxByCodeAsync(string code);
    Task<PagedResultDto<TransportBoxDto>> GetBoxesAsync(GetTransportBoxesQuery query);
    
    Task<TransportBoxDto> AddItemAsync(Guid boxId, AddItemToBoxDto input);
    Task<TransportBoxDto> UpdateItemQuantityAsync(Guid boxId, Guid itemId, int quantity);
    Task<TransportBoxDto> RemoveItemAsync(Guid boxId, Guid itemId);
    
    Task<TransportBoxDto> TransitBoxAsync(Guid boxId, TransitBoxDto input);
    Task<TransportBoxDto> ReceiveBoxAsync(Guid boxId, ReceiveBoxDto input);
    Task<TransportBoxDto> StockUpBoxAsync(Guid boxId);
    Task<TransportBoxDto> SwapInBoxAsync(Guid boxId);
    Task<TransportBoxDto> ReserveBoxAsync(Guid boxId, ReserveBoxDto input);
    Task<TransportBoxDto> CloseBoxAsync(Guid boxId, string? reason = null);
    Task SetBoxErrorAsync(Guid boxId, string errorMessage);
    
    Task<List<TransportBoxStateLogDto>> GetBoxHistoryAsync(Guid boxId);
    Task<TransportBoxValidationResultDto> ValidateBoxAsync(Guid boxId);
    Task<List<TransportBoxState>> GetValidTransitionsAsync(Guid boxId);
    
    Task<List<TransportBoxDto>> GetActiveBoxesByUserAsync(string userId);
    Task<List<TransportBoxDto>> GetBoxesByStateAsync(TransportBoxState state);
    Task<TransportBoxSummaryDto> GetBoxSummaryAsync(DateTime fromDate, DateTime toDate);
    
    Task<List<ConflictingBoxDto>> GetConflictingBoxesAsync(string code);
    Task ResolveConflictingBoxesAsync(string code, Guid keepBoxId);
}
```

#### TransportBoxAppService Implementation
```csharp
[Authorize]
public class TransportBoxAppService : ApplicationService, ITransportBoxAppService
{
    private readonly ITransportBoxRepository _boxRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<TransportBoxAppService> _logger;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public TransportBoxAppService(
        ITransportBoxRepository boxRepository,
        ICatalogRepository catalogRepository,
        ILogger<TransportBoxAppService> logger,
        IBackgroundJobManager backgroundJobManager)
    {
        _boxRepository = boxRepository;
        _catalogRepository = catalogRepository;
        _logger = logger;
        _backgroundJobManager = backgroundJobManager;
    }

    public async Task<TransportBoxDto> OpenBoxAsync(string code, string? userId = null)
    {
        userId ??= CurrentUser.Id?.ToString() ?? "";
        
        _logger.LogInformation("Opening transport box with code {Code} by user {UserId}", code, userId);
        
        // Check for existing active box with same code
        var conflictingBoxes = await _boxRepository.GetActiveBoxesByCodeAsync(code);
        
        TransportBox box;
        
        if (conflictingBoxes.Any())
        {
            // Auto-close conflicting boxes
            foreach (var conflictingBox in conflictingBoxes)
            {
                conflictingBox.CloseBox(userId, "Auto-closed due to code reuse");
                await _boxRepository.UpdateAsync(conflictingBox);
                
                _logger.LogInformation("Auto-closed conflicting box {BoxId} with code {Code}", 
                    conflictingBox.Id, code);
            }
            
            // Reuse the first box
            box = conflictingBoxes.First();
            box.OpenBox(userId);
        }
        else
        {
            // Create new box
            box = new TransportBox
            {
                Id = Guid.NewGuid(),
                Code = code,
                State = TransportBoxState.New
            };
            
            await _boxRepository.InsertAsync(box);
            box.OpenBox(userId);
        }
        
        await _boxRepository.UpdateAsync(box);
        
        _logger.LogInformation("Successfully opened transport box {BoxId} with code {Code}", 
            box.Id, code);
        
        return ObjectMapper.Map<TransportBox, TransportBoxDto>(box);
    }

    public async Task<TransportBoxDto> AddItemAsync(Guid boxId, AddItemToBoxDto input)
    {
        var box = await _boxRepository.GetAsync(boxId);
        var userId = CurrentUser.Id?.ToString() ?? "";
        
        // Get product info from catalog
        var productInfo = await _catalogRepository.GetProductInfoAsync(input.ProductCode);
        
        box.AddItem(
            input.ProductCode,
            productInfo?.Name ?? input.ProductCode,
            input.Quantity,
            userId,
            input.ItemType);
        
        await _boxRepository.UpdateAsync(box);
        
        _logger.LogInformation("Added {Quantity} of {ProductCode} to transport box {BoxId}", 
            input.Quantity, input.ProductCode, boxId);
        
        return ObjectMapper.Map<TransportBox, TransportBoxDto>(box);
    }

    public async Task<TransportBoxDto> TransitBoxAsync(Guid boxId, TransitBoxDto input)
    {
        var box = await _boxRepository.GetAsync(boxId);
        var userId = CurrentUser.Id?.ToString() ?? "";
        
        box.TransitBox(input.DeliveryType, input.TrackingNumber, userId);
        await _boxRepository.UpdateAsync(box);
        
        // Queue background job for carrier notification
        await _backgroundJobManager.EnqueueAsync<NotifyCarrierJob>(
            new NotifyCarrierArgs { BoxId = boxId, DeliveryType = input.DeliveryType });
        
        _logger.LogInformation("Set transport box {BoxId} to transit with tracking {TrackingNumber}", 
            boxId, input.TrackingNumber);
        
        return ObjectMapper.Map<TransportBox, TransportBoxDto>(box);
    }

    public async Task<TransportBoxDto> ReceiveBoxAsync(Guid boxId, ReceiveBoxDto input)
    {
        var box = await _boxRepository.GetAsync(boxId);
        var userId = CurrentUser.Id?.ToString() ?? "";
        
        box.ReceiveBox(input.Weight, userId);
        await _boxRepository.UpdateAsync(box);
        
        _logger.LogInformation("Received transport box {BoxId} with weight {Weight}kg", 
            boxId, input.Weight);
        
        return ObjectMapper.Map<TransportBox, TransportBoxDto>(box);
    }

    public async Task<TransportBoxDto> StockUpBoxAsync(Guid boxId)
    {
        var box = await _boxRepository.GetAsync(boxId);
        var userId = CurrentUser.Id?.ToString() ?? "";
        
        box.StockUpBox(userId);
        
        // Update stock levels in catalog
        foreach (var item in box.Items)
        {
            await _catalogRepository.UpdateProductStockAsync(item.ProductCode, item.Quantity);
        }
        
        await _boxRepository.UpdateAsync(box);
        
        // Queue background job for ERP sync
        await _backgroundJobManager.EnqueueAsync<SyncStockLevelsJob>(
            new SyncStockLevelsArgs { BoxId = boxId });
        
        _logger.LogInformation("Stocked up transport box {BoxId} with {ItemCount} items", 
            boxId, box.ItemCount);
        
        return ObjectMapper.Map<TransportBox, TransportBoxDto>(box);
    }

    public async Task<TransportBoxDto> ReserveBoxAsync(Guid boxId, ReserveBoxDto input)
    {
        var box = await _boxRepository.GetAsync(boxId);
        var userId = CurrentUser.Id?.ToString() ?? "";
        
        box.ReserveBox(input.ReservationReference, userId);
        await _boxRepository.UpdateAsync(box);
        
        _logger.LogInformation("Reserved transport box {BoxId} for {Reference}", 
            boxId, input.ReservationReference);
        
        return ObjectMapper.Map<TransportBox, TransportBoxDto>(box);
    }

    public async Task<List<TransportBoxStateLogDto>> GetBoxHistoryAsync(Guid boxId)
    {
        var box = await _boxRepository.GetAsync(boxId);
        var history = box.StateHistory.OrderBy(h => h.Timestamp).ToList();
        
        return ObjectMapper.Map<List<TransportBoxStateLog>, List<TransportBoxStateLogDto>>(history);
    }

    public async Task<TransportBoxValidationResultDto> ValidateBoxAsync(Guid boxId)
    {
        var box = await _boxRepository.GetAsync(boxId);
        var validationResult = box.Validate();
        
        return ObjectMapper.Map<TransportBoxValidationResult, TransportBoxValidationResultDto>(validationResult);
    }

    public async Task<List<TransportBoxState>> GetValidTransitionsAsync(Guid boxId)
    {
        var box = await _boxRepository.GetAsync(boxId);
        return box.GetValidTransitions();
    }

    public async Task<TransportBoxSummaryDto> GetBoxSummaryAsync(DateTime fromDate, DateTime toDate)
    {
        var boxes = await _boxRepository.GetBoxesByDateRangeAsync(fromDate, toDate);
        
        var summary = new TransportBoxSummaryDto
        {
            FromDate = fromDate,
            ToDate = toDate,
            TotalBoxes = boxes.Count,
            BoxesByState = boxes.GroupBy(b => b.State)
                               .ToDictionary(g => g.Key, g => g.Count()),
            AverageItemsPerBox = boxes.Any() ? boxes.Average(b => b.ItemCount) : 0,
            TotalItemsProcessed = boxes.Sum(b => b.ItemCount),
            AverageProcessingTime = CalculateAverageProcessingTime(boxes),
            BoxesWithErrors = boxes.Count(b => b.State == TransportBoxState.Error)
        };
        
        return summary;
    }

    public async Task<List<ConflictingBoxDto>> GetConflictingBoxesAsync(string code)
    {
        var activeBoxes = await _boxRepository.GetActiveBoxesByCodeAsync(code);
        
        return activeBoxes.Select(box => new ConflictingBoxDto
        {
            BoxId = box.Id,
            Code = box.Code,
            State = box.State,
            CreatedDate = box.CreationTime,
            ItemCount = box.ItemCount,
            LastModified = box.LastModificationTime ?? box.CreationTime
        }).ToList();
    }

    public async Task ResolveConflictingBoxesAsync(string code, Guid keepBoxId)
    {
        var conflictingBoxes = await _boxRepository.GetActiveBoxesByCodeAsync(code);
        var userId = CurrentUser.Id?.ToString() ?? "";
        
        foreach (var box in conflictingBoxes.Where(b => b.Id != keepBoxId))
        {
            box.CloseBox(userId, "Resolved conflict - duplicate code");
            await _boxRepository.UpdateAsync(box);
        }
        
        _logger.LogInformation("Resolved {Count} conflicting boxes for code {Code}, kept box {BoxId}", 
            conflictingBoxes.Count - 1, code, keepBoxId);
    }

    private TimeSpan CalculateAverageProcessingTime(List<TransportBox> boxes)
    {
        var completedBoxes = boxes.Where(b => b.TotalProcessingTime.HasValue);
        
        if (!completedBoxes.Any())
            return TimeSpan.Zero;
            
        var totalTicks = completedBoxes.Sum(b => b.TotalProcessingTime!.Value.Ticks);
        return new TimeSpan(totalTicks / completedBoxes.Count());
    }
}
```

### Background Jobs

#### NotifyCarrierJob
```csharp
public class NotifyCarrierJob : IAsyncBackgroundJob<NotifyCarrierArgs>
{
    private readonly ITransportBoxRepository _boxRepository;
    private readonly ICarrierNotificationService _carrierService;
    private readonly ILogger<NotifyCarrierJob> _logger;

    public NotifyCarrierJob(
        ITransportBoxRepository boxRepository,
        ICarrierNotificationService carrierService,
        ILogger<NotifyCarrierJob> logger)
    {
        _boxRepository = boxRepository;
        _carrierService = carrierService;
        _logger = logger;
    }

    public async Task ExecuteAsync(NotifyCarrierArgs args)
    {
        try
        {
            var box = await _boxRepository.GetAsync(args.BoxId);
            
            if (box.State != TransportBoxState.InTransit)
            {
                _logger.LogWarning("Box {BoxId} is not in transit state", args.BoxId);
                return;
            }

            await _carrierService.NotifyShipmentAsync(args.DeliveryType, box.TrackingNumber!, box);
            
            _logger.LogInformation("Notified carrier {DeliveryType} for box {BoxId}", 
                args.DeliveryType, args.BoxId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify carrier for box {BoxId}", args.BoxId);
            throw;
        }
    }
}

public class NotifyCarrierArgs
{
    public Guid BoxId { get; set; }
    public string DeliveryType { get; set; } = "";
}
```

### Repository Interfaces

#### ITransportBoxRepository
```csharp
public interface ITransportBoxRepository : IRepository<TransportBox, Guid>
{
    Task<List<TransportBox>> GetActiveBoxesByCodeAsync(string code);
    Task<List<TransportBox>> GetBoxesByStateAsync(TransportBoxState state);
    Task<List<TransportBox>> GetBoxesByUserAsync(string userId);
    Task<List<TransportBox>> GetBoxesByDateRangeAsync(DateTime fromDate, DateTime toDate);
    Task<TransportBox?> GetBoxByCodeAsync(string code);
    Task<PagedResultDto<TransportBox>> GetPagedBoxesAsync(
        ISpecification<TransportBox> specification,
        int pageIndex,
        int pageSize,
        string sorting = null);
    Task<List<TransportBox>> GetBoxesWithTrackingAsync(string trackingNumber);
    Task<Dictionary<TransportBoxState, int>> GetBoxCountsByStateAsync();
}
```

### Performance Requirements

#### Response Time Targets
- Box operations: < 2 seconds
- State transitions: < 1 second
- History queries: < 3 seconds
- Conflict resolution: < 5 seconds

#### Scalability
- Support 10,000+ active transport boxes
- Handle 100+ concurrent state transitions
- Process 1000+ daily box operations
- Maintain complete audit trail

### Happy Day Scenarios

#### Scenario 1: Complete Box Lifecycle
```
1. Warehouse clerk opens new transport box with unique code
2. Adds multiple products with quantities to the box
3. Coordinates shipping and sets box to transit with tracking
4. Box arrives at destination and is marked as received
5. Items are stocked into inventory system
6. Box is closed completing the lifecycle
```

#### Scenario 2: Box Conflict Resolution
```
1. User attempts to open box with existing active code
2. System identifies conflicting boxes automatically
3. Auto-closes previous boxes with notification
4. Opens new box with same code
5. Maintains audit trail of all operations
```

#### Scenario 3: Error Recovery
```
1. Box encounters error during processing
2. System moves box to error state with details
3. Administrator investigates and resolves issue
4. Box is moved back to appropriate state
5. Processing continues normally
```

### Error Scenarios

#### Scenario 1: Invalid State Transition
```
User: Tries to add items to transit box
System: Shows error "Cannot add items to box in InTransit state"
Action: Guide user to valid operations for current state
```

#### Scenario 2: Empty Box Transit
```
User: Attempts to transit box without items
System: Shows error "Cannot transit empty box"
Action: Require items before allowing transit
```

#### Scenario 3: Code Conflicts
```
User: Opens box with conflicting active code
System: Shows warning "Active box with this code exists"
Action: Auto-resolve or present resolution options
```