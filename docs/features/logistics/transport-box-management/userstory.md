# Transport Box Management User Story

## Feature Overview
The Transport Box Management feature provides comprehensive lifecycle management for shipping containers throughout the supply chain. This system implements a sophisticated finite state machine to track boxes from creation through delivery, managing inventory movements, state transitions, and complete audit trails while integrating with ERP and e-commerce systems for real-time stock synchronization.

## Business Requirements

### Primary Use Case
As a warehouse operator, I want to manage transport boxes through their complete lifecycle from receipt to delivery so that I can track inventory movements accurately, maintain audit trails for compliance, and ensure proper stock synchronization across all systems while preventing duplicate box codes and invalid state transitions.

### Acceptance Criteria
1. The system shall enforce unique box codes across all active transport boxes
2. The system shall implement a finite state machine with validated state transitions
3. The system shall maintain complete audit trails of all state changes with user tracking
4. The system shall automatically close conflicting boxes when reusing codes
5. The system shall synchronize stock levels with ERP and e-commerce systems
6. The system shall track individual items within boxes with quantities and users
7. The system shall prevent invalid operations based on current box state
8. The system shall support error recovery with comprehensive error logging

## Technical Contracts

### Domain Model

```csharp
// Primary aggregate root implementing state machine
public class TransportBox : AuditedAggregateRoot<int>
{
    private List<TransportBoxItem> _items = new();
    private List<TransportBoxStateLog> _stateLog = new();
    
    // Properties
    public string? Code { get; private set; }
    public TransportBoxState State { get; private set; } = TransportBoxState.New;
    public TransportBoxState DefaultReceiveState { get; private set; } = TransportBoxState.Stocked;
    public string? Description { get; set; }
    public DateTime? LastStateChanged { get; set; }
    public string? Location { get; set; }
    
    // Collections
    public IReadOnlyList<TransportBoxItem> Items => _items;
    public IReadOnlyList<TransportBoxStateLog> StateLog => _stateLog;
    
    // Computed properties
    public bool IsInTransit => State == TransportBoxState.InTransit || 
                              State == TransportBoxState.Received || 
                              State == TransportBoxState.Opened;
    
    public bool IsInReserve => State == TransportBoxState.Reserve;
    
    // State machine navigation
    public TransportBoxState? NextState => TransitionNode.NextState?.NewState;
    public TransportBoxState? PreviousState => TransitionNode.PreviousState?.NewState;
    public TransportBoxStateNode TransitionNode => _transitions[State];
    
    // Business Methods
    public void Open(string boxCode, DateTime date, string userName)
    {
        ChangeState(TransportBoxState.Opened, date, userName, 
            TransportBoxState.New, TransportBoxState.InTransit, TransportBoxState.Reserve);
        Code = boxCode;
        Location = null;
    }
    
    public TransportBoxItem AddItem(string productCode, string productName, double amount, 
        DateTime date, string userName)
    {
        CheckState(TransportBoxState.Opened, TransportBoxState.Opened);
        var newItem = new TransportBoxItem(productCode, productName, amount, date, userName);
        _items.Add(newItem);
        return newItem;
    }
    
    public TransportBoxItem? DeleteItem(int itemId)
    {
        CheckState(TransportBoxState.Opened, TransportBoxState.Opened);
        var toDelete = _items.SingleOrDefault(s => s.Id == itemId);
        if (toDelete != null)
            _items.Remove(toDelete);
        return toDelete;
    }
    
    public void Reset(DateTime date, string userName)
    {
        _items.Clear();
        Code = null;
        ChangeState(TransportBoxState.New, date, userName);
    }
    
    public void ToTransit(DateTime date, string userName)
    {
        ChangeState(TransportBoxState.InTransit, date, userName, 
            TransportBoxState.Opened, TransportBoxState.Error);
    }
    
    public void ToReserve(DateTime date, string userName, TransportBoxLocation location)
    {
        Location = location.ToString();
        ChangeState(TransportBoxState.Reserve, date, userName, 
            TransportBoxState.Opened, TransportBoxState.Error);
    }
    
    public void Receive(DateTime date, string userName, 
        TransportBoxState receiveState = TransportBoxState.Stocked)
    {
        DefaultReceiveState = receiveState;
        ChangeState(TransportBoxState.Received, date, userName, 
            TransportBoxState.InTransit, TransportBoxState.Reserve);
    }
    
    public void ToSwap(DateTime date, string userName)
    {
        ChangeState(TransportBoxState.InSwap, date, userName, 
            TransportBoxState.Received, TransportBoxState.Stocked);
    }
    
    public void ToPick(DateTime date, string userName)
    {
        ChangeState(TransportBoxState.Stocked, date, userName, 
            TransportBoxState.InSwap, TransportBoxState.Received);
    }
    
    public void Close(DateTime date, string userName)
    {
        ChangeState(TransportBoxState.Closed, date, userName);
    }
    
    public void Error(DateTime date, string userName, string exMessage)
    {
        ChangeState(TransportBoxState.Error, date, userName, exMessage, 
            Array.Empty<TransportBoxState>());
    }
    
    private void ChangeState(TransportBoxState newState, DateTime now, string userName, 
        params TransportBoxState[] allowedStates)
    {
        CheckState(newState, allowedStates);
        
        State = newState;
        LastStateChanged = now;
        _stateLog.Add(new TransportBoxStateLog(newState, now, userName, null));
    }
    
    private void CheckState(TransportBoxState newState, params TransportBoxState[] allowedStates)
    {
        if (allowedStates.Any() && !allowedStates.Contains(State))
        {
            throw new AbpValidationException($"Invalid state transition", 
                new List<ValidationResult>() 
                { 
                    new($"Unable to change state from {State} to {newState} " +
                        $"({string.Join(", ", allowedStates)} state is required for this action)")
                });
        }
    }
    
    // Static state machine configuration
    private static readonly Dictionary<TransportBoxState, TransportBoxStateNode> _transitions = new();
    
    static TransportBox()
    {
        ConfigureStateMachine();
    }
}

// Entity representing items within transport box
public class TransportBoxItem : Entity<int>
{
    public string ProductCode { get; private set; }
    public string ProductName { get; private set; }
    public double Amount { get; private set; }
    public DateTime DateAdded { get; private set; }
    public string UserAdded { get; private set; }
    
    public TransportBoxItem(string productCode, string productName, double amount, 
        DateTime dateAdded, string userAdded)
    {
        if (string.IsNullOrEmpty(productCode))
            throw new BusinessException("Product code is required");
        
        if (string.IsNullOrEmpty(productName))
            throw new BusinessException("Product name is required");
        
        if (amount <= 0)
            throw new BusinessException("Amount must be positive");
        
        ProductCode = productCode;
        ProductName = productName;
        Amount = amount;
        DateAdded = dateAdded;
        UserAdded = userAdded;
    }
}

// Audit trail entity
public class TransportBoxStateLog : Entity<int>
{
    public TransportBoxState State { get; private set; }
    public DateTime Timestamp { get; private set; }
    public string UserName { get; private set; }
    public string? Description { get; private set; }
    
    public TransportBoxStateLog(TransportBoxState state, DateTime timestamp, 
        string userName, string? description)
    {
        State = state;
        Timestamp = timestamp;
        UserName = userName;
        Description = description;
    }
}

// State machine node configuration
public class TransportBoxStateNode
{
    public TransportBoxAction? NextState { get; set; }
    public TransportBoxAction? PreviousState { get; set; }
}

// State transition action
public class TransportBoxAction
{
    public TransportBoxState NewState { get; set; }
    public Action<TransportBox, DateTime, string> Action { get; set; }
    public Func<TransportBox, bool>? Condition { get; set; }
    
    public TransportBoxAction(TransportBoxState newState, 
        Action<TransportBox, DateTime, string> action, 
        Func<TransportBox, bool>? condition = null)
    {
        NewState = newState;
        Action = action;
        Condition = condition;
    }
}

// State enumeration
public enum TransportBoxState
{
    New = 0,
    Opened = 1,
    InTransit = 2,
    Received = 3,
    InSwap = 4,
    Stocked = 5,
    Reserve = 6,
    Closed = 7,
    Error = 8
}

// Location enumeration
public enum TransportBoxLocation
{
    Warehouse = 0,
    Transit = 1,
    Store = 2,
    Reserve = 3
}
```

### Application Layer Contracts

```csharp
// Application service interface
public interface ITransportBoxAppService : ICrudAppService<TransportBoxDto, int, TransportBoxRequestDto, 
    TransportBoxCreateDto, TransportBoxUpdateDto>
{
    Task<TransportBoxDto> OpenAsync(OpenBoxDto dto);
    Task<TransportBoxDto> ResetAsync(ResetBoxDto dto);
    Task<TransportBoxDto> ToTransitAsync(ToTransitBoxDto dto);
    Task<TransportBoxDto> CloseAsync(CloseBoxDto dto);
    Task<TransportBoxDto> ReceiveAsync(ReceiveBoxDto dto);
    Task<TransportBoxDto> ToReserveAsync(ToReserveBoxDto dto);
    Task<TransportBoxDto> StockUpBoxAsync(StockUpBoxDto dto);
    Task<TransportBoxDto> SwapInAsync(SwapInBoxDto dto);
    Task<TransportBoxDto> AddItemsAsync(AddItemsDto dto);
    Task<TransportBoxDto> RemoveItemAsync(RemoveItemDto dto);
    Task<StockUpResultDto> ExecuteStockUpAsync(ExecuteStockUpDto dto);
}

// DTOs
public class TransportBoxDto
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public TransportBoxState State { get; set; }
    public TransportBoxState DefaultReceiveState { get; set; }
    public string? Description { get; set; }
    public DateTime? LastStateChanged { get; set; }
    public string? Location { get; set; }
    public List<TransportBoxItemDto> Items { get; set; } = new();
    public List<TransportBoxStateLogDto> StateLog { get; set; } = new();
    public bool IsInTransit { get; set; }
    public bool IsInReserve { get; set; }
    public TransportBoxState? NextState { get; set; }
    public TransportBoxState? PreviousState { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

public class TransportBoxItemDto
{
    public int Id { get; set; }
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double Amount { get; set; }
    public DateTime DateAdded { get; set; }
    public string UserAdded { get; set; }
}

public class TransportBoxStateLogDto
{
    public int Id { get; set; }
    public TransportBoxState State { get; set; }
    public DateTime Timestamp { get; set; }
    public string UserName { get; set; }
    public string? Description { get; set; }
}

public class OpenBoxDto
{
    public int Id { get; set; }
    [Required]
    public string Code { get; set; }
}

public class ToTransitBoxDto
{
    public int Id { get; set; }
    [Required]
    public string Code { get; set; }
}

public class ReceiveBoxDto
{
    public int Id { get; set; }
    public double? Weight { get; set; }
    public TransportBoxState ReceiveState { get; set; } = TransportBoxState.Stocked;
}

public class AddItemsDto
{
    public int BoxId { get; set; }
    public List<TransportBoxItemRequestDto> Items { get; set; } = new();
}

public class TransportBoxItemRequestDto
{
    [Required]
    public string ProductCode { get; set; }
    [Required]
    public string ProductName { get; set; }
    [Range(0.01, double.MaxValue)]
    public double Amount { get; set; }
}

public class StockUpResultDto
{
    public bool Success { get; set; }
    public int ItemsProcessed { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, double> StockUpdates { get; set; } = new();
}
```

### Repository Pattern

```csharp
public interface ITransportBoxRepository : IRepository<TransportBox, int>
{
    Task<TransportBox?> FindByCodeAsync(string code, params TransportBoxState[] excludeStates);
    Task<List<TransportBox>> GetActiveBoxesAsync();
    Task<List<TransportBox>> GetBoxesByStateAsync(TransportBoxState state);
    Task<List<TransportBox>> GetBoxesRequiringActionAsync();
    Task<PagedResultDto<TransportBox>> GetPagedListAsync(
        int skipCount,
        int maxResultCount,
        string sorting = null,
        string? code = null,
        TransportBoxState? state = null,
        DateTime? fromDate = null,
        DateTime? toDate = null);
}
```

## Implementation Details

### State Machine Configuration

```csharp
public static class TransportBoxStateMachine
{
    public static void ConfigureStateMachine()
    {
        // New state - can transition to Opened
        _transitions.Add(TransportBoxState.New, new TransportBoxStateNode()
        {
            NextState = new TransportBoxAction(
                TransportBoxState.Opened, 
                (box, time, userName) => box.Open(box.Code!, time, userName), 
                condition: b => b.Code != null),
            PreviousState = new TransportBoxAction(
                TransportBoxState.Closed, 
                (box, time, userName) => box.Close(time, userName))
        });
        
        // Opened state - can add items and transition to transit
        _transitions.Add(TransportBoxState.Opened, new TransportBoxStateNode()
        {
            NextState = new TransportBoxAction(
                TransportBoxState.InTransit, 
                (box, time, userName) => box.ToTransit(time, userName)),
            PreviousState = new TransportBoxAction(
                TransportBoxState.New, 
                (box, time, userName) => box.Reset(time, userName))
        });
        
        // InTransit state - awaiting receipt
        _transitions.Add(TransportBoxState.InTransit, new TransportBoxStateNode()
        {
            NextState = new TransportBoxAction(
                TransportBoxState.Received, 
                (box, time, userName) => box.Receive(time, userName)),
            PreviousState = new TransportBoxAction(
                TransportBoxState.Opened, 
                (box, time, userName) => box.Open(box.Code!, time, userName), 
                condition: b => b.Code != null)
        });
        
        // Received state - can go to swap or stocked
        _transitions.Add(TransportBoxState.Received, new TransportBoxStateNode());
        
        // InSwap state - inventory reconciliation
        _transitions.Add(TransportBoxState.InSwap, new TransportBoxStateNode()
        {
            NextState = new TransportBoxAction(
                TransportBoxState.Stocked, 
                (box, time, userName) => box.ToPick(time, userName))
        });
        
        // Stocked state - items available for picking
        _transitions.Add(TransportBoxState.Stocked, new TransportBoxStateNode()
        {
            NextState = new TransportBoxAction(
                TransportBoxState.Closed, 
                (box, time, userName) => box.Close(time, userName))
        });
        
        // Reserve state - items held for specific orders
        _transitions.Add(TransportBoxState.Reserve, new TransportBoxStateNode()
        {
            NextState = new TransportBoxAction(
                TransportBoxState.Received, 
                (box, time, userName) => box.Receive(time, userName)),
            PreviousState = new TransportBoxAction(
                TransportBoxState.Opened, 
                (box, time, userName) => box.Open(box.Code!, time, userName), 
                condition: b => b.Code != null)
        });
        
        // Terminal states
        _transitions.Add(TransportBoxState.Closed, new TransportBoxStateNode());
        _transitions.Add(TransportBoxState.Error, new TransportBoxStateNode());
    }
}
```

### Application Service Implementation

```csharp
[Authorize]
public class TransportBoxAppService : CrudAppService<TransportBox, TransportBoxDto, int, 
    TransportBoxRequestDto, TransportBoxCreateDto, TransportBoxUpdateDto>, ITransportBoxAppService
{
    private readonly IClock _clock;
    private readonly ICurrentUser _userProvider;
    private readonly IEshopStockTakingDomainService _stockUpDomainService;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<TransportBoxAppService> _logger;
    
    public TransportBoxAppService(
        IRepository<TransportBox, int> repository,
        IClock clock,
        ICurrentUser userProvider,
        IEshopStockTakingDomainService stockUpService,
        ICatalogRepository catalogRepository,
        ILogger<TransportBoxAppService> logger)
        : base(repository)
    {
        _clock = clock;
        _userProvider = userProvider;
        _stockUpDomainService = stockUpService;
        _catalogRepository = catalogRepository;
        _logger = logger;
    }
    
    public async Task<TransportBoxDto> OpenAsync(OpenBoxDto dto)
    {
        var box = await Repository.GetAsync(dto.Id);
        
        // Check for duplicate codes in active states
        var duplicate = await Repository.FindAsync(f => 
            f.Code == dto.Code && 
            f.State != TransportBoxState.Stocked && 
            f.State != TransportBoxState.Closed && 
            f.Id != dto.Id);
            
        if (duplicate != null)
        {
            throw new AbpValidationException(
                "Open box failed", 
                new List<ValidationResult>() 
                { 
                    new($"There is already box with same code {dto.Code} in state {duplicate.State}")
                });
        }
        
        // Open the box
        box.Open(dto.Code, _clock.Now, _userProvider.UserName);
        
        // Auto-close any stocked boxes with same code
        var stocked = await Repository.GetListAsync(w => 
            w.Code == dto.Code && 
            w.State == TransportBoxState.Stocked);
            
        foreach (var s in stocked)
        {
            s.Close(_clock.Now, _userProvider.UserName);
            await Repository.UpdateAsync(s, true);
        }
        
        box = await Repository.UpdateAsync(box, true);
        
        _logger.LogInformation("Box {BoxId} opened with code {Code} by user {User}", 
            box.Id, dto.Code, _userProvider.UserName);
        
        return ObjectMapper.Map<TransportBox, TransportBoxDto>(box);
    }
    
    public async Task<TransportBoxDto> AddItemsAsync(AddItemsDto dto)
    {
        var box = await Repository.GetAsync(dto.BoxId);
        
        foreach (var itemDto in dto.Items)
        {
            try
            {
                box.AddItem(
                    itemDto.ProductCode, 
                    itemDto.ProductName, 
                    itemDto.Amount, 
                    _clock.Now, 
                    _userProvider.UserName);
                    
                _logger.LogDebug("Added item {ProductCode} quantity {Amount} to box {BoxId}", 
                    itemDto.ProductCode, itemDto.Amount, box.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add item {ProductCode} to box {BoxId}", 
                    itemDto.ProductCode, box.Id);
                throw;
            }
        }
        
        box = await Repository.UpdateAsync(box, true);
        
        return ObjectMapper.Map<TransportBox, TransportBoxDto>(box);
    }
    
    public async Task<TransportBoxDto> ToTransitAsync(ToTransitBoxDto dto)
    {
        var box = await Repository.GetAsync(dto.Id);
        
        if (dto.Code != box.Code)
        {
            throw new AbpValidationException(
                "Close box failed", 
                new List<ValidationResult>() 
                { 
                    new($"Closing box code {dto.Code} is not the same as code " +
                        $"assigned to box during opening ({box.Code})")
                });
        }
        
        if (!box.Items.Any())
        {
            throw new UserFriendlyException("Cannot transit empty box");
        }
        
        box.ToTransit(_clock.Now, _userProvider.UserName);
        box = await Repository.UpdateAsync(box, true);
        
        _logger.LogInformation("Box {BoxId} transitioned to transit by user {User}", 
            box.Id, _userProvider.UserName);
        
        return ObjectMapper.Map<TransportBox, TransportBoxDto>(box);
    }
    
    public async Task<TransportBoxDto> ReceiveAsync(ReceiveBoxDto dto)
    {
        var box = await Repository.GetAsync(dto.Id);
        
        box.Receive(_clock.Now, _userProvider.UserName, dto.ReceiveState);
        
        if (dto.Weight.HasValue)
        {
            box.Description = $"Weight: {dto.Weight}kg";
        }
        
        box = await Repository.UpdateAsync(box, true);
        
        _logger.LogInformation("Box {BoxId} received by user {User} with state {State}", 
            box.Id, _userProvider.UserName, dto.ReceiveState);
        
        return ObjectMapper.Map<TransportBox, TransportBoxDto>(box);
    }
    
    public async Task<StockUpResultDto> ExecuteStockUpAsync(ExecuteStockUpDto dto)
    {
        var box = await Repository.GetAsync(dto.BoxId, includeDetails: true);
        
        if (box.State != TransportBoxState.Received && box.State != TransportBoxState.InSwap)
        {
            throw new UserFriendlyException(
                $"Box must be in Received or InSwap state to stock up (current: {box.State})");
        }
        
        var result = new StockUpResultDto();
        
        try
        {
            // Process each item for stock update
            foreach (var item in box.Items)
            {
                try
                {
                    var catalog = await _catalogRepository.GetAsync(item.ProductCode);
                    
                    if (catalog == null)
                    {
                        result.Errors.Add($"Product {item.ProductCode} not found in catalog");
                        continue;
                    }
                    
                    // Update stock levels
                    var stockUpdate = await _stockUpDomainService.StockUpAsync(
                        item.ProductCode, 
                        item.Amount, 
                        _userProvider.UserName);
                    
                    result.StockUpdates[item.ProductCode] = item.Amount;
                    result.ItemsProcessed++;
                    
                    _logger.LogInformation("Stocked up {ProductCode} quantity {Amount}", 
                        item.ProductCode, item.Amount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to stock up item {ProductCode}", item.ProductCode);
                    result.Errors.Add($"Failed to stock up {item.ProductCode}: {ex.Message}");
                }
            }
            
            // Update box state if successful
            if (result.ItemsProcessed > 0 && result.ItemsProcessed == box.Items.Count)
            {
                box.ToPick(_clock.Now, _userProvider.UserName);
                await Repository.UpdateAsync(box, true);
                result.Success = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute stock up for box {BoxId}", box.Id);
            throw new UserFriendlyException("Stock up failed: " + ex.Message);
        }
        
        return result;
    }
    
    protected override async Task<IQueryable<TransportBox>> CreateFilteredQueryAsync(
        TransportBoxRequestDto input)
    {
        var query = await base.CreateFilteredQueryAsync(input);
        
        if (!string.IsNullOrWhiteSpace(input.Code))
        {
            query = query.Where(x => x.Code.Contains(input.Code));
        }
        
        if (input.State.HasValue)
        {
            query = query.Where(x => x.State == input.State.Value);
        }
        
        if (input.FromDate.HasValue)
        {
            query = query.Where(x => x.CreationTime >= input.FromDate.Value);
        }
        
        if (input.ToDate.HasValue)
        {
            query = query.Where(x => x.CreationTime <= input.ToDate.Value);
        }
        
        return query;
    }
}
```

## State Transition Flow

```
┌─────┐      ┌────────┐      ┌───────────┐      ┌──────────┐
│ New │ ───> │ Opened │ ───> │ InTransit │ ───> │ Received │
└─────┘      └────────┘      └───────────┘      └──────────┘
   │              │                                     │
   │              ↓                                     ↓
   │         ┌─────────┐                         ┌─────────┐
   │         │ Reserve │                         │ InSwap  │
   │         └─────────┘                         └─────────┘
   │              │                                     │
   │              ↓                                     ↓
   │         ┌──────────┐                       ┌──────────┐
   └──────> │  Closed   │ <─────────────────── │ Stocked  │
            └──────────┘                       └──────────┘
                 ↑
                 │
            ┌─────────┐
            │  Error  │ (Can transition from any state)
            └─────────┘
```

## Happy Day Scenario

1. **Box Creation**: Create new transport box in system
2. **Box Opening**: Assign unique code and open for item management
3. **Item Addition**: Add products with quantities to open box
4. **Transit Transition**: Mark box as shipped with tracking info
5. **Receipt Processing**: Receive box at destination warehouse
6. **Stock Integration**: Update inventory levels in all systems
7. **Box Closure**: Complete lifecycle and archive for audit

## Error Handling

### State Validation Errors
- **Invalid Transition**: Clear error message with allowed states
- **Duplicate Code**: Prevent duplicate active box codes
- **Empty Box Transit**: Cannot ship boxes without items
- **State Mismatch**: Validate expected vs actual state

### Data Integrity Errors
- **Missing Product**: Handle unknown product codes gracefully
- **Negative Quantities**: Validate positive amounts only
- **User Tracking**: Ensure all operations have user context
- **Concurrent Updates**: Handle optimistic concurrency

### Integration Errors
- **Stock Update Failures**: Rollback box state on error
- **ERP Sync Issues**: Queue for retry with exponential backoff
- **Network Timeouts**: Implement circuit breaker pattern
- **Partial Success**: Track individual item successes/failures

## Business Rules

### Code Management
1. **Uniqueness**: No duplicate codes in active states (not Closed/Error)
2. **Auto-Closure**: Automatically close stocked boxes when reusing code
3. **Code Validation**: Required for transit operations
4. **Code Immutability**: Cannot change code after opening

### State Constraints
1. **Item Management**: Items only added/removed in Opened state
2. **Transit Requirements**: Must have items to transition to transit
3. **Receive Validation**: Only InTransit/Reserve boxes can be received
4. **Terminal States**: Closed and Error states are final

### Audit Requirements
1. **User Tracking**: All state changes record user and timestamp
2. **Complete History**: Full state transition log maintained
3. **Description Support**: Optional descriptions for context
4. **Compliance Ready**: Audit trail for regulatory requirements

## Persistence Layer Requirements

### Database Schema
```sql
CREATE TABLE TransportBoxes (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Code NVARCHAR(50) NULL,
    State INT NOT NULL,
    DefaultReceiveState INT NOT NULL,
    Description NVARCHAR(MAX) NULL,
    LastStateChanged DATETIME2 NULL,
    Location NVARCHAR(50) NULL,
    CreationTime DATETIME2 NOT NULL,
    CreatorId UNIQUEIDENTIFIER NULL,
    LastModificationTime DATETIME2 NULL,
    LastModifierId UNIQUEIDENTIFIER NULL,
    INDEX IX_TransportBoxes_Code (Code),
    INDEX IX_TransportBoxes_State (State),
    INDEX IX_TransportBoxes_CreationTime (CreationTime)
);

CREATE TABLE TransportBoxItems (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TransportBoxId INT NOT NULL,
    ProductCode NVARCHAR(50) NOT NULL,
    ProductName NVARCHAR(255) NOT NULL,
    Amount FLOAT NOT NULL,
    DateAdded DATETIME2 NOT NULL,
    UserAdded NVARCHAR(255) NOT NULL,
    FOREIGN KEY (TransportBoxId) REFERENCES TransportBoxes(Id) ON DELETE CASCADE,
    INDEX IX_TransportBoxItems_ProductCode (ProductCode)
);

CREATE TABLE TransportBoxStateLogs (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TransportBoxId INT NOT NULL,
    State INT NOT NULL,
    Timestamp DATETIME2 NOT NULL,
    UserName NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    FOREIGN KEY (TransportBoxId) REFERENCES TransportBoxes(Id) ON DELETE CASCADE,
    INDEX IX_TransportBoxStateLogs_Timestamp (Timestamp)
);
```

### Caching Strategy
- **Active Box Cache**: Cache frequently accessed active boxes (TTL: 5 minutes)
- **State Machine Cache**: Static configuration cached indefinitely
- **Code Lookup Cache**: Fast duplicate detection (TTL: 1 minute)
- **Invalidation**: Clear cache on state changes

## Integration Requirements

### ERP Integration
- Real-time stock level synchronization
- Product master data validation
- Transaction logging for reconciliation
- Error queue for failed updates

### E-commerce Integration
- Inventory availability updates
- Order fulfillment tracking
- Reserve stock allocation
- Multi-channel inventory sync

### Warehouse Management
- Physical location tracking
- Picking list generation
- Receiving documentation
- Cycle count integration

## Security Considerations

### Access Control
- Role-based permissions for state transitions
- Warehouse-specific access restrictions
- Audit trail protection from tampering
- Sensitive data encryption

### Data Protection
- User activity logging
- State change authorization
- API security for integrations
- Backup and recovery procedures

## Performance Requirements
- Handle 1000+ active transport boxes concurrently
- Sub-second response times for state transitions
- Support 100+ items per box efficiently
- Scale horizontally for multiple warehouses
- Real-time stock synchronization within 2 seconds
- Maintain 99.9% uptime for critical operations