# User Story: Picking Operations Management

## Feature Description
The Picking Operations Management feature orchestrates warehouse picking workflows with order-based picking lists, item location tracking, and real-time inventory updates. The system manages picking sessions, supports batch picking optimization, integrates with transport box management, and provides comprehensive audit trails for order fulfillment operations.

## Business Requirements

### Primary Use Cases
1. **Picking Session Management**: Create and manage picking sessions for order fulfillment
2. **Order-Based Picking Lists**: Generate optimized picking lists from customer orders
3. **Item Location Tracking**: Track item locations and guide pickers efficiently
4. **Batch Picking Optimization**: Optimize picking routes and batch multiple orders
5. **Inventory Integration**: Real-time inventory updates during picking operations

### User Stories
- As a warehouse manager, I want to create picking sessions so I can organize order fulfillment efficiently
- As a picker, I want optimized picking lists so I can fulfill orders quickly and accurately
- As an operations manager, I want to track picking performance so I can optimize warehouse operations
- As an inventory clerk, I want automatic stock updates so I can maintain accurate inventory levels

## Technical Requirements

### Domain Models

#### PickingSession
```csharp
public class PickingSession : AuditedAggregateRoot<Guid>
{
    public string SessionNumber { get; set; } = "";
    public string SessionName { get; set; } = "";
    public PickingSessionType Type { get; set; } = PickingSessionType.Standard;
    public PickingSessionStatus Status { get; set; } = PickingSessionStatus.Created;
    public DateTime PlannedStartDate { get; set; }
    public DateTime? ActualStartDate { get; set; }
    public DateTime? CompletionDate { get; set; }
    public string AssignedPickerId { get; set; } = "";
    public string? WarehouseZone { get; set; }
    public PickingPriority Priority { get; set; } = PickingPriority.Normal;
    public string? Notes { get; set; }
    
    // Navigation Properties
    public virtual ICollection<PickingOrder> Orders { get; set; } = new List<PickingOrder>();
    public virtual ICollection<PickingItem> Items { get; set; } = new List<PickingItem>();
    public virtual ICollection<PickingRoute> Routes { get; set; } = new List<PickingRoute>();
    
    // Computed Properties
    public int TotalOrders => Orders.Count;
    public int CompletedOrders => Orders.Count(o => o.Status == PickingOrderStatus.Completed);
    public int TotalItems => Items.Count;
    public int PickedItems => Items.Count(i => i.Status == PickingItemStatus.Picked);
    public decimal CompletionPercentage => TotalItems > 0 ? (PickedItems / (decimal)TotalItems) * 100 : 0;
    public TimeSpan? SessionDuration => CompletionDate - ActualStartDate;
    public bool IsActive => Status == PickingSessionStatus.InProgress;
    public bool CanStart => Status == PickingSessionStatus.Created && Items.Any();
    public bool CanComplete => Status == PickingSessionStatus.InProgress && PickedItems == TotalItems;
    
    // Business Methods
    public void AddOrder(string orderNumber, string customerName, PickingPriority orderPriority = PickingPriority.Normal)
    {
        if (Status != PickingSessionStatus.Created)
            throw new BusinessException("Cannot add orders to session in progress");
            
        var order = new PickingOrder
        {
            Id = Guid.NewGuid(),
            SessionId = Id,
            OrderNumber = orderNumber,
            CustomerName = customerName,
            Priority = orderPriority,
            Status = PickingOrderStatus.Planned,
            OrderDate = DateTime.UtcNow
        };
        
        Orders.Add(order);
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void AddItem(string orderNumber, string productCode, string productName, int quantity, string locationCode, string? lotNumber = null)
    {
        if (Status != PickingSessionStatus.Created)
            throw new BusinessException("Cannot add items to session in progress");
            
        var order = Orders.FirstOrDefault(o => o.OrderNumber == orderNumber);
        if (order == null)
            throw new BusinessException($"Order {orderNumber} not found in session");
            
        var item = new PickingItem
        {
            Id = Guid.NewGuid(),
            SessionId = Id,
            OrderId = order.Id,
            ProductCode = productCode,
            ProductName = productName,
            RequiredQuantity = quantity,
            LocationCode = locationCode,
            LotNumber = lotNumber,
            Status = PickingItemStatus.Pending,
            PickingSequence = Items.Count + 1
        };
        
        Items.Add(item);
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void StartSession(string pickerId)
    {
        if (!CanStart)
            throw new BusinessException("Session cannot be started");
            
        Status = PickingSessionStatus.InProgress;
        ActualStartDate = DateTime.UtcNow;
        AssignedPickerId = pickerId;
        
        // Set orders to in progress
        foreach (var order in Orders)
        {
            order.StartPicking();
        }
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void PickItem(Guid itemId, int pickedQuantity, string pickerId, string? notes = null)
    {
        if (Status != PickingSessionStatus.InProgress)
            throw new BusinessException("Can only pick items in active sessions");
            
        var item = Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null)
            throw new BusinessException($"Item {itemId} not found in session");
            
        item.PickItem(pickedQuantity, pickerId, notes);
        
        // Check if order is complete
        var order = Orders.FirstOrDefault(o => o.Id == item.OrderId);
        if (order != null && order.IsComplete)
        {
            order.CompletePicking();
        }
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CompleteSession()
    {
        if (!CanComplete)
            throw new BusinessException("Cannot complete session with unpicked items");
            
        Status = PickingSessionStatus.Completed;
        CompletionDate = DateTime.UtcNow;
        
        // Complete all orders
        foreach (var order in Orders.Where(o => o.Status != PickingOrderStatus.Completed))
        {
            order.CompletePicking();
        }
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void OptimizePickingRoute()
    {
        if (Status != PickingSessionStatus.Created)
            throw new BusinessException("Can only optimize routes for new sessions");
            
        var routeOptimizer = new PickingRouteOptimizer();
        var optimizedRoute = routeOptimizer.OptimizeRoute(Items.ToList(), WarehouseZone);
        
        // Update picking sequence based on optimized route
        for (int i = 0; i < optimizedRoute.OptimizedItems.Count; i++)
        {
            var item = Items.FirstOrDefault(x => x.Id == optimizedRoute.OptimizedItems[i].ItemId);
            if (item != null)
            {
                item.PickingSequence = i + 1;
            }
        }
        
        // Create route record
        var route = new PickingRoute
        {
            SessionId = Id,
            RouteDescription = optimizedRoute.Description,
            TotalDistance = optimizedRoute.EstimatedDistance,
            EstimatedTime = optimizedRoute.EstimatedTime,
            CreationTime = DateTime.UtcNow
        };
        
        Routes.Add(route);
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CancelSession(string reason)
    {
        if (Status == PickingSessionStatus.Completed)
            throw new BusinessException("Cannot cancel completed sessions");
            
        Status = PickingSessionStatus.Cancelled;
        Notes = $"Cancelled: {reason}";
        
        // Cancel all orders
        foreach (var order in Orders.Where(o => o.Status != PickingOrderStatus.Completed))
        {
            order.CancelPicking(reason);
        }
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public PickingPerformanceMetrics GetPerformanceMetrics()
    {
        if (Status != PickingSessionStatus.Completed)
            return new PickingPerformanceMetrics { IsComplete = false };
            
        var totalTime = SessionDuration ?? TimeSpan.Zero;
        var itemsPerHour = totalTime.TotalHours > 0 ? TotalItems / totalTime.TotalHours : 0;
        var accuracy = TotalItems > 0 ? (Items.Count(i => i.IsAccurate) / (double)TotalItems) * 100 : 100;
        
        return new PickingPerformanceMetrics
        {
            IsComplete = true,
            TotalItems = TotalItems,
            TotalTime = totalTime,
            ItemsPerHour = itemsPerHour,
            AccuracyPercentage = accuracy,
            TotalDistance = Routes.Sum(r => r.TotalDistance),
            PickerId = AssignedPickerId
        };
    }
}

public enum PickingSessionType
{
    Standard,
    Express,
    Batch,
    Zone
}

public enum PickingSessionStatus
{
    Created,
    InProgress,
    Completed,
    Cancelled
}

public enum PickingPriority
{
    Low,
    Normal,
    High,
    Critical
}
```

#### PickingOrder
```csharp
public class PickingOrder : AuditedEntity<Guid>
{
    public Guid SessionId { get; set; }
    public string OrderNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public PickingOrderStatus Status { get; set; } = PickingOrderStatus.Planned;
    public PickingPriority Priority { get; set; } = PickingPriority.Normal;
    public DateTime? StartTime { get; set; }
    public DateTime? CompletionTime { get; set; }
    public string? Notes { get; set; }
    public string? ShippingAddress { get; set; }
    public string? TransportBoxId { get; set; }
    
    // Navigation Properties
    public virtual PickingSession Session { get; set; } = null!;
    public virtual ICollection<PickingItem> Items { get; set; } = new List<PickingItem>();
    
    // Computed Properties
    public int TotalItems => Items.Count;
    public int PickedItems => Items.Count(i => i.Status == PickingItemStatus.Picked);
    public bool IsComplete => TotalItems > 0 && PickedItems == TotalItems;
    public TimeSpan? ProcessingTime => CompletionTime - StartTime;
    public decimal CompletionPercentage => TotalItems > 0 ? (PickedItems / (decimal)TotalItems) * 100 : 0;
    
    // Business Methods
    public void StartPicking()
    {
        if (Status != PickingOrderStatus.Planned)
            throw new BusinessException("Can only start planned orders");
            
        Status = PickingOrderStatus.InProgress;
        StartTime = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CompletePicking()
    {
        if (!IsComplete)
            throw new BusinessException("Cannot complete order with unpicked items");
            
        Status = PickingOrderStatus.Completed;
        CompletionTime = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void CancelPicking(string reason)
    {
        Status = PickingOrderStatus.Cancelled;
        Notes = $"Cancelled: {reason}";
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void AssignToTransportBox(string transportBoxId)
    {
        TransportBoxId = transportBoxId;
        LastModificationTime = DateTime.UtcNow;
    }
}

public enum PickingOrderStatus
{
    Planned,
    InProgress,
    Completed,
    Cancelled
}
```

#### PickingItem
```csharp
public class PickingItem : AuditedEntity<Guid>
{
    public Guid SessionId { get; set; }
    public Guid OrderId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int RequiredQuantity { get; set; }
    public int? PickedQuantity { get; set; }
    public string LocationCode { get; set; } = "";
    public string? LotNumber { get; set; }
    public PickingItemStatus Status { get; set; } = PickingItemStatus.Pending;
    public int PickingSequence { get; set; }
    public DateTime? PickingTime { get; set; }
    public string? PickedBy { get; set; }
    public string? Notes { get; set; }
    public decimal? Weight { get; set; }
    public DateTime? ExpirationDate { get; set; }
    
    // Navigation Properties
    public virtual PickingSession Session { get; set; } = null!;
    public virtual PickingOrder Order { get; set; } = null!;
    
    // Computed Properties
    public int QuantityVariance => (PickedQuantity ?? 0) - RequiredQuantity;
    public bool HasVariance => QuantityVariance != 0;
    public bool IsShortPick => QuantityVariance < 0;
    public bool IsOverPick => QuantityVariance > 0;
    public bool IsAccurate => !HasVariance;
    public bool IsPicked => Status == PickingItemStatus.Picked;
    public bool IsExpired => ExpirationDate.HasValue && ExpirationDate.Value < DateTime.Today;
    public bool IsNearExpiry => ExpirationDate.HasValue && ExpirationDate.Value <= DateTime.Today.AddDays(7);
    
    // Business Methods
    public void PickItem(int quantity, string pickerId, string? notes = null)
    {
        if (Status == PickingItemStatus.Picked)
            throw new BusinessException("Item already picked");
            
        PickedQuantity = quantity;
        PickingTime = DateTime.UtcNow;
        PickedBy = pickerId;
        Notes = notes;
        Status = PickingItemStatus.Picked;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void SkipItem(string reason)
    {
        Status = PickingItemStatus.Skipped;
        Notes = $"Skipped: {reason}";
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void ResetPicking()
    {
        PickedQuantity = null;
        PickingTime = null;
        PickedBy = null;
        Status = PickingItemStatus.Pending;
        Notes = null;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void UpdateLocation(string newLocationCode)
    {
        LocationCode = newLocationCode;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void SetExpirationDate(DateTime expirationDate)
    {
        ExpirationDate = expirationDate;
        LastModificationTime = DateTime.UtcNow;
    }
}

public enum PickingItemStatus
{
    Pending,
    Picked,
    Skipped,
    BackOrdered
}
```

#### PickingRoute
```csharp
public class PickingRoute : Entity<Guid>
{
    public Guid SessionId { get; set; }
    public string RouteDescription { get; set; } = "";
    public decimal TotalDistance { get; set; }
    public TimeSpan EstimatedTime { get; set; }
    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
    public string? OptimizationAlgorithm { get; set; }
    public string? RouteSteps { get; set; } // JSON serialized route steps
    
    // Navigation Properties
    public virtual PickingSession Session { get; set; } = null!;
    
    // Computed Properties
    public bool IsOptimized => !string.IsNullOrEmpty(OptimizationAlgorithm);
    public decimal EstimatedSpeed => EstimatedTime.TotalHours > 0 ? TotalDistance / (decimal)EstimatedTime.TotalHours : 0;
}
```

#### PickingPerformanceMetrics (Value Object)
```csharp
public class PickingPerformanceMetrics : ValueObject
{
    public bool IsComplete { get; set; }
    public int TotalItems { get; set; }
    public TimeSpan TotalTime { get; set; }
    public double ItemsPerHour { get; set; }
    public double AccuracyPercentage { get; set; }
    public decimal TotalDistance { get; set; }
    public string PickerId { get; set; } = "";
    
    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return IsComplete;
        yield return TotalItems;
        yield return TotalTime;
        yield return ItemsPerHour;
        yield return AccuracyPercentage;
        yield return TotalDistance;
        yield return PickerId;
    }
    
    public string PerformanceGrade
    {
        get
        {
            if (!IsComplete) return "N/A";
            if (AccuracyPercentage >= 99 && ItemsPerHour >= 50) return "Excellent";
            if (AccuracyPercentage >= 95 && ItemsPerHour >= 40) return "Good";
            if (AccuracyPercentage >= 90 && ItemsPerHour >= 30) return "Average";
            return "Below Average";
        }
    }
    
    public bool MeetsTargets => AccuracyPercentage >= 95 && ItemsPerHour >= 40;
}
```

### Application Services

#### IPickingOperationsAppService
```csharp
public interface IPickingOperationsAppService : IApplicationService
{
    Task<PickingSessionDto> CreateSessionAsync(CreatePickingSessionDto input);
    Task<PickingSessionDto> GetSessionAsync(Guid sessionId);
    Task<PagedResultDto<PickingSessionDto>> GetSessionsAsync(GetPickingSessionsQuery query);
    Task<PickingSessionDto> AddOrderToSessionAsync(Guid sessionId, AddOrderToSessionDto input);
    Task<PickingSessionDto> OptimizePickingRouteAsync(Guid sessionId);
    
    Task<PickingSessionDto> StartSessionAsync(Guid sessionId, string pickerId);
    Task<PickingSessionDto> PickItemAsync(Guid sessionId, PickItemDto input);
    Task<PickingSessionDto> SkipItemAsync(Guid sessionId, Guid itemId, string reason);
    Task<PickingSessionDto> CompleteSessionAsync(Guid sessionId);
    Task CancelSessionAsync(Guid sessionId, string reason);
    
    Task<List<PickingListDto>> GeneratePickingListsAsync(GeneratePickingListsDto input);
    Task<PickingListDto> GetPickingListAsync(Guid sessionId);
    Task<List<LocationStockDto>> GetLocationStockAsync(string locationCode);
    
    Task<PickingPerformanceReportDto> GetPerformanceReportAsync(string pickerId, DateTime fromDate, DateTime toDate);
    Task<List<PickingSessionSummaryDto>> GetActiveSessionsAsync();
    Task<PickingAnalyticsDto> GetPickingAnalyticsAsync(DateTime fromDate, DateTime toDate);
    
    Task<List<PickingItemDto>> GetItemsByLocationAsync(string locationCode);
    Task UpdateItemLocationAsync(Guid itemId, string newLocationCode);
    Task<PickingValidationResultDto> ValidatePickingListAsync(Guid sessionId);
}
```

#### PickingOperationsAppService Implementation
```csharp
[Authorize]
public class PickingOperationsAppService : ApplicationService, IPickingOperationsAppService
{
    private readonly IPickingSessionRepository _sessionRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly IPickingRouteOptimizer _routeOptimizer;
    private readonly ILogger<PickingOperationsAppService> _logger;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public PickingOperationsAppService(
        IPickingSessionRepository sessionRepository,
        ICatalogRepository catalogRepository,
        ITransportBoxRepository transportBoxRepository,
        IPickingRouteOptimizer routeOptimizer,
        ILogger<PickingOperationsAppService> logger,
        IBackgroundJobManager backgroundJobManager)
    {
        _sessionRepository = sessionRepository;
        _catalogRepository = catalogRepository;
        _transportBoxRepository = transportBoxRepository;
        _routeOptimizer = routeOptimizer;
        _logger = logger;
        _backgroundJobManager = backgroundJobManager;
    }

    public async Task<PickingSessionDto> CreateSessionAsync(CreatePickingSessionDto input)
    {
        var session = new PickingSession
        {
            Id = Guid.NewGuid(),
            SessionNumber = await GenerateSessionNumberAsync(),
            SessionName = input.SessionName,
            Type = input.Type,
            PlannedStartDate = input.PlannedStartDate,
            WarehouseZone = input.WarehouseZone,
            Priority = input.Priority
        };

        // Add orders to session
        foreach (var orderDto in input.Orders ?? new List<CreatePickingOrderDto>())
        {
            session.AddOrder(orderDto.OrderNumber, orderDto.CustomerName, orderDto.Priority);
            
            // Add items for this order
            foreach (var itemDto in orderDto.Items ?? new List<CreatePickingItemDto>())
            {
                var locationInfo = await _catalogRepository.GetProductLocationAsync(itemDto.ProductCode);
                session.AddItem(
                    orderDto.OrderNumber,
                    itemDto.ProductCode,
                    itemDto.ProductName,
                    itemDto.Quantity,
                    locationInfo?.LocationCode ?? "UNKNOWN",
                    itemDto.LotNumber);
            }
        }

        await _sessionRepository.InsertAsync(session);
        
        _logger.LogInformation("Created picking session {SessionNumber} with {OrderCount} orders and {ItemCount} items", 
            session.SessionNumber, session.TotalOrders, session.TotalItems);

        return ObjectMapper.Map<PickingSession, PickingSessionDto>(session);
    }

    public async Task<PickingSessionDto> OptimizePickingRouteAsync(Guid sessionId)
    {
        var session = await _sessionRepository.GetAsync(sessionId);
        
        session.OptimizePickingRoute();
        await _sessionRepository.UpdateAsync(session);
        
        _logger.LogInformation("Optimized picking route for session {SessionId}", sessionId);
        
        return ObjectMapper.Map<PickingSession, PickingSessionDto>(session);
    }

    public async Task<PickingSessionDto> StartSessionAsync(Guid sessionId, string pickerId)
    {
        var session = await _sessionRepository.GetAsync(sessionId);
        
        session.StartSession(pickerId);
        await _sessionRepository.UpdateAsync(session);
        
        _logger.LogInformation("Started picking session {SessionId} by picker {PickerId}", 
            sessionId, pickerId);
        
        return ObjectMapper.Map<PickingSession, PickingSessionDto>(session);
    }

    public async Task<PickingSessionDto> PickItemAsync(Guid sessionId, PickItemDto input)
    {
        var session = await _sessionRepository.GetAsync(sessionId);
        
        session.PickItem(input.ItemId, input.PickedQuantity, CurrentUser.UserName ?? "", input.Notes);
        await _sessionRepository.UpdateAsync(session);
        
        // Update inventory
        var item = session.Items.FirstOrDefault(i => i.Id == input.ItemId);
        if (item != null)
        {
            await _catalogRepository.UpdateProductStockAsync(item.ProductCode, -input.PickedQuantity);
            
            // If picking to transport box, add item to box
            if (!string.IsNullOrEmpty(input.TransportBoxCode))
            {
                var transportBox = await _transportBoxRepository.GetBoxByCodeAsync(input.TransportBoxCode);
                if (transportBox != null)
                {
                    transportBox.AddItem(item.ProductCode, item.ProductName, input.PickedQuantity, 
                        CurrentUser.UserName ?? "");
                    await _transportBoxRepository.UpdateAsync(transportBox);
                }
            }
        }
        
        _logger.LogInformation("Picked item {ItemId} in session {SessionId}: {Quantity} units", 
            input.ItemId, sessionId, input.PickedQuantity);

        return ObjectMapper.Map<PickingSession, PickingSessionDto>(session);
    }

    public async Task<PickingSessionDto> CompleteSessionAsync(Guid sessionId)
    {
        var session = await _sessionRepository.GetAsync(sessionId);
        
        session.CompleteSession();
        await _sessionRepository.UpdateAsync(session);
        
        // Queue background job for performance analysis
        await _backgroundJobManager.EnqueueAsync<AnalyzePickingPerformanceJob>(
            new AnalyzePickingPerformanceArgs { SessionId = sessionId });
        
        _logger.LogInformation("Completed picking session {SessionId}", sessionId);
        
        return ObjectMapper.Map<PickingSession, PickingSessionDto>(session);
    }

    public async Task<List<PickingListDto>> GeneratePickingListsAsync(GeneratePickingListsDto input)
    {
        var pickingLists = new List<PickingListDto>();
        
        // Group orders by criteria (zone, priority, etc.)
        var orderGroups = await GroupOrdersForPicking(input.OrderNumbers, input.GroupingStrategy);
        
        foreach (var group in orderGroups)
        {
            var session = new PickingSession
            {
                Id = Guid.NewGuid(),
                SessionNumber = await GenerateSessionNumberAsync(),
                SessionName = $"Auto-generated picking list {DateTime.Now:yyyyMMdd-HHmm}",
                Type = input.SessionType,
                PlannedStartDate = input.PlannedStartDate,
                WarehouseZone = group.Zone
            };
            
            foreach (var orderNumber in group.OrderNumbers)
            {
                var orderItems = await _catalogRepository.GetOrderItemsAsync(orderNumber);
                var customerInfo = await _catalogRepository.GetOrderCustomerAsync(orderNumber);
                
                session.AddOrder(orderNumber, customerInfo.Name);
                
                foreach (var item in orderItems)
                {
                    var location = await _catalogRepository.GetProductLocationAsync(item.ProductCode);
                    session.AddItem(orderNumber, item.ProductCode, item.ProductName, 
                        item.Quantity, location?.LocationCode ?? "UNKNOWN");
                }
            }
            
            // Optimize route
            session.OptimizePickingRoute();
            
            await _sessionRepository.InsertAsync(session);
            
            var pickingList = ObjectMapper.Map<PickingSession, PickingListDto>(session);
            pickingLists.Add(pickingList);
        }
        
        _logger.LogInformation("Generated {Count} picking lists for {OrderCount} orders", 
            pickingLists.Count, input.OrderNumbers.Count);
        
        return pickingLists;
    }

    public async Task<PickingPerformanceReportDto> GetPerformanceReportAsync(string pickerId, DateTime fromDate, DateTime toDate)
    {
        var sessions = await _sessionRepository.GetSessionsByPickerAsync(pickerId, fromDate, toDate);
        
        var report = new PickingPerformanceReportDto
        {
            PickerId = pickerId,
            FromDate = fromDate,
            ToDate = toDate,
            TotalSessions = sessions.Count,
            CompletedSessions = sessions.Count(s => s.Status == PickingSessionStatus.Completed)
        };
        
        if (sessions.Any())
        {
            var completedSessions = sessions.Where(s => s.Status == PickingSessionStatus.Completed).ToList();
            
            report.TotalItems = completedSessions.Sum(s => s.TotalItems);
            report.TotalTime = TimeSpan.FromTicks(completedSessions.Sum(s => s.SessionDuration?.Ticks ?? 0));
            report.AverageItemsPerHour = report.TotalTime.TotalHours > 0 ? 
                report.TotalItems / report.TotalTime.TotalHours : 0;
            
            var accurateItems = completedSessions.SelectMany(s => s.Items).Count(i => i.IsAccurate);
            var totalItems = completedSessions.SelectMany(s => s.Items).Count();
            report.AccuracyPercentage = totalItems > 0 ? (accurateItems / (double)totalItems) * 100 : 100;
            
            report.SessionDetails = completedSessions.Select(s => new SessionPerformanceDto
            {
                SessionId = s.Id,
                SessionNumber = s.SessionNumber,
                Date = s.ActualStartDate?.Date ?? DateTime.Today,
                ItemCount = s.TotalItems,
                Duration = s.SessionDuration ?? TimeSpan.Zero,
                Accuracy = s.Items.Any() ? (s.Items.Count(i => i.IsAccurate) / (double)s.Items.Count) * 100 : 100
            }).ToList();
        }
        
        return report;
    }

    private async Task<string> GenerateSessionNumberAsync()
    {
        var today = DateTime.Today;
        var dailyCount = await _sessionRepository.GetDailySessionCountAsync(today);
        return $"P{today:yyyyMMdd}{(dailyCount + 1):D3}";
    }

    private async Task<List<OrderGroup>> GroupOrdersForPicking(List<string> orderNumbers, PickingGroupingStrategy strategy)
    {
        var groups = new List<OrderGroup>();
        
        // Implementation for grouping orders based on strategy
        // This would group orders by zone, priority, customer proximity, etc.
        
        return groups;
    }
}
```

### Background Jobs

#### AnalyzePickingPerformanceJob
```csharp
public class AnalyzePickingPerformanceJob : IAsyncBackgroundJob<AnalyzePickingPerformanceArgs>
{
    private readonly IPickingSessionRepository _sessionRepository;
    private readonly ILogger<AnalyzePickingPerformanceJob> _logger;

    public AnalyzePickingPerformanceJob(
        IPickingSessionRepository sessionRepository,
        ILogger<AnalyzePickingPerformanceJob> logger)
    {
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task ExecuteAsync(AnalyzePickingPerformanceArgs args)
    {
        try
        {
            var session = await _sessionRepository.GetAsync(args.SessionId);
            var metrics = session.GetPerformanceMetrics();
            
            if (!metrics.MeetsTargets)
            {
                _logger.LogWarning("Picking session {SessionId} did not meet performance targets: {Grade}", 
                    args.SessionId, metrics.PerformanceGrade);
            }
            
            // Store performance metrics for reporting
            // Implementation would save metrics to performance tracking system
            
            _logger.LogInformation("Analyzed performance for picking session {SessionId}: {Grade}", 
                args.SessionId, metrics.PerformanceGrade);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze picking performance for session {SessionId}", args.SessionId);
            throw;
        }
    }
}

public class AnalyzePickingPerformanceArgs
{
    public Guid SessionId { get; set; }
}
```

### Route Optimization

#### IPickingRouteOptimizer
```csharp
public interface IPickingRouteOptimizer
{
    OptimizedPickingRoute OptimizeRoute(List<PickingItem> items, string? warehouseZone = null);
    PickingRouteComparison CompareRoutes(List<PickingItem> items, List<string> algorithms);
    RouteOptimizationReport GenerateOptimizationReport(PickingSession session);
}
```

### Repository Interfaces

#### IPickingSessionRepository
```csharp
public interface IPickingSessionRepository : IRepository<PickingSession, Guid>
{
    Task<List<PickingSession>> GetSessionsByPickerAsync(string pickerId, DateTime fromDate, DateTime toDate);
    Task<List<PickingSession>> GetActiveSessionsAsync();
    Task<List<PickingSession>> GetSessionsByStatusAsync(PickingSessionStatus status);
    Task<int> GetDailySessionCountAsync(DateTime date);
    Task<List<PickingSession>> GetSessionsByDateRangeAsync(DateTime fromDate, DateTime toDate);
    Task<PagedResultDto<PickingSession>> GetPagedSessionsAsync(
        ISpecification<PickingSession> specification,
        int pageIndex,
        int pageSize,
        string sorting = null);
}
```

### Performance Requirements

#### Response Time Targets
- Session creation: < 3 seconds
- Route optimization: < 5 seconds
- Item picking: < 1 second
- Performance reports: < 10 seconds

#### Scalability
- Support 500+ concurrent picking sessions
- Handle 10,000+ items per session
- Process 1000+ orders daily
- Maintain real-time inventory updates

### Happy Day Scenarios

#### Scenario 1: Batch Picking Session
```
1. Warehouse manager creates batch picking session for 20 orders
2. System optimizes picking route by location proximity
3. Picker receives optimized picking list on mobile device
4. Picker follows route, scanning items and updating quantities
5. System updates inventory in real-time
6. Completed items are sorted into transport boxes by order
7. Session is completed with performance metrics recorded
```

#### Scenario 2: Express Order Picking
```
1. High-priority order received requiring immediate picking
2. System creates express picking session
3. Available picker is notified immediately
4. Picker follows optimized route for single order
5. Items are picked and placed in expedited transport box
6. Transport box is immediately marked for shipping
7. Customer receives rapid fulfillment notification
```

#### Scenario 3: Zone-Based Picking
```
1. Manager organizes picking by warehouse zones
2. Multiple pickers assigned to different zones simultaneously
3. Each picker receives zone-specific picking list
4. Items are picked and staged at zone collection points
5. Orders are consolidated from multiple zones
6. Final quality check ensures order completeness
7. Orders proceed to packing and shipping
```

### Error Scenarios

#### Scenario 1: Item Not Found at Location
```
User: Scans item that's not at expected location
System: Shows alert "Item not found at location A-15-B"
Action: Suggest alternative locations, allow location update
```

#### Scenario 2: Quantity Variance
```
User: Picks 8 items when 10 were required
System: Shows warning "Short pick detected: 8/10"
Action: Prompt for reason, suggest backorder or alternative products
```

#### Scenario 3: Picking Session Interruption
```
User: Session interrupted due to system maintenance
System: Automatically saves current progress
Action: Resume session when system available, preserve picking state
```