# User Story: Shoptet E-commerce Integration

## Feature Description
The Shoptet E-commerce Integration feature provides seamless synchronization between the warehouse management system and Shoptet e-commerce platform. It handles order import, inventory synchronization, product catalog updates, and real-time stock level management to ensure accurate e-commerce operations and customer satisfaction.

## Business Requirements

### Primary Use Cases
1. **Order Import and Processing**: Automatically import orders from Shoptet for fulfillment
2. **Real-Time Inventory Sync**: Synchronize stock levels between warehouse and e-commerce platform
3. **Product Catalog Management**: Manage product information and availability across systems
4. **Order Status Updates**: Update order fulfillment status back to Shoptet
5. **Price and Promotion Sync**: Synchronize pricing and promotional information

### User Stories
- As an e-commerce manager, I want orders to be automatically imported so I can fulfill them efficiently
- As a warehouse manager, I want inventory levels synchronized so customers see accurate availability
- As a customer service rep, I want order status updates so I can provide accurate information to customers
- As a product manager, I want catalog synchronization so product information is consistent across platforms

## Technical Requirements

### Domain Models

#### ShoptetIntegration
```csharp
public class ShoptetIntegration : AuditedAggregateRoot<Guid>
{
    public string IntegrationName { get; set; } = "";
    public string ShoptetStoreUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Inactive;
    public DateTime? LastSyncDate { get; set; }
    public DateTime? LastOrderSync { get; set; }
    public DateTime? LastInventorySync { get; set; }
    public bool AutoOrderImport { get; set; } = true;
    public bool AutoInventorySync { get; set; } = true;
    public bool AutoStatusUpdate { get; set; } = true;
    public int SyncIntervalMinutes { get; set; } = 15;
    public string? DefaultWarehouseCode { get; set; }
    
    // Navigation Properties
    public virtual ICollection<ShoptetOrder> Orders { get; set; } = new List<ShoptetOrder>();
    public virtual ICollection<ShoptetProduct> Products { get; set; } = new List<ShoptetProduct>();
    public virtual ICollection<SyncLog> SyncLogs { get; set; } = new List<SyncLog>();
    public virtual ICollection<ErrorLog> ErrorLogs { get; set; } = new List<ErrorLog>();
    
    // Computed Properties
    public bool IsActive => Status == IntegrationStatus.Active;
    public TimeSpan? TimeSinceLastSync => LastSyncDate.HasValue ? DateTime.UtcNow - LastSyncDate.Value : null;
    public bool NeedsSync => !LastSyncDate.HasValue || TimeSinceLastSync > TimeSpan.FromMinutes(SyncIntervalMinutes);
    public int TotalOrders => Orders.Count;
    public int PendingOrders => Orders.Count(o => o.Status == ShoptetOrderStatus.Imported);
    public int TotalProducts => Products.Count;
    public int ActiveProducts => Products.Count(p => p.IsActive);
    
    // Business Methods
    public void Activate()
    {
        if (string.IsNullOrEmpty(ApiKey) || string.IsNullOrEmpty(ApiSecret))
            throw new BusinessException("API credentials are required to activate integration");
            
        Status = IntegrationStatus.Active;
        LastModificationTime = DateTime.UtcNow;
        
        LogEvent("Integration activated", SyncEventType.Activation);
    }
    
    public void Deactivate(string reason = "")
    {
        Status = IntegrationStatus.Inactive;
        LastModificationTime = DateTime.UtcNow;
        
        LogEvent($"Integration deactivated: {reason}", SyncEventType.Deactivation);
    }
    
    public void UpdateCredentials(string apiKey, string apiSecret)
    {
        ApiKey = apiKey;
        ApiSecret = apiSecret;
        LastModificationTime = DateTime.UtcNow;
        
        LogEvent("API credentials updated", SyncEventType.Configuration);
    }
    
    public void RecordSync(SyncType syncType, int recordsProcessed, int errors = 0)
    {
        LastSyncDate = DateTime.UtcNow;
        
        if (syncType == SyncType.Orders)
            LastOrderSync = DateTime.UtcNow;
        else if (syncType == SyncType.Inventory)
            LastInventorySync = DateTime.UtcNow;
            
        var syncLog = new SyncLog
        {
            Id = Guid.NewGuid(),
            IntegrationId = Id,
            SyncType = syncType,
            StartTime = DateTime.UtcNow.AddMinutes(-1), // Approximate
            EndTime = DateTime.UtcNow,
            RecordsProcessed = recordsProcessed,
            ErrorCount = errors,
            Status = errors > 0 ? SyncStatus.CompletedWithErrors : SyncStatus.Completed
        };
        
        SyncLogs.Add(syncLog);
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void LogError(string errorMessage, string? details = null, SyncType? syncType = null)
    {
        var errorLog = new ErrorLog
        {
            Id = Guid.NewGuid(),
            IntegrationId = Id,
            ErrorMessage = errorMessage,
            ErrorDetails = details,
            SyncType = syncType,
            Timestamp = DateTime.UtcNow,
            IsResolved = false
        };
        
        ErrorLogs.Add(errorLog);
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void AddOrder(string shoptetOrderId, string orderNumber, decimal totalAmount, string customerEmail, List<ShoptetOrderItemDto> items)
    {
        var existingOrder = Orders.FirstOrDefault(o => o.ShoptetOrderId == shoptetOrderId);
        if (existingOrder != null)
            throw new BusinessException($"Order {shoptetOrderId} already exists");
            
        var order = new ShoptetOrder
        {
            Id = Guid.NewGuid(),
            IntegrationId = Id,
            ShoptetOrderId = shoptetOrderId,
            OrderNumber = orderNumber,
            TotalAmount = totalAmount,
            CustomerEmail = customerEmail,
            Status = ShoptetOrderStatus.Imported,
            ImportDate = DateTime.UtcNow
        };
        
        foreach (var itemDto in items)
        {
            var orderItem = new ShoptetOrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductCode = itemDto.ProductCode,
                ProductName = itemDto.ProductName,
                Quantity = itemDto.Quantity,
                UnitPrice = itemDto.UnitPrice,
                TotalPrice = itemDto.Quantity * itemDto.UnitPrice
            };
            
            order.Items.Add(orderItem);
        }
        
        Orders.Add(order);
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void AddOrUpdateProduct(string shoptetProductId, string productCode, string productName, decimal price, int stockQuantity, bool isActive)
    {
        var existingProduct = Products.FirstOrDefault(p => p.ShoptetProductId == shoptetProductId);
        
        if (existingProduct != null)
        {
            existingProduct.UpdateProduct(productName, price, stockQuantity, isActive);
        }
        else
        {
            var product = new ShoptetProduct
            {
                Id = Guid.NewGuid(),
                IntegrationId = Id,
                ShoptetProductId = shoptetProductId,
                ProductCode = productCode,
                ProductName = productName,
                Price = price,
                StockQuantity = stockQuantity,
                IsActive = isActive,
                LastSyncDate = DateTime.UtcNow
            };
            
            Products.Add(product);
        }
        
        LastModificationTime = DateTime.UtcNow;
    }
    
    public IntegrationHealthStatus GetHealthStatus()
    {
        var recentErrors = ErrorLogs.Count(e => e.Timestamp > DateTime.UtcNow.AddHours(-24) && !e.IsResolved);
        var lastSyncAge = TimeSinceLastSync?.TotalHours ?? double.MaxValue;
        
        if (!IsActive)
            return IntegrationHealthStatus.Inactive;
            
        if (recentErrors > 10 || lastSyncAge > 2)
            return IntegrationHealthStatus.Critical;
            
        if (recentErrors > 5 || lastSyncAge > 1)
            return IntegrationHealthStatus.Warning;
            
        return IntegrationHealthStatus.Healthy;
    }
    
    private void LogEvent(string message, SyncEventType eventType)
    {
        var syncLog = new SyncLog
        {
            Id = Guid.NewGuid(),
            IntegrationId = Id,
            SyncType = SyncType.System,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            RecordsProcessed = 0,
            ErrorCount = 0,
            Status = SyncStatus.Completed,
            Notes = message
        };
        
        SyncLogs.Add(syncLog);
    }
}

public enum IntegrationStatus
{
    Inactive,
    Active,
    Error,
    Suspended
}

public enum IntegrationHealthStatus
{
    Healthy,
    Warning,
    Critical,
    Inactive
}
```

#### ShoptetOrder
```csharp
public class ShoptetOrder : AuditedEntity<Guid>
{
    public Guid IntegrationId { get; set; }
    public string ShoptetOrderId { get; set; } = "";
    public string OrderNumber { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string CustomerEmail { get; set; } = "";
    public string? CustomerName { get; set; }
    public string? ShippingAddress { get; set; }
    public ShoptetOrderStatus Status { get; set; } = ShoptetOrderStatus.Imported;
    public DateTime ImportDate { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedDate { get; set; }
    public DateTime? ShippedDate { get; set; }
    public string? TrackingNumber { get; set; }
    public string? PickingSessionId { get; set; }
    public string? TransportBoxId { get; set; }
    public string? Notes { get; set; }
    
    // Navigation Properties
    public virtual ShoptetIntegration Integration { get; set; } = null!;
    public virtual ICollection<ShoptetOrderItem> Items { get; set; } = new List<ShoptetOrderItem>();
    
    // Computed Properties
    public int ItemCount => Items.Count;
    public int TotalQuantity => Items.Sum(i => i.Quantity);
    public bool IsProcessed => Status != ShoptetOrderStatus.Imported;
    public bool IsShipped => Status == ShoptetOrderStatus.Shipped;
    public bool CanProcess => Status == ShoptetOrderStatus.Imported;
    public bool CanShip => Status == ShoptetOrderStatus.Processing;
    public TimeSpan? ProcessingTime => ProcessedDate - ImportDate;
    public TimeSpan? ShippingTime => ShippedDate - ProcessedDate;
    
    // Business Methods
    public void StartProcessing(string pickingSessionId)
    {
        if (!CanProcess)
            throw new BusinessException($"Cannot process order in {Status} status");
            
        Status = ShoptetOrderStatus.Processing;
        ProcessedDate = DateTime.UtcNow;
        PickingSessionId = pickingSessionId;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void MarkAsShipped(string trackingNumber, string? transportBoxId = null)
    {
        if (!CanShip)
            throw new BusinessException($"Cannot ship order in {Status} status");
            
        Status = ShoptetOrderStatus.Shipped;
        ShippedDate = DateTime.UtcNow;
        TrackingNumber = trackingNumber;
        TransportBoxId = transportBoxId;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void Cancel(string reason)
    {
        if (IsShipped)
            throw new BusinessException("Cannot cancel shipped orders");
            
        Status = ShoptetOrderStatus.Cancelled;
        Notes = $"Cancelled: {reason}";
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void AddNote(string note)
    {
        Notes = string.IsNullOrEmpty(Notes) ? note : $"{Notes}\n{note}";
        LastModificationTime = DateTime.UtcNow;
    }
    
    public OrderFulfillmentSummary GetFulfillmentSummary()
    {
        return new OrderFulfillmentSummary
        {
            OrderId = Id,
            OrderNumber = OrderNumber,
            Status = Status,
            ItemCount = ItemCount,
            TotalQuantity = TotalQuantity,
            TotalAmount = TotalAmount,
            ImportDate = ImportDate,
            ProcessingTime = ProcessingTime,
            ShippingTime = ShippingTime,
            IsComplete = IsShipped,
            HasIssues = Status == ShoptetOrderStatus.Error
        };
    }
}

public enum ShoptetOrderStatus
{
    Imported,
    Processing,
    Shipped,
    Delivered,
    Cancelled,
    Error
}
```

#### ShoptetOrderItem
```csharp
public class ShoptetOrderItem : Entity<Guid>
{
    public Guid OrderId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string? ProductVariant { get; set; }
    public string? Notes { get; set; }
    
    // Navigation Properties
    public virtual ShoptetOrder Order { get; set; } = null!;
    
    // Computed Properties
    public decimal CalculatedTotal => Quantity * UnitPrice;
    public bool PriceMatches => Math.Abs(TotalPrice - CalculatedTotal) < 0.01m;
}
```

#### ShoptetProduct
```csharp
public class ShoptetProduct : AuditedEntity<Guid>
{
    public Guid IntegrationId { get; set; }
    public string ShoptetProductId { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastSyncDate { get; set; } = DateTime.UtcNow;
    public DateTime? LastPriceUpdate { get; set; }
    public DateTime? LastStockUpdate { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public bool SyncEnabled { get; set; } = true;
    
    // Navigation Properties
    public virtual ShoptetIntegration Integration { get; set; } = null!;
    
    // Computed Properties
    public bool IsOutOfStock => StockQuantity <= 0;
    public bool IsLowStock => StockQuantity > 0 && StockQuantity <= 5;
    public TimeSpan TimeSinceLastSync => DateTime.UtcNow - LastSyncDate;
    public bool NeedsSync => SyncEnabled && TimeSinceLastSync > TimeSpan.FromHours(1);
    
    // Business Methods
    public void UpdateProduct(string productName, decimal price, int stockQuantity, bool isActive)
    {
        var priceChanged = Math.Abs(Price - price) > 0.01m;
        var stockChanged = StockQuantity != stockQuantity;
        
        ProductName = productName;
        Price = price;
        StockQuantity = stockQuantity;
        IsActive = isActive;
        LastSyncDate = DateTime.UtcNow;
        
        if (priceChanged)
            LastPriceUpdate = DateTime.UtcNow;
            
        if (stockChanged)
            LastStockUpdate = DateTime.UtcNow;
            
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void UpdateStock(int newQuantity)
    {
        StockQuantity = newQuantity;
        LastStockUpdate = DateTime.UtcNow;
        LastSyncDate = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void UpdatePrice(decimal newPrice)
    {
        Price = newPrice;
        LastPriceUpdate = DateTime.UtcNow;
        LastSyncDate = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
    }
    
    public void DisableSync(string reason)
    {
        SyncEnabled = false;
        Description = string.IsNullOrEmpty(Description) ? reason : $"{Description}\nSync disabled: {reason}";
        LastModificationTime = DateTime.UtcNow;
    }
}
```

#### SyncLog
```csharp
public class SyncLog : Entity<Guid>
{
    public Guid IntegrationId { get; set; }
    public SyncType SyncType { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int RecordsProcessed { get; set; }
    public int ErrorCount { get; set; }
    public SyncStatus Status { get; set; }
    public string? Notes { get; set; }
    public string? ErrorDetails { get; set; }
    
    // Navigation Properties
    public virtual ShoptetIntegration Integration { get; set; } = null!;
    
    // Computed Properties
    public TimeSpan Duration => EndTime - StartTime;
    public bool IsSuccessful => Status == SyncStatus.Completed;
    public bool HasErrors => ErrorCount > 0;
    public double RecordsPerSecond => Duration.TotalSeconds > 0 ? RecordsProcessed / Duration.TotalSeconds : 0;
}

public enum SyncType
{
    Orders,
    Inventory,
    Products,
    Prices,
    System
}

public enum SyncStatus
{
    Started,
    InProgress,
    Completed,
    CompletedWithErrors,
    Failed,
    Cancelled
}

public enum SyncEventType
{
    Activation,
    Deactivation,
    Configuration,
    Sync,
    Error
}
```

#### ErrorLog
```csharp
public class ErrorLog : Entity<Guid>
{
    public Guid IntegrationId { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string? ErrorDetails { get; set; }
    public SyncType? SyncType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsResolved { get; set; }
    public DateTime? ResolvedDate { get; set; }
    public string? ResolvedBy { get; set; }
    public string? Resolution { get; set; }
    public ErrorSeverity Severity { get; set; } = ErrorSeverity.Medium;
    
    // Navigation Properties
    public virtual ShoptetIntegration Integration { get; set; } = null!;
    
    // Business Methods
    public void Resolve(string resolvedBy, string resolution)
    {
        IsResolved = true;
        ResolvedDate = DateTime.UtcNow;
        ResolvedBy = resolvedBy;
        Resolution = resolution;
    }
    
    public void UpdateSeverity(ErrorSeverity severity)
    {
        Severity = severity;
    }
}

public enum ErrorSeverity
{
    Low,
    Medium,
    High,
    Critical
}
```

#### OrderFulfillmentSummary (Value Object)
```csharp
public class OrderFulfillmentSummary : ValueObject
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public ShoptetOrderStatus Status { get; set; }
    public int ItemCount { get; set; }
    public int TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime ImportDate { get; set; }
    public TimeSpan? ProcessingTime { get; set; }
    public TimeSpan? ShippingTime { get; set; }
    public bool IsComplete { get; set; }
    public bool HasIssues { get; set; }
    
    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return OrderId;
        yield return OrderNumber;
        yield return Status;
        yield return ItemCount;
        yield return TotalQuantity;
        yield return TotalAmount;
        yield return ImportDate;
        yield return IsComplete;
        yield return HasIssues;
    }
    
    public string FulfillmentGrade
    {
        get
        {
            if (HasIssues) return "Issues";
            if (!IsComplete) return "In Progress";
            if (ProcessingTime?.TotalHours <= 2) return "Excellent";
            if (ProcessingTime?.TotalHours <= 24) return "Good";
            return "Delayed";
        }
    }
    
    public bool MeetsSLA => ProcessingTime?.TotalHours <= 24;
}
```

### Application Services

#### IShoptetIntegrationAppService
```csharp
public interface IShoptetIntegrationAppService : IApplicationService
{
    Task<ShoptetIntegrationDto> CreateIntegrationAsync(CreateShoptetIntegrationDto input);
    Task<ShoptetIntegrationDto> GetIntegrationAsync(Guid integrationId);
    Task<List<ShoptetIntegrationDto>> GetIntegrationsAsync();
    Task<ShoptetIntegrationDto> UpdateIntegrationAsync(Guid integrationId, UpdateShoptetIntegrationDto input);
    Task DeleteIntegrationAsync(Guid integrationId);
    
    Task<ShoptetIntegrationDto> ActivateIntegrationAsync(Guid integrationId);
    Task<ShoptetIntegrationDto> DeactivateIntegrationAsync(Guid integrationId, string reason);
    Task<ShoptetIntegrationDto> UpdateCredentialsAsync(Guid integrationId, UpdateCredentialsDto input);
    
    Task<SyncResultDto> SyncOrdersAsync(Guid integrationId, bool forceSync = false);
    Task<SyncResultDto> SyncInventoryAsync(Guid integrationId, bool forceSync = false);
    Task<SyncResultDto> SyncProductsAsync(Guid integrationId, bool forceSync = false);
    Task<SyncResultDto> SyncPricesAsync(Guid integrationId, bool forceSync = false);
    Task<SyncResultDto> FullSyncAsync(Guid integrationId);
    
    Task<PagedResultDto<ShoptetOrderDto>> GetOrdersAsync(Guid integrationId, GetShoptetOrdersQuery query);
    Task<ShoptetOrderDto> GetOrderAsync(Guid orderId);
    Task<ShoptetOrderDto> ProcessOrderAsync(Guid orderId, ProcessOrderDto input);
    Task<ShoptetOrderDto> ShipOrderAsync(Guid orderId, ShipOrderDto input);
    Task<ShoptetOrderDto> CancelOrderAsync(Guid orderId, string reason);
    
    Task<PagedResultDto<ShoptetProductDto>> GetProductsAsync(Guid integrationId, GetShoptetProductsQuery query);
    Task<ShoptetProductDto> UpdateProductStockAsync(Guid productId, int newQuantity);
    Task<ShoptetProductDto> UpdateProductPriceAsync(Guid productId, decimal newPrice);
    Task<ShoptetProductDto> ToggleProductSyncAsync(Guid productId, bool enabled);
    
    Task<List<SyncLogDto>> GetSyncHistoryAsync(Guid integrationId, int days = 30);
    Task<List<ErrorLogDto>> GetErrorsAsync(Guid integrationId, bool unresolvedOnly = true);
    Task ResolveErrorAsync(Guid errorId, string resolution);
    
    Task<IntegrationHealthReportDto> GetHealthReportAsync(Guid integrationId);
    Task<OrderFulfillmentReportDto> GetFulfillmentReportAsync(Guid integrationId, DateTime fromDate, DateTime toDate);
    Task<InventorySyncReportDto> GetInventorySyncReportAsync(Guid integrationId);
}
```

#### ShoptetIntegrationAppService Implementation
```csharp
[Authorize]
public class ShoptetIntegrationAppService : ApplicationService, IShoptetIntegrationAppService
{
    private readonly IShoptetIntegrationRepository _integrationRepository;
    private readonly IShoptetApiService _shoptetApiService;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IPickingSessionRepository _pickingSessionRepository;
    private readonly ILogger<ShoptetIntegrationAppService> _logger;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public ShoptetIntegrationAppService(
        IShoptetIntegrationRepository integrationRepository,
        IShoptetApiService shoptetApiService,
        ICatalogRepository catalogRepository,
        IPickingSessionRepository pickingSessionRepository,
        ILogger<ShoptetIntegrationAppService> logger,
        IBackgroundJobManager backgroundJobManager)
    {
        _integrationRepository = integrationRepository;
        _shoptetApiService = shoptetApiService;
        _catalogRepository = catalogRepository;
        _pickingSessionRepository = pickingSessionRepository;
        _logger = logger;
        _backgroundJobManager = backgroundJobManager;
    }

    public async Task<ShoptetIntegrationDto> CreateIntegrationAsync(CreateShoptetIntegrationDto input)
    {
        var integration = new ShoptetIntegration
        {
            Id = Guid.NewGuid(),
            IntegrationName = input.IntegrationName,
            ShoptetStoreUrl = input.ShoptetStoreUrl,
            ApiKey = input.ApiKey,
            ApiSecret = input.ApiSecret,
            AutoOrderImport = input.AutoOrderImport,
            AutoInventorySync = input.AutoInventorySync,
            AutoStatusUpdate = input.AutoStatusUpdate,
            SyncIntervalMinutes = input.SyncIntervalMinutes,
            DefaultWarehouseCode = input.DefaultWarehouseCode
        };

        await _integrationRepository.InsertAsync(integration);
        
        _logger.LogInformation("Created Shoptet integration {IntegrationName} for store {StoreUrl}", 
            integration.IntegrationName, integration.ShoptetStoreUrl);

        return ObjectMapper.Map<ShoptetIntegration, ShoptetIntegrationDto>(integration);
    }

    public async Task<ShoptetIntegrationDto> ActivateIntegrationAsync(Guid integrationId)
    {
        var integration = await _integrationRepository.GetAsync(integrationId);
        
        // Test API connection
        var connectionTest = await _shoptetApiService.TestConnectionAsync(integration.ApiKey, integration.ApiSecret);
        if (!connectionTest.IsSuccessful)
            throw new BusinessException($"Cannot activate integration: {connectionTest.ErrorMessage}");
        
        integration.Activate();
        await _integrationRepository.UpdateAsync(integration);
        
        // Queue initial sync jobs
        await _backgroundJobManager.EnqueueAsync<SyncShoptetOrdersJob>(
            new SyncShoptetOrdersArgs { IntegrationId = integrationId });
        await _backgroundJobManager.EnqueueAsync<SyncShoptetInventoryJob>(
            new SyncShoptetInventoryArgs { IntegrationId = integrationId });
        
        _logger.LogInformation("Activated Shoptet integration {IntegrationId}", integrationId);

        return ObjectMapper.Map<ShoptetIntegration, ShoptetIntegrationDto>(integration);
    }

    public async Task<SyncResultDto> SyncOrdersAsync(Guid integrationId, bool forceSync = false)
    {
        var integration = await _integrationRepository.GetAsync(integrationId);
        
        if (!integration.IsActive)
            throw new BusinessException("Integration is not active");
        
        if (!forceSync && !integration.NeedsSync)
            return new SyncResultDto { Message = "Sync not needed", RecordsProcessed = 0 };

        var startTime = DateTime.UtcNow;
        var ordersProcessed = 0;
        var errors = 0;

        try
        {
            _logger.LogInformation("Starting order sync for integration {IntegrationId}", integrationId);
            
            var lastSync = integration.LastOrderSync ?? DateTime.UtcNow.AddDays(-7);
            var newOrders = await _shoptetApiService.GetOrdersSinceAsync(integration.ApiKey, integration.ApiSecret, lastSync);
            
            foreach (var orderData in newOrders)
            {
                try
                {
                    integration.AddOrder(
                        orderData.ShoptetOrderId,
                        orderData.OrderNumber,
                        orderData.TotalAmount,
                        orderData.CustomerEmail,
                        orderData.Items);
                    
                    ordersProcessed++;
                }
                catch (Exception ex)
                {
                    errors++;
                    integration.LogError($"Failed to import order {orderData.OrderNumber}", ex.Message, SyncType.Orders);
                    _logger.LogError(ex, "Failed to import order {OrderNumber}", orderData.OrderNumber);
                }
            }
            
            integration.RecordSync(SyncType.Orders, ordersProcessed, errors);
            await _integrationRepository.UpdateAsync(integration);
            
            _logger.LogInformation("Completed order sync for integration {IntegrationId}: {Orders} orders, {Errors} errors", 
                integrationId, ordersProcessed, errors);

            return new SyncResultDto
            {
                SyncType = SyncType.Orders,
                RecordsProcessed = ordersProcessed,
                Errors = errors,
                Duration = DateTime.UtcNow - startTime,
                Message = $"Processed {ordersProcessed} orders with {errors} errors"
            };
        }
        catch (Exception ex)
        {
            integration.LogError("Order sync failed", ex.Message, SyncType.Orders);
            await _integrationRepository.UpdateAsync(integration);
            
            _logger.LogError(ex, "Order sync failed for integration {IntegrationId}", integrationId);
            throw;
        }
    }

    public async Task<SyncResultDto> SyncInventoryAsync(Guid integrationId, bool forceSync = false)
    {
        var integration = await _integrationRepository.GetAsync(integrationId);
        
        if (!integration.IsActive)
            throw new BusinessException("Integration is not active");

        var startTime = DateTime.UtcNow;
        var productsUpdated = 0;
        var errors = 0;

        try
        {
            _logger.LogInformation("Starting inventory sync for integration {IntegrationId}", integrationId);
            
            // Get current warehouse inventory
            var warehouseInventory = await _catalogRepository.GetWarehouseInventoryAsync(integration.DefaultWarehouseCode);
            
            foreach (var inventoryItem in warehouseInventory)
            {
                try
                {
                    var shoptetProduct = integration.Products.FirstOrDefault(p => p.ProductCode == inventoryItem.ProductCode);
                    if (shoptetProduct?.SyncEnabled == true)
                    {
                        // Update stock in Shoptet
                        await _shoptetApiService.UpdateProductStockAsync(
                            integration.ApiKey, 
                            integration.ApiSecret,
                            shoptetProduct.ShoptetProductId, 
                            inventoryItem.AvailableQuantity);
                        
                        shoptetProduct.UpdateStock(inventoryItem.AvailableQuantity);
                        productsUpdated++;
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    integration.LogError($"Failed to sync inventory for {inventoryItem.ProductCode}", ex.Message, SyncType.Inventory);
                    _logger.LogError(ex, "Failed to sync inventory for product {ProductCode}", inventoryItem.ProductCode);
                }
            }
            
            integration.RecordSync(SyncType.Inventory, productsUpdated, errors);
            await _integrationRepository.UpdateAsync(integration);
            
            _logger.LogInformation("Completed inventory sync for integration {IntegrationId}: {Products} products, {Errors} errors", 
                integrationId, productsUpdated, errors);

            return new SyncResultDto
            {
                SyncType = SyncType.Inventory,
                RecordsProcessed = productsUpdated,
                Errors = errors,
                Duration = DateTime.UtcNow - startTime,
                Message = $"Updated {productsUpdated} products with {errors} errors"
            };
        }
        catch (Exception ex)
        {
            integration.LogError("Inventory sync failed", ex.Message, SyncType.Inventory);
            await _integrationRepository.UpdateAsync(integration);
            
            _logger.LogError(ex, "Inventory sync failed for integration {IntegrationId}", integrationId);
            throw;
        }
    }

    public async Task<ShoptetOrderDto> ProcessOrderAsync(Guid orderId, ProcessOrderDto input)
    {
        var order = await GetShoptetOrderAsync(orderId);
        
        order.StartProcessing(input.PickingSessionId);
        await _integrationRepository.UpdateAsync(order.Integration);
        
        // Create picking session if needed
        if (!string.IsNullOrEmpty(input.PickingSessionId))
        {
            await CreatePickingSessionForOrder(order);
        }
        
        // Update status in Shoptet
        await _shoptetApiService.UpdateOrderStatusAsync(
            order.Integration.ApiKey,
            order.Integration.ApiSecret,
            order.ShoptetOrderId,
            "processing");
        
        _logger.LogInformation("Started processing Shoptet order {OrderNumber}", order.OrderNumber);

        return ObjectMapper.Map<ShoptetOrder, ShoptetOrderDto>(order);
    }

    public async Task<ShoptetOrderDto> ShipOrderAsync(Guid orderId, ShipOrderDto input)
    {
        var order = await GetShoptetOrderAsync(orderId);
        
        order.MarkAsShipped(input.TrackingNumber, input.TransportBoxId);
        await _integrationRepository.UpdateAsync(order.Integration);
        
        // Update status in Shoptet with tracking number
        await _shoptetApiService.UpdateOrderStatusAsync(
            order.Integration.ApiKey,
            order.Integration.ApiSecret,
            order.ShoptetOrderId,
            "shipped",
            input.TrackingNumber);
        
        _logger.LogInformation("Marked Shoptet order {OrderNumber} as shipped with tracking {TrackingNumber}", 
            order.OrderNumber, input.TrackingNumber);

        return ObjectMapper.Map<ShoptetOrder, ShoptetOrderDto>(order);
    }

    public async Task<IntegrationHealthReportDto> GetHealthReportAsync(Guid integrationId)
    {
        var integration = await _integrationRepository.GetAsync(integrationId);
        
        var report = new IntegrationHealthReportDto
        {
            IntegrationId = integrationId,
            IntegrationName = integration.IntegrationName,
            Status = integration.Status,
            HealthStatus = integration.GetHealthStatus(),
            LastSyncDate = integration.LastSyncDate,
            TimeSinceLastSync = integration.TimeSinceLastSync,
            TotalOrders = integration.TotalOrders,
            PendingOrders = integration.PendingOrders,
            TotalProducts = integration.TotalProducts,
            ActiveProducts = integration.ActiveProducts
        };
        
        // Get recent sync statistics
        var recentSyncs = integration.SyncLogs
            .Where(s => s.StartTime > DateTime.UtcNow.AddDays(-7))
            .OrderByDescending(s => s.StartTime)
            .Take(10)
            .ToList();
            
        report.RecentSyncSummary = recentSyncs.Select(s => new SyncSummaryDto
        {
            SyncType = s.SyncType,
            StartTime = s.StartTime,
            Duration = s.Duration,
            RecordsProcessed = s.RecordsProcessed,
            ErrorCount = s.ErrorCount,
            Status = s.Status
        }).ToList();
        
        // Get unresolved errors
        report.UnresolvedErrors = integration.ErrorLogs
            .Where(e => !e.IsResolved)
            .OrderByDescending(e => e.Timestamp)
            .Take(5)
            .Select(e => new ErrorSummaryDto
            {
                ErrorMessage = e.ErrorMessage,
                Timestamp = e.Timestamp,
                Severity = e.Severity,
                SyncType = e.SyncType
            }).ToList();
        
        return report;
    }

    private async Task<ShoptetOrder> GetShoptetOrderAsync(Guid orderId)
    {
        var integrations = await _integrationRepository.GetListAsync();
        var order = integrations.SelectMany(i => i.Orders).FirstOrDefault(o => o.Id == orderId);
        
        if (order == null)
            throw new EntityNotFoundException($"Shoptet order {orderId} not found");
            
        return order;
    }

    private async Task CreatePickingSessionForOrder(ShoptetOrder order)
    {
        // Implementation to create picking session from Shoptet order
        // This would integrate with the picking operations system
    }
}
```

### External API Service

#### IShoptetApiService
```csharp
public interface IShoptetApiService
{
    Task<ApiConnectionResult> TestConnectionAsync(string apiKey, string apiSecret);
    Task<List<ShoptetOrderData>> GetOrdersSinceAsync(string apiKey, string apiSecret, DateTime since);
    Task<List<ShoptetProductData>> GetProductsAsync(string apiKey, string apiSecret);
    Task UpdateProductStockAsync(string apiKey, string apiSecret, string productId, int quantity);
    Task UpdateProductPriceAsync(string apiKey, string apiSecret, string productId, decimal price);
    Task UpdateOrderStatusAsync(string apiKey, string apiSecret, string orderId, string status, string? trackingNumber = null);
    Task<ShoptetOrderData> GetOrderAsync(string apiKey, string apiSecret, string orderId);
    Task<ShoptetProductData> GetProductAsync(string apiKey, string apiSecret, string productId);
}
```

### Background Jobs

#### SyncShoptetOrdersJob
```csharp
public class SyncShoptetOrdersJob : IAsyncBackgroundJob<SyncShoptetOrdersArgs>
{
    private readonly IShoptetIntegrationAppService _shoptetService;
    private readonly ILogger<SyncShoptetOrdersJob> _logger;

    public SyncShoptetOrdersJob(
        IShoptetIntegrationAppService shoptetService,
        ILogger<SyncShoptetOrdersJob> logger)
    {
        _shoptetService = shoptetService;
        _logger = logger;
    }

    public async Task ExecuteAsync(SyncShoptetOrdersArgs args)
    {
        try
        {
            _logger.LogInformation("Starting scheduled Shoptet order sync for integration {IntegrationId}", 
                args.IntegrationId);
            
            var result = await _shoptetService.SyncOrdersAsync(args.IntegrationId, args.ForceSync);
            
            _logger.LogInformation("Completed scheduled Shoptet order sync: {Result}", result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync Shoptet orders for integration {IntegrationId}", 
                args.IntegrationId);
            throw;
        }
    }
}

public class SyncShoptetOrdersArgs
{
    public Guid IntegrationId { get; set; }
    public bool ForceSync { get; set; } = false;
}
```

#### SyncShoptetInventoryJob
```csharp
public class SyncShoptetInventoryJob : IAsyncBackgroundJob<SyncShoptetInventoryArgs>
{
    private readonly IShoptetIntegrationAppService _shoptetService;
    private readonly ILogger<SyncShoptetInventoryJob> _logger;

    public SyncShoptetInventoryJob(
        IShoptetIntegrationAppService shoptetService,
        ILogger<SyncShoptetInventoryJob> logger)
    {
        _shoptetService = shoptetService;
        _logger = logger;
    }

    public async Task ExecuteAsync(SyncShoptetInventoryArgs args)
    {
        try
        {
            _logger.LogInformation("Starting scheduled Shoptet inventory sync for integration {IntegrationId}", 
                args.IntegrationId);
            
            var result = await _shoptetService.SyncInventoryAsync(args.IntegrationId, args.ForceSync);
            
            _logger.LogInformation("Completed scheduled Shoptet inventory sync: {Result}", result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync Shoptet inventory for integration {IntegrationId}", 
                args.IntegrationId);
            throw;
        }
    }
}

public class SyncShoptetInventoryArgs
{
    public Guid IntegrationId { get; set; }
    public bool ForceSync { get; set; } = false;
}
```

### Repository Interfaces

#### IShoptetIntegrationRepository
```csharp
public interface IShoptetIntegrationRepository : IRepository<ShoptetIntegration, Guid>
{
    Task<List<ShoptetIntegration>> GetActiveIntegrationsAsync();
    Task<ShoptetIntegration?> GetByStoreUrlAsync(string storeUrl);
    Task<List<ShoptetIntegration>> GetIntegrationsNeedingSyncAsync();
    Task<List<ShoptetOrder>> GetOrdersByStatusAsync(ShoptetOrderStatus status);
    Task<List<ShoptetProduct>> GetProductsNeedingSyncAsync();
    Task<PagedResultDto<ShoptetIntegration>> GetPagedIntegrationsAsync(
        ISpecification<ShoptetIntegration> specification,
        int pageIndex,
        int pageSize,
        string sorting = null);
}
```

### Performance Requirements

#### Response Time Targets
- Order sync: < 30 seconds for 100 orders
- Inventory sync: < 60 seconds for 1000 products
- Order processing: < 5 seconds
- Status updates: < 10 seconds

#### Scalability
- Support multiple Shoptet store integrations
- Handle 1000+ orders per day per integration
- Process 10,000+ product inventory updates
- Maintain real-time sync capabilities

### Happy Day Scenarios

#### Scenario 1: New Order Processing
```
1. Customer places order on Shoptet e-commerce site
2. Shoptet integration automatically imports order into warehouse system
3. Order is assigned to picking session and processed
4. Items are picked and packed into transport box
5. Transport box is shipped with tracking number
6. Order status and tracking info updated back to Shoptet
7. Customer receives shipment notification
```

#### Scenario 2: Inventory Synchronization
```
1. Warehouse receives new stock shipment
2. Inventory levels updated in warehouse system
3. Shoptet integration automatically syncs new quantities
4. E-commerce site reflects accurate availability
5. Customers can order with confidence in stock levels
6. Out-of-stock items automatically marked unavailable
```

#### Scenario 3: Price Update Propagation
```
1. Product manager updates pricing in warehouse system
2. Price changes flagged for e-commerce sync
3. Shoptet integration pushes new prices to online store
4. E-commerce site displays updated pricing
5. Active shopping carts updated with new prices
6. Price change history maintained for audit
```

### Error Scenarios

#### Scenario 1: API Connection Failure
```
User: Attempts to sync orders during Shoptet downtime
System: Shows error "Unable to connect to Shoptet API"
Action: Queue sync for retry, notify administrators, continue with cached data
```

#### Scenario 2: Order Import Conflict
```
User: Order already exists in system with different data
System: Shows warning "Order conflict detected"
Action: Compare data, flag for manual review, prevent duplicate processing
```

#### Scenario 3: Inventory Sync Mismatch
```
User: Warehouse quantity doesn't match e-commerce availability
System: Shows alert "Inventory discrepancy detected"
Action: Log discrepancy, trigger stock taking, maintain audit trail
```