# Test Scenarios: Shoptet E-commerce Integration

## Unit Tests

### ShoptetIntegration Aggregate Tests

#### Integration Creation and Configuration
```csharp
[Test]
public void Activate_WithValidCredentials_ShouldActivateSuccessfully()
{
    // Arrange
    var integration = new ShoptetIntegration 
    { 
        ApiKey = "valid-api-key",
        ApiSecret = "valid-api-secret",
        Status = IntegrationStatus.Inactive
    };
    
    // Act
    integration.Activate();
    
    // Assert
    Assert.That(integration.Status, Is.EqualTo(IntegrationStatus.Active));
    Assert.That(integration.IsActive, Is.True);
    Assert.That(integration.SyncLogs.Count, Is.EqualTo(1));
    Assert.That(integration.SyncLogs.First().Notes, Does.Contain("activated"));
}

[Test]
public void Activate_WithoutCredentials_ShouldThrowException()
{
    // Arrange
    var integration = new ShoptetIntegration 
    { 
        ApiKey = "",
        ApiSecret = ""
    };
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => integration.Activate());
}

[Test]
public void UpdateCredentials_ShouldUpdateAndLog()
{
    // Arrange
    var integration = new ShoptetIntegration();
    
    // Act
    integration.UpdateCredentials("new-api-key", "new-api-secret");
    
    // Assert
    Assert.That(integration.ApiKey, Is.EqualTo("new-api-key"));
    Assert.That(integration.ApiSecret, Is.EqualTo("new-api-secret"));
    Assert.That(integration.SyncLogs.Count, Is.EqualTo(1));
    Assert.That(integration.SyncLogs.First().Notes, Does.Contain("credentials updated"));
}

[Test]
public void Deactivate_ShouldUpdateStatusAndLog()
{
    // Arrange
    var integration = new ShoptetIntegration 
    { 
        Status = IntegrationStatus.Active 
    };
    
    // Act
    integration.Deactivate("Manual deactivation");
    
    // Assert
    Assert.That(integration.Status, Is.EqualTo(IntegrationStatus.Inactive));
    Assert.That(integration.IsActive, Is.False);
    Assert.That(integration.SyncLogs.First().Notes, Does.Contain("Manual deactivation"));
}
```

#### Order Management
```csharp
[Test]
public void AddOrder_WithValidData_ShouldCreateOrder()
{
    // Arrange
    var integration = new ShoptetIntegration { Id = Guid.NewGuid() };
    var items = new List<ShoptetOrderItemDto>
    {
        new() { ProductCode = "PROD001", ProductName = "Product 1", Quantity = 2, UnitPrice = 10.50m }
    };
    
    // Act
    integration.AddOrder("SHOP001", "ORD001", 21.00m, "customer@test.com", items);
    
    // Assert
    Assert.That(integration.Orders.Count, Is.EqualTo(1));
    Assert.That(integration.Orders.First().ShoptetOrderId, Is.EqualTo("SHOP001"));
    Assert.That(integration.Orders.First().OrderNumber, Is.EqualTo("ORD001"));
    Assert.That(integration.Orders.First().Items.Count, Is.EqualTo(1));
    Assert.That(integration.TotalOrders, Is.EqualTo(1));
    Assert.That(integration.PendingOrders, Is.EqualTo(1));
}

[Test]
public void AddOrder_WithDuplicateShoptetId_ShouldThrowException()
{
    // Arrange
    var integration = new ShoptetIntegration { Id = Guid.NewGuid() };
    var items = new List<ShoptetOrderItemDto>
    {
        new() { ProductCode = "PROD001", ProductName = "Product 1", Quantity = 1, UnitPrice = 10m }
    };
    
    integration.AddOrder("SHOP001", "ORD001", 10m, "customer@test.com", items);
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        integration.AddOrder("SHOP001", "ORD002", 20m, "customer2@test.com", items));
}
```

#### Product Management
```csharp
[Test]
public void AddOrUpdateProduct_NewProduct_ShouldCreateProduct()
{
    // Arrange
    var integration = new ShoptetIntegration { Id = Guid.NewGuid() };
    
    // Act
    integration.AddOrUpdateProduct("SHOP-PROD001", "PROD001", "Product 1", 25.50m, 100, true);
    
    // Assert
    Assert.That(integration.Products.Count, Is.EqualTo(1));
    Assert.That(integration.Products.First().ShoptetProductId, Is.EqualTo("SHOP-PROD001"));
    Assert.That(integration.Products.First().ProductCode, Is.EqualTo("PROD001"));
    Assert.That(integration.Products.First().Price, Is.EqualTo(25.50m));
    Assert.That(integration.Products.First().StockQuantity, Is.EqualTo(100));
    Assert.That(integration.TotalProducts, Is.EqualTo(1));
    Assert.That(integration.ActiveProducts, Is.EqualTo(1));
}

[Test]
public void AddOrUpdateProduct_ExistingProduct_ShouldUpdateProduct()
{
    // Arrange
    var integration = new ShoptetIntegration { Id = Guid.NewGuid() };
    integration.AddOrUpdateProduct("SHOP-PROD001", "PROD001", "Product 1", 25.50m, 100, true);
    
    // Act
    integration.AddOrUpdateProduct("SHOP-PROD001", "PROD001", "Updated Product 1", 30.00m, 80, true);
    
    // Assert
    Assert.That(integration.Products.Count, Is.EqualTo(1)); // Still one product
    Assert.That(integration.Products.First().ProductName, Is.EqualTo("Updated Product 1"));
    Assert.That(integration.Products.First().Price, Is.EqualTo(30.00m));
    Assert.That(integration.Products.First().StockQuantity, Is.EqualTo(80));
}
```

#### Sync Logging and Error Handling
```csharp
[Test]
public void RecordSync_ShouldUpdateSyncDatesAndCreateLog()
{
    // Arrange
    var integration = new ShoptetIntegration { Id = Guid.NewGuid() };
    
    // Act
    integration.RecordSync(SyncType.Orders, 10, 2);
    
    // Assert
    Assert.That(integration.LastSyncDate, Is.Not.Null);
    Assert.That(integration.LastOrderSync, Is.Not.Null);
    Assert.That(integration.SyncLogs.Count, Is.EqualTo(1));
    Assert.That(integration.SyncLogs.First().SyncType, Is.EqualTo(SyncType.Orders));
    Assert.That(integration.SyncLogs.First().RecordsProcessed, Is.EqualTo(10));
    Assert.That(integration.SyncLogs.First().ErrorCount, Is.EqualTo(2));
    Assert.That(integration.SyncLogs.First().Status, Is.EqualTo(SyncStatus.CompletedWithErrors));
}

[Test]
public void LogError_ShouldCreateErrorLog()
{
    // Arrange
    var integration = new ShoptetIntegration { Id = Guid.NewGuid() };
    
    // Act
    integration.LogError("API connection failed", "Timeout after 30 seconds", SyncType.Orders);
    
    // Assert
    Assert.That(integration.ErrorLogs.Count, Is.EqualTo(1));
    Assert.That(integration.ErrorLogs.First().ErrorMessage, Is.EqualTo("API connection failed"));
    Assert.That(integration.ErrorLogs.First().ErrorDetails, Is.EqualTo("Timeout after 30 seconds"));
    Assert.That(integration.ErrorLogs.First().SyncType, Is.EqualTo(SyncType.Orders));
    Assert.That(integration.ErrorLogs.First().IsResolved, Is.False);
}

[Test]
public void NeedsSync_WhenSyncIntervalExceeded_ShouldReturnTrue()
{
    // Arrange
    var integration = new ShoptetIntegration 
    { 
        SyncIntervalMinutes = 15,
        LastSyncDate = DateTime.UtcNow.AddMinutes(-20) // 20 minutes ago
    };
    
    // Act & Assert
    Assert.That(integration.NeedsSync, Is.True);
}

[Test]
public void NeedsSync_WhenWithinSyncInterval_ShouldReturnFalse()
{
    // Arrange
    var integration = new ShoptetIntegration 
    { 
        SyncIntervalMinutes = 15,
        LastSyncDate = DateTime.UtcNow.AddMinutes(-10) // 10 minutes ago
    };
    
    // Act & Assert
    Assert.That(integration.NeedsSync, Is.False);
}
```

#### Health Status Assessment
```csharp
[Test]
public void GetHealthStatus_WhenActive_ShouldReturnHealthy()
{
    // Arrange
    var integration = new ShoptetIntegration 
    { 
        Status = IntegrationStatus.Active,
        LastSyncDate = DateTime.UtcNow.AddMinutes(-30)
    };
    
    // Act
    var health = integration.GetHealthStatus();
    
    // Assert
    Assert.That(health, Is.EqualTo(IntegrationHealthStatus.Healthy));
}

[Test]
public void GetHealthStatus_WhenInactive_ShouldReturnInactive()
{
    // Arrange
    var integration = new ShoptetIntegration 
    { 
        Status = IntegrationStatus.Inactive 
    };
    
    // Act
    var health = integration.GetHealthStatus();
    
    // Assert
    Assert.That(health, Is.EqualTo(IntegrationHealthStatus.Inactive));
}

[Test]
public void GetHealthStatus_WithManyRecentErrors_ShouldReturnCritical()
{
    // Arrange
    var integration = new ShoptetIntegration 
    { 
        Status = IntegrationStatus.Active,
        LastSyncDate = DateTime.UtcNow.AddMinutes(-30)
    };
    
    // Add 15 recent errors
    for (int i = 0; i < 15; i++)
    {
        integration.LogError($"Error {i}", null, SyncType.Orders);
    }
    
    // Act
    var health = integration.GetHealthStatus();
    
    // Assert
    Assert.That(health, Is.EqualTo(IntegrationHealthStatus.Critical));
}

[Test]
public void GetHealthStatus_WithOldSync_ShouldReturnCritical()
{
    // Arrange
    var integration = new ShoptetIntegration 
    { 
        Status = IntegrationStatus.Active,
        LastSyncDate = DateTime.UtcNow.AddHours(-3) // 3 hours ago
    };
    
    // Act
    var health = integration.GetHealthStatus();
    
    // Assert
    Assert.That(health, Is.EqualTo(IntegrationHealthStatus.Critical));
}
```

### ShoptetOrder Entity Tests

#### Order Status Management
```csharp
[Test]
public void StartProcessing_WhenImported_ShouldStartSuccessfully()
{
    // Arrange
    var order = new ShoptetOrder 
    { 
        Status = ShoptetOrderStatus.Imported 
    };
    
    // Act
    order.StartProcessing("PICKING-SESSION-001");
    
    // Assert
    Assert.That(order.Status, Is.EqualTo(ShoptetOrderStatus.Processing));
    Assert.That(order.PickingSessionId, Is.EqualTo("PICKING-SESSION-001"));
    Assert.That(order.ProcessedDate, Is.Not.Null);
    Assert.That(order.IsProcessed, Is.True);
}

[Test]
public void StartProcessing_WhenNotImported_ShouldThrowException()
{
    // Arrange
    var order = new ShoptetOrder 
    { 
        Status = ShoptetOrderStatus.Processing 
    };
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        order.StartProcessing("PICKING-SESSION-001"));
}

[Test]
public void MarkAsShipped_WhenProcessing_ShouldShipSuccessfully()
{
    // Arrange
    var order = new ShoptetOrder 
    { 
        Status = ShoptetOrderStatus.Processing 
    };
    
    // Act
    order.MarkAsShipped("TRACK123456", "BOX001");
    
    // Assert
    Assert.That(order.Status, Is.EqualTo(ShoptetOrderStatus.Shipped));
    Assert.That(order.TrackingNumber, Is.EqualTo("TRACK123456"));
    Assert.That(order.TransportBoxId, Is.EqualTo("BOX001"));
    Assert.That(order.ShippedDate, Is.Not.Null);
    Assert.That(order.IsShipped, Is.True);
}

[Test]
public void Cancel_WhenNotShipped_ShouldCancelSuccessfully()
{
    // Arrange
    var order = new ShoptetOrder 
    { 
        Status = ShoptetOrderStatus.Processing 
    };
    
    // Act
    order.Cancel("Customer requested cancellation");
    
    // Assert
    Assert.That(order.Status, Is.EqualTo(ShoptetOrderStatus.Cancelled));
    Assert.That(order.Notes, Does.Contain("Customer requested cancellation"));
}

[Test]
public void Cancel_WhenShipped_ShouldThrowException()
{
    // Arrange
    var order = new ShoptetOrder 
    { 
        Status = ShoptetOrderStatus.Shipped 
    };
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        order.Cancel("Too late"));
}
```

#### Order Metrics and Analysis
```csharp
[Test]
public void GetFulfillmentSummary_ShouldCalculateCorrectly()
{
    // Arrange
    var order = new ShoptetOrder 
    { 
        Id = Guid.NewGuid(),
        OrderNumber = "ORD001",
        Status = ShoptetOrderStatus.Shipped,
        TotalAmount = 150.50m,
        ImportDate = DateTime.UtcNow.AddHours(-3),
        ProcessedDate = DateTime.UtcNow.AddHours(-2),
        ShippedDate = DateTime.UtcNow.AddHours(-1)
    };
    
    order.Items.Add(new ShoptetOrderItem { Quantity = 2 });
    order.Items.Add(new ShoptetOrderItem { Quantity = 3 });
    
    // Act
    var summary = order.GetFulfillmentSummary();
    
    // Assert
    Assert.That(summary.OrderNumber, Is.EqualTo("ORD001"));
    Assert.That(summary.Status, Is.EqualTo(ShoptetOrderStatus.Shipped));
    Assert.That(summary.ItemCount, Is.EqualTo(2));
    Assert.That(summary.TotalQuantity, Is.EqualTo(5));
    Assert.That(summary.TotalAmount, Is.EqualTo(150.50m));
    Assert.That(summary.IsComplete, Is.True);
    Assert.That(summary.ProcessingTime, Is.Not.Null);
    Assert.That(summary.ShippingTime, Is.Not.Null);
}

[Test]
public void ProcessingTime_ShouldCalculateCorrectly()
{
    // Arrange
    var order = new ShoptetOrder 
    { 
        ImportDate = DateTime.UtcNow.AddHours(-5),
        ProcessedDate = DateTime.UtcNow.AddHours(-3)
    };
    
    // Act
    var processingTime = order.ProcessingTime;
    
    // Assert
    Assert.That(processingTime, Is.Not.Null);
    Assert.That(processingTime.Value.TotalHours, Is.EqualTo(2).Within(0.1));
}

[Test]
public void AddNote_ShouldAppendToExistingNotes()
{
    // Arrange
    var order = new ShoptetOrder 
    { 
        Notes = "Initial note" 
    };
    
    // Act
    order.AddNote("Additional note");
    
    // Assert
    Assert.That(order.Notes, Is.EqualTo("Initial note\nAdditional note"));
}
```

### ShoptetProduct Entity Tests

#### Product Updates
```csharp
[Test]
public void UpdateProduct_ShouldUpdateAllFields()
{
    // Arrange
    var product = new ShoptetProduct 
    { 
        ProductName = "Old Name",
        Price = 10.00m,
        StockQuantity = 50,
        IsActive = true
    };
    
    // Act
    product.UpdateProduct("New Name", 15.00m, 75, false);
    
    // Assert
    Assert.That(product.ProductName, Is.EqualTo("New Name"));
    Assert.That(product.Price, Is.EqualTo(15.00m));
    Assert.That(product.StockQuantity, Is.EqualTo(75));
    Assert.That(product.IsActive, Is.False);
    Assert.That(product.LastSyncDate, Is.Not.Null);
}

[Test]
public void UpdateProduct_WithPriceChange_ShouldUpdatePriceTimestamp()
{
    // Arrange
    var product = new ShoptetProduct 
    { 
        Price = 10.00m 
    };
    
    // Act
    product.UpdateProduct("Same Name", 15.00m, 50, true);
    
    // Assert
    Assert.That(product.LastPriceUpdate, Is.Not.Null);
}

[Test]
public void UpdateProduct_WithStockChange_ShouldUpdateStockTimestamp()
{
    // Arrange
    var product = new ShoptetProduct 
    { 
        StockQuantity = 50 
    };
    
    // Act
    product.UpdateProduct("Same Name", 10.00m, 75, true);
    
    // Assert
    Assert.That(product.LastStockUpdate, Is.Not.Null);
}

[Test]
public void UpdateStock_ShouldUpdateQuantityAndTimestamp()
{
    // Arrange
    var product = new ShoptetProduct 
    { 
        StockQuantity = 100 
    };
    
    // Act
    product.UpdateStock(85);
    
    // Assert
    Assert.That(product.StockQuantity, Is.EqualTo(85));
    Assert.That(product.LastStockUpdate, Is.Not.Null);
    Assert.That(product.LastSyncDate, Is.Not.Null);
}

[Test]
public void UpdatePrice_ShouldUpdatePriceAndTimestamp()
{
    // Arrange
    var product = new ShoptetProduct 
    { 
        Price = 20.00m 
    };
    
    // Act
    product.UpdatePrice(25.00m);
    
    // Assert
    Assert.That(product.Price, Is.EqualTo(25.00m));
    Assert.That(product.LastPriceUpdate, Is.Not.Null);
    Assert.That(product.LastSyncDate, Is.Not.Null);
}
```

#### Product Status and Sync Management
```csharp
[Test]
public void IsOutOfStock_WhenZeroQuantity_ShouldReturnTrue()
{
    // Arrange
    var product = new ShoptetProduct 
    { 
        StockQuantity = 0 
    };
    
    // Act & Assert
    Assert.That(product.IsOutOfStock, Is.True);
}

[Test]
public void IsLowStock_WhenBetweenOneAndFive_ShouldReturnTrue()
{
    // Arrange
    var product = new ShoptetProduct 
    { 
        StockQuantity = 3 
    };
    
    // Act & Assert
    Assert.That(product.IsLowStock, Is.True);
    Assert.That(product.IsOutOfStock, Is.False);
}

[Test]
public void NeedsSync_WhenSyncEnabledAndOld_ShouldReturnTrue()
{
    // Arrange
    var product = new ShoptetProduct 
    { 
        SyncEnabled = true,
        LastSyncDate = DateTime.UtcNow.AddHours(-2) // 2 hours ago
    };
    
    // Act & Assert
    Assert.That(product.NeedsSync, Is.True);
}

[Test]
public void DisableSync_ShouldUpdateSyncStatus()
{
    // Arrange
    var product = new ShoptetProduct 
    { 
        SyncEnabled = true 
    };
    
    // Act
    product.DisableSync("Product discontinued");
    
    // Assert
    Assert.That(product.SyncEnabled, Is.False);
    Assert.That(product.Description, Does.Contain("Product discontinued"));
}
```

### ErrorLog Entity Tests

#### Error Resolution
```csharp
[Test]
public void Resolve_ShouldUpdateResolutionInfo()
{
    // Arrange
    var error = new ErrorLog 
    { 
        IsResolved = false 
    };
    
    // Act
    error.Resolve("ADMIN001", "API endpoint updated, issue resolved");
    
    // Assert
    Assert.That(error.IsResolved, Is.True);
    Assert.That(error.ResolvedBy, Is.EqualTo("ADMIN001"));
    Assert.That(error.Resolution, Is.EqualTo("API endpoint updated, issue resolved"));
    Assert.That(error.ResolvedDate, Is.Not.Null);
}

[Test]
public void UpdateSeverity_ShouldUpdateSeverityLevel()
{
    // Arrange
    var error = new ErrorLog 
    { 
        Severity = ErrorSeverity.Medium 
    };
    
    // Act
    error.UpdateSeverity(ErrorSeverity.Critical);
    
    // Assert
    Assert.That(error.Severity, Is.EqualTo(ErrorSeverity.Critical));
}
```

### OrderFulfillmentSummary Value Object Tests

#### Performance Grading
```csharp
[Test]
public void FulfillmentGrade_WithFastProcessing_ShouldBeExcellent()
{
    // Arrange
    var summary = new OrderFulfillmentSummary
    {
        IsComplete = true,
        HasIssues = false,
        ProcessingTime = TimeSpan.FromHours(1) // 1 hour
    };
    
    // Act & Assert
    Assert.That(summary.FulfillmentGrade, Is.EqualTo("Excellent"));
    Assert.That(summary.MeetsSLA, Is.True);
}

[Test]
public void FulfillmentGrade_WithSlowProcessing_ShouldBeDelayed()
{
    // Arrange
    var summary = new OrderFulfillmentSummary
    {
        IsComplete = true,
        HasIssues = false,
        ProcessingTime = TimeSpan.FromHours(36) // 36 hours
    };
    
    // Act & Assert
    Assert.That(summary.FulfillmentGrade, Is.EqualTo("Delayed"));
    Assert.That(summary.MeetsSLA, Is.False);
}

[Test]
public void FulfillmentGrade_WithIssues_ShouldShowIssues()
{
    // Arrange
    var summary = new OrderFulfillmentSummary
    {
        IsComplete = false,
        HasIssues = true
    };
    
    // Act & Assert
    Assert.That(summary.FulfillmentGrade, Is.EqualTo("Issues"));
}
```

## Integration Tests

### Application Service Tests

#### Integration Management
```csharp
[Test]
public async Task CreateIntegrationAsync_WithValidInput_ShouldCreateIntegration()
{
    // Arrange
    var input = new CreateShoptetIntegrationDto
    {
        IntegrationName = "Test Store Integration",
        ShoptetStoreUrl = "https://teststore.myshoptet.com",
        ApiKey = "test-api-key",
        ApiSecret = "test-api-secret",
        AutoOrderImport = true,
        AutoInventorySync = true,
        SyncIntervalMinutes = 30,
        DefaultWarehouseCode = "WH001"
    };
    
    // Act
    var result = await _shoptetService.CreateIntegrationAsync(input);
    
    // Assert
    Assert.That(result.IntegrationName, Is.EqualTo("Test Store Integration"));
    Assert.That(result.ShoptetStoreUrl, Is.EqualTo("https://teststore.myshoptet.com"));
    Assert.That(result.Status, Is.EqualTo(IntegrationStatus.Inactive));
    Assert.That(result.AutoOrderImport, Is.True);
}

[Test]
public async Task ActivateIntegrationAsync_WithValidCredentials_ShouldActivateSuccessfully()
{
    // Arrange
    var integration = await CreateTestIntegration();
    
    // Mock successful API connection test
    _mockShoptetApiService
        .Setup(x => x.TestConnectionAsync(It.IsAny<string>(), It.IsAny<string>()))
        .ReturnsAsync(new ApiConnectionResult { IsSuccessful = true });
    
    // Act
    var result = await _shoptetService.ActivateIntegrationAsync(integration.Id);
    
    // Assert
    Assert.That(result.Status, Is.EqualTo(IntegrationStatus.Active));
    
    // Verify background jobs were queued
    _mockBackgroundJobManager.Verify(x => x.EnqueueAsync<SyncShoptetOrdersJob>(
        It.IsAny<SyncShoptetOrdersArgs>()), Times.Once);
    _mockBackgroundJobManager.Verify(x => x.EnqueueAsync<SyncShoptetInventoryJob>(
        It.IsAny<SyncShoptetInventoryArgs>()), Times.Once);
}

[Test]
public async Task ActivateIntegrationAsync_WithInvalidCredentials_ShouldThrowException()
{
    // Arrange
    var integration = await CreateTestIntegration();
    
    // Mock failed API connection test
    _mockShoptetApiService
        .Setup(x => x.TestConnectionAsync(It.IsAny<string>(), It.IsAny<string>()))
        .ReturnsAsync(new ApiConnectionResult { IsSuccessful = false, ErrorMessage = "Invalid credentials" });
    
    // Act & Assert
    var exception = await Assert.ThrowsAsync<BusinessException>(() => 
        _shoptetService.ActivateIntegrationAsync(integration.Id));
    
    Assert.That(exception.Message, Does.Contain("Invalid credentials"));
}
```

#### Order Synchronization
```csharp
[Test]
public async Task SyncOrdersAsync_WithNewOrders_ShouldImportSuccessfully()
{
    // Arrange
    var integration = await CreateActiveIntegration();
    
    var mockOrders = new List<ShoptetOrderData>
    {
        new()
        {
            ShoptetOrderId = "SHOP001",
            OrderNumber = "ORD001",
            TotalAmount = 150.00m,
            CustomerEmail = "customer1@test.com",
            Items = new List<ShoptetOrderItemDto>
            {
                new() { ProductCode = "PROD001", ProductName = "Product 1", Quantity = 2, UnitPrice = 75.00m }
            }
        },
        new()
        {
            ShoptetOrderId = "SHOP002",
            OrderNumber = "ORD002",
            TotalAmount = 200.00m,
            CustomerEmail = "customer2@test.com",
            Items = new List<ShoptetOrderItemDto>
            {
                new() { ProductCode = "PROD002", ProductName = "Product 2", Quantity = 1, UnitPrice = 200.00m }
            }
        }
    };
    
    _mockShoptetApiService
        .Setup(x => x.GetOrdersSinceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
        .ReturnsAsync(mockOrders);
    
    // Act
    var result = await _shoptetService.SyncOrdersAsync(integration.Id, true);
    
    // Assert
    Assert.That(result.SyncType, Is.EqualTo(SyncType.Orders));
    Assert.That(result.RecordsProcessed, Is.EqualTo(2));
    Assert.That(result.Errors, Is.EqualTo(0));
    
    // Verify orders were created
    var updatedIntegration = await _shoptetService.GetIntegrationAsync(integration.Id);
    Assert.That(updatedIntegration.TotalOrders, Is.EqualTo(2));
    Assert.That(updatedIntegration.PendingOrders, Is.EqualTo(2));
}

[Test]
public async Task SyncOrdersAsync_WhenNotNeeded_ShouldSkipSync()
{
    // Arrange
    var integration = await CreateActiveIntegration();
    integration.LastSyncDate = DateTime.UtcNow.AddMinutes(-5); // Recent sync
    
    // Act
    var result = await _shoptetService.SyncOrdersAsync(integration.Id, false);
    
    // Assert
    Assert.That(result.Message, Is.EqualTo("Sync not needed"));
    Assert.That(result.RecordsProcessed, Is.EqualTo(0));
}
```

#### Inventory Synchronization
```csharp
[Test]
public async Task SyncInventoryAsync_WithWarehouseInventory_ShouldUpdateShoptet()
{
    // Arrange
    var integration = await CreateActiveIntegrationWithProducts();
    
    var warehouseInventory = new List<WarehouseInventoryItem>
    {
        new() { ProductCode = "PROD001", AvailableQuantity = 85 },
        new() { ProductCode = "PROD002", AvailableQuantity = 120 }
    };
    
    _mockCatalogRepository
        .Setup(x => x.GetWarehouseInventoryAsync(It.IsAny<string>()))
        .ReturnsAsync(warehouseInventory);
    
    // Act
    var result = await _shoptetService.SyncInventoryAsync(integration.Id, true);
    
    // Assert
    Assert.That(result.SyncType, Is.EqualTo(SyncType.Inventory));
    Assert.That(result.RecordsProcessed, Is.EqualTo(2));
    Assert.That(result.Errors, Is.EqualTo(0));
    
    // Verify API calls were made
    _mockShoptetApiService.Verify(x => x.UpdateProductStockAsync(
        It.IsAny<string>(), It.IsAny<string>(), "SHOP-PROD001", 85), Times.Once);
    _mockShoptetApiService.Verify(x => x.UpdateProductStockAsync(
        It.IsAny<string>(), It.IsAny<string>(), "SHOP-PROD002", 120), Times.Once);
}
```

#### Order Processing
```csharp
[Test]
public async Task ProcessOrderAsync_WithValidOrder_ShouldStartProcessing()
{
    // Arrange
    var order = await CreateTestOrder();
    
    var processDto = new ProcessOrderDto
    {
        PickingSessionId = "PICKING-001"
    };
    
    // Act
    var result = await _shoptetService.ProcessOrderAsync(order.Id, processDto);
    
    // Assert
    Assert.That(result.Status, Is.EqualTo(ShoptetOrderStatus.Processing));
    Assert.That(result.PickingSessionId, Is.EqualTo("PICKING-001"));
    
    // Verify Shoptet API was called
    _mockShoptetApiService.Verify(x => x.UpdateOrderStatusAsync(
        It.IsAny<string>(), It.IsAny<string>(), order.ShoptetOrderId, "processing", null), Times.Once);
}

[Test]
public async Task ShipOrderAsync_WithValidOrder_ShouldMarkAsShipped()
{
    // Arrange
    var order = await CreateProcessingOrder();
    
    var shipDto = new ShipOrderDto
    {
        TrackingNumber = "TRACK123456",
        TransportBoxId = "BOX001"
    };
    
    // Act
    var result = await _shoptetService.ShipOrderAsync(order.Id, shipDto);
    
    // Assert
    Assert.That(result.Status, Is.EqualTo(ShoptetOrderStatus.Shipped));
    Assert.That(result.TrackingNumber, Is.EqualTo("TRACK123456"));
    Assert.That(result.TransportBoxId, Is.EqualTo("BOX001"));
    
    // Verify Shoptet API was called with tracking
    _mockShoptetApiService.Verify(x => x.UpdateOrderStatusAsync(
        It.IsAny<string>(), It.IsAny<string>(), order.ShoptetOrderId, "shipped", "TRACK123456"), Times.Once);
}
```

#### Health and Reporting
```csharp
[Test]
public async Task GetHealthReportAsync_ShouldProvideComprehensiveReport()
{
    // Arrange
    var integration = await CreateActiveIntegrationWithData();
    
    // Act
    var report = await _shoptetService.GetHealthReportAsync(integration.Id);
    
    // Assert
    Assert.That(report.IntegrationName, Is.EqualTo(integration.IntegrationName));
    Assert.That(report.Status, Is.EqualTo(integration.Status));
    Assert.That(report.HealthStatus, Is.Not.Null);
    Assert.That(report.TotalOrders, Is.GreaterThan(0));
    Assert.That(report.TotalProducts, Is.GreaterThan(0));
    Assert.That(report.RecentSyncSummary.Count, Is.GreaterThan(0));
}

[Test]
public async Task GetFulfillmentReportAsync_ShouldCalculateMetrics()
{
    // Arrange
    var integration = await CreateIntegrationWithCompletedOrders();
    var fromDate = DateTime.Today.AddDays(-30);
    var toDate = DateTime.Today;
    
    // Act
    var report = await _shoptetService.GetFulfillmentReportAsync(integration.Id, fromDate, toDate);
    
    // Assert
    Assert.That(report.FromDate, Is.EqualTo(fromDate));
    Assert.That(report.ToDate, Is.EqualTo(toDate));
    Assert.That(report.TotalOrders, Is.GreaterThan(0));
    Assert.That(report.ShippedOrders, Is.GreaterThan(0));
    Assert.That(report.AverageProcessingTime, Is.Not.Null);
    Assert.That(report.FulfillmentRate, Is.InRange(0, 100));
}
```

### Performance Tests

#### High Volume Order Sync
```csharp
[Test]
public async Task SyncLargeOrderBatch_ShouldCompleteWithinTimeLimit()
{
    // Arrange
    var integration = await CreateActiveIntegration();
    var largeOrderBatch = GenerateMockOrders(500); // 500 orders
    
    _mockShoptetApiService
        .Setup(x => x.GetOrdersSinceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
        .ReturnsAsync(largeOrderBatch);
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var result = await _shoptetService.SyncOrdersAsync(integration.Id, true);
    stopwatch.Stop();
    
    // Assert
    Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(30000)); // 30 second limit
    Assert.That(result.RecordsProcessed, Is.EqualTo(500));
    Assert.That(result.Errors, Is.EqualTo(0));
}

[Test]
public async Task SyncLargeInventory_ShouldCompleteWithinTimeLimit()
{
    // Arrange
    var integration = await CreateActiveIntegrationWithManyProducts(1000);
    var largeInventory = GenerateMockInventory(1000); // 1000 products
    
    _mockCatalogRepository
        .Setup(x => x.GetWarehouseInventoryAsync(It.IsAny<string>()))
        .ReturnsAsync(largeInventory);
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var result = await _shoptetService.SyncInventoryAsync(integration.Id, true);
    stopwatch.Stop();
    
    // Assert
    Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(60000)); // 60 second limit
    Assert.That(result.RecordsProcessed, Is.EqualTo(1000));
}
```

#### Concurrent Operations
```csharp
[Test]
public async Task ConcurrentOrderProcessing_ShouldHandleCorrectly()
{
    // Arrange
    var integration = await CreateActiveIntegrationWithOrders(20);
    var orders = integration.Orders.Take(10).ToList();
    
    // Act
    var tasks = orders.Select(async (order, index) => 
    {
        var processDto = new ProcessOrderDto 
        { 
            PickingSessionId = $"PICKING-{index:D3}" 
        };
        return await _shoptetService.ProcessOrderAsync(order.Id, processDto);
    });
    
    var results = await Task.WhenAll(tasks);
    
    // Assert
    Assert.That(results.Length, Is.EqualTo(10));
    Assert.That(results.All(r => r.Status == ShoptetOrderStatus.Processing));
    
    // Verify all API calls were made
    _mockShoptetApiService.Verify(x => x.UpdateOrderStatusAsync(
        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), "processing", null), 
        Times.Exactly(10));
}
```

### End-to-End Tests

#### Complete Order Fulfillment Workflow
```csharp
[Test]
public async Task CompleteOrderFulfillmentWorkflow_ShouldProcessSuccessfully()
{
    // Arrange - Create integration and mock orders
    var createInput = new CreateShoptetIntegrationDto
    {
        IntegrationName = "E2E Test Store",
        ShoptetStoreUrl = "https://e2etest.myshoptet.com",
        ApiKey = "test-api-key",
        ApiSecret = "test-api-secret",
        AutoOrderImport = true,
        AutoInventorySync = true,
        DefaultWarehouseCode = "WH001"
    };
    
    // Act & Assert - Create and activate integration
    var integration = await _shoptetService.CreateIntegrationAsync(createInput);
    Assert.That(integration.Status, Is.EqualTo(IntegrationStatus.Inactive));
    
    _mockShoptetApiService
        .Setup(x => x.TestConnectionAsync(It.IsAny<string>(), It.IsAny<string>()))
        .ReturnsAsync(new ApiConnectionResult { IsSuccessful = true });
    
    integration = await _shoptetService.ActivateIntegrationAsync(integration.Id);
    Assert.That(integration.Status, Is.EqualTo(IntegrationStatus.Active));
    
    // Act & Assert - Sync orders
    var mockOrders = new List<ShoptetOrderData>
    {
        CreateMockOrderData("SHOP001", "ORD001", 100.00m, "customer@test.com")
    };
    
    _mockShoptetApiService
        .Setup(x => x.GetOrdersSinceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
        .ReturnsAsync(mockOrders);
    
    var syncResult = await _shoptetService.SyncOrdersAsync(integration.Id, true);
    Assert.That(syncResult.RecordsProcessed, Is.EqualTo(1));
    
    // Act & Assert - Process order
    var orders = await _shoptetService.GetOrdersAsync(integration.Id, new GetShoptetOrdersQuery());
    var order = orders.Items.First();
    
    var processDto = new ProcessOrderDto { PickingSessionId = "PICKING-001" };
    order = await _shoptetService.ProcessOrderAsync(order.Id, processDto);
    Assert.That(order.Status, Is.EqualTo(ShoptetOrderStatus.Processing));
    
    // Act & Assert - Ship order
    var shipDto = new ShipOrderDto 
    { 
        TrackingNumber = "TRACK123456",
        TransportBoxId = "BOX001"
    };
    order = await _shoptetService.ShipOrderAsync(order.Id, shipDto);
    Assert.That(order.Status, Is.EqualTo(ShoptetOrderStatus.Shipped));
    Assert.That(order.TrackingNumber, Is.EqualTo("TRACK123456"));
    
    // Verify all Shoptet API interactions
    _mockShoptetApiService.Verify(x => x.UpdateOrderStatusAsync(
        It.IsAny<string>(), It.IsAny<string>(), "SHOP001", "processing", null), Times.Once);
    _mockShoptetApiService.Verify(x => x.UpdateOrderStatusAsync(
        It.IsAny<string>(), It.IsAny<string>(), "SHOP001", "shipped", "TRACK123456"), Times.Once);
}
```

#### Error Recovery and Resilience
```csharp
[Test]
public async Task SyncWithAPIFailures_ShouldRecoverGracefully()
{
    // Arrange
    var integration = await CreateActiveIntegration();
    
    // Simulate API failures
    _mockShoptetApiService
        .SetupSequence(x => x.GetOrdersSinceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
        .ThrowsAsync(new HttpRequestException("API temporarily unavailable"))
        .ReturnsAsync(new List<ShoptetOrderData> 
        { 
            CreateMockOrderData("SHOP001", "ORD001", 100.00m, "customer@test.com") 
        });
    
    // Act - First sync should fail
    await Assert.ThrowsAsync<HttpRequestException>(() => 
        _shoptetService.SyncOrdersAsync(integration.Id, true));
    
    // Act - Second sync should succeed
    var result = await _shoptetService.SyncOrdersAsync(integration.Id, true);
    
    // Assert
    Assert.That(result.RecordsProcessed, Is.EqualTo(1));
    Assert.That(result.Errors, Is.EqualTo(0));
    
    // Verify error was logged
    var healthReport = await _shoptetService.GetHealthReportAsync(integration.Id);
    Assert.That(healthReport.UnresolvedErrors.Count, Is.GreaterThan(0));
}
```

## Test Data Builders

### Integration Builder
```csharp
public class ShoptetIntegrationBuilder
{
    private ShoptetIntegration _integration = new() { Id = Guid.NewGuid() };
    
    public ShoptetIntegrationBuilder WithStatus(IntegrationStatus status)
    {
        _integration.Status = status;
        return this;
    }
    
    public ShoptetIntegrationBuilder WithCredentials(string apiKey, string apiSecret)
    {
        _integration.ApiKey = apiKey;
        _integration.ApiSecret = apiSecret;
        return this;
    }
    
    public ShoptetIntegrationBuilder WithOrder(string shoptetOrderId, string orderNumber, decimal amount)
    {
        var items = new List<ShoptetOrderItemDto>
        {
            new() { ProductCode = "PROD001", ProductName = "Product 1", Quantity = 1, UnitPrice = amount }
        };
        _integration.AddOrder(shoptetOrderId, orderNumber, amount, "customer@test.com", items);
        return this;
    }
    
    public ShoptetIntegrationBuilder WithProduct(string shoptetProductId, string productCode, decimal price, int stock)
    {
        _integration.AddOrUpdateProduct(shoptetProductId, productCode, $"Product {productCode}", price, stock, true);
        return this;
    }
    
    public ShoptetIntegration Build() => _integration;
}
```

### Performance Test Data
```csharp
public static class ShoptetTestDataGenerator
{
    public static List<ShoptetOrderData> GenerateMockOrders(int count)
    {
        var orders = new List<ShoptetOrderData>();
        
        for (int i = 0; i < count; i++)
        {
            orders.Add(new ShoptetOrderData
            {
                ShoptetOrderId = $"SHOP{i:D6}",
                OrderNumber = $"ORD{i:D6}",
                TotalAmount = Random.Shared.Next(10, 500),
                CustomerEmail = $"customer{i}@test.com",
                Items = new List<ShoptetOrderItemDto>
                {
                    new()
                    {
                        ProductCode = $"PROD{Random.Shared.Next(1, 100):D3}",
                        ProductName = $"Product {i}",
                        Quantity = Random.Shared.Next(1, 5),
                        UnitPrice = Random.Shared.Next(10, 100)
                    }
                }
            });
        }
        
        return orders;
    }
    
    public static List<WarehouseInventoryItem> GenerateMockInventory(int count)
    {
        var inventory = new List<WarehouseInventoryItem>();
        
        for (int i = 0; i < count; i++)
        {
            inventory.Add(new WarehouseInventoryItem
            {
                ProductCode = $"PROD{i:D6}",
                AvailableQuantity = Random.Shared.Next(0, 200)
            });
        }
        
        return inventory;
    }
}
```