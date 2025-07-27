# Test Scenarios: Picking Operations Management

## Unit Tests

### PickingSession Aggregate Tests

#### Session Creation and Order Management
```csharp
[Test]
public void AddOrder_WhenSessionIsCreated_ShouldAddOrderSuccessfully()
{
    // Arrange
    var session = new PickingSession 
    { 
        Id = Guid.NewGuid(), 
        Status = PickingSessionStatus.Created 
    };
    
    // Act
    session.AddOrder("ORD001", "Customer A", PickingPriority.High);
    
    // Assert
    Assert.That(session.TotalOrders, Is.EqualTo(1));
    Assert.That(session.Orders.First().OrderNumber, Is.EqualTo("ORD001"));
    Assert.That(session.Orders.First().CustomerName, Is.EqualTo("Customer A"));
    Assert.That(session.Orders.First().Priority, Is.EqualTo(PickingPriority.High));
}

[Test]
public void AddOrder_WhenSessionInProgress_ShouldThrowException()
{
    // Arrange
    var session = new PickingSession 
    { 
        Status = PickingSessionStatus.InProgress 
    };
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        session.AddOrder("ORD001", "Customer A"));
}

[Test]
public void AddItem_WhenOrderExists_ShouldAddItemSuccessfully()
{
    // Arrange
    var session = new PickingSession 
    { 
        Id = Guid.NewGuid(), 
        Status = PickingSessionStatus.Created 
    };
    session.AddOrder("ORD001", "Customer A");
    
    // Act
    session.AddItem("ORD001", "PROD001", "Product 1", 5, "A-15-B");
    
    // Assert
    Assert.That(session.TotalItems, Is.EqualTo(1));
    Assert.That(session.Items.First().ProductCode, Is.EqualTo("PROD001"));
    Assert.That(session.Items.First().RequiredQuantity, Is.EqualTo(5));
    Assert.That(session.Items.First().LocationCode, Is.EqualTo("A-15-B"));
}

[Test]
public void AddItem_WhenOrderNotFound_ShouldThrowException()
{
    // Arrange
    var session = new PickingSession 
    { 
        Status = PickingSessionStatus.Created 
    };
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        session.AddItem("ORD999", "PROD001", "Product 1", 5, "A-15-B"));
}
```

#### Session Lifecycle Management
```csharp
[Test]
public void StartSession_WhenSessionHasItems_ShouldStartSuccessfully()
{
    // Arrange
    var session = CreateSessionWithItems();
    
    // Act
    session.StartSession("PICKER001");
    
    // Assert
    Assert.That(session.Status, Is.EqualTo(PickingSessionStatus.InProgress));
    Assert.That(session.AssignedPickerId, Is.EqualTo("PICKER001"));
    Assert.That(session.ActualStartDate, Is.Not.Null);
    Assert.That(session.Orders.All(o => o.Status == PickingOrderStatus.InProgress));
}

[Test]
public void StartSession_WhenSessionHasNoItems_ShouldThrowException()
{
    // Arrange
    var session = new PickingSession 
    { 
        Status = PickingSessionStatus.Created 
    };
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        session.StartSession("PICKER001"));
}

[Test]
public void CompleteSession_WhenAllItemsPicked_ShouldCompleteSuccessfully()
{
    // Arrange
    var session = CreateInProgressSessionWithPickedItems();
    
    // Act
    session.CompleteSession();
    
    // Assert
    Assert.That(session.Status, Is.EqualTo(PickingSessionStatus.Completed));
    Assert.That(session.CompletionDate, Is.Not.Null);
    Assert.That(session.Orders.All(o => o.Status == PickingOrderStatus.Completed));
}

[Test]
public void CompleteSession_WhenUnpickedItemsExist_ShouldThrowException()
{
    // Arrange
    var session = CreateInProgressSessionWithUnpickedItems();
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        session.CompleteSession());
}
```

#### Item Picking Operations
```csharp
[Test]
public void PickItem_WhenValidItem_ShouldUpdateItemAndOrder()
{
    // Arrange
    var session = CreateInProgressSession();
    var item = session.Items.First();
    
    // Act
    session.PickItem(item.Id, 5, "PICKER001", "Good condition");
    
    // Assert
    Assert.That(item.Status, Is.EqualTo(PickingItemStatus.Picked));
    Assert.That(item.PickedQuantity, Is.EqualTo(5));
    Assert.That(item.PickedBy, Is.EqualTo("PICKER001"));
    Assert.That(item.Notes, Is.EqualTo("Good condition"));
    Assert.That(item.PickingTime, Is.Not.Null);
}

[Test]
public void PickItem_WhenLastItemInOrder_ShouldCompleteOrder()
{
    // Arrange
    var session = CreateInProgressSessionWithSingleItem();
    var item = session.Items.First();
    var order = session.Orders.First();
    
    // Act
    session.PickItem(item.Id, 5, "PICKER001");
    
    // Assert
    Assert.That(order.Status, Is.EqualTo(PickingOrderStatus.Completed));
    Assert.That(order.CompletionTime, Is.Not.Null);
}

[Test]
public void PickItem_WhenSessionNotInProgress_ShouldThrowException()
{
    // Arrange
    var session = CreateSessionWithItems(); // Created status
    var item = session.Items.First();
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        session.PickItem(item.Id, 5, "PICKER001"));
}
```

#### Route Optimization
```csharp
[Test]
public void OptimizePickingRoute_WhenSessionCreated_ShouldUpdateSequences()
{
    // Arrange
    var session = CreateSessionWithMultipleItems();
    
    // Act
    session.OptimizePickingRoute();
    
    // Assert
    Assert.That(session.Routes.Count, Is.EqualTo(1));
    Assert.That(session.Items.All(i => i.PickingSequence > 0));
    Assert.That(session.Items.Select(i => i.PickingSequence).Distinct().Count(), 
                Is.EqualTo(session.Items.Count)); // All unique sequences
}

[Test]
public void OptimizePickingRoute_WhenSessionInProgress_ShouldThrowException()
{
    // Arrange
    var session = CreateInProgressSession();
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        session.OptimizePickingRoute());
}
```

#### Performance Metrics
```csharp
[Test]
public void GetPerformanceMetrics_WhenSessionCompleted_ShouldCalculateCorrectly()
{
    // Arrange
    var session = CreateCompletedSession();
    
    // Act
    var metrics = session.GetPerformanceMetrics();
    
    // Assert
    Assert.That(metrics.IsComplete, Is.True);
    Assert.That(metrics.TotalItems, Is.EqualTo(session.TotalItems));
    Assert.That(metrics.ItemsPerHour, Is.GreaterThan(0));
    Assert.That(metrics.AccuracyPercentage, Is.InRange(0, 100));
}

[Test]
public void GetPerformanceMetrics_WhenSessionNotCompleted_ShouldReturnIncompleteMetrics()
{
    // Arrange
    var session = CreateInProgressSession();
    
    // Act
    var metrics = session.GetPerformanceMetrics();
    
    // Assert
    Assert.That(metrics.IsComplete, Is.False);
}
```

### PickingOrder Entity Tests

#### Order Lifecycle
```csharp
[Test]
public void StartPicking_WhenOrderPlanned_ShouldStartSuccessfully()
{
    // Arrange
    var order = new PickingOrder 
    { 
        Status = PickingOrderStatus.Planned 
    };
    
    // Act
    order.StartPicking();
    
    // Assert
    Assert.That(order.Status, Is.EqualTo(PickingOrderStatus.InProgress));
    Assert.That(order.StartTime, Is.Not.Null);
}

[Test]
public void CompletePicking_WhenAllItemsPicked_ShouldCompleteSuccessfully()
{
    // Arrange
    var order = CreateOrderWithPickedItems();
    
    // Act
    order.CompletePicking();
    
    // Assert
    Assert.That(order.Status, Is.EqualTo(PickingOrderStatus.Completed));
    Assert.That(order.CompletionTime, Is.Not.Null);
}

[Test]
public void CompletePicking_WhenUnpickedItemsExist_ShouldThrowException()
{
    // Arrange
    var order = CreateOrderWithUnpickedItems();
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        order.CompletePicking());
}
```

#### Transport Box Assignment
```csharp
[Test]
public void AssignToTransportBox_ShouldUpdateTransportBoxId()
{
    // Arrange
    var order = new PickingOrder();
    
    // Act
    order.AssignToTransportBox("BOX001");
    
    // Assert
    Assert.That(order.TransportBoxId, Is.EqualTo("BOX001"));
}
```

### PickingItem Entity Tests

#### Item Picking
```csharp
[Test]
public void PickItem_WithCorrectQuantity_ShouldBeAccurate()
{
    // Arrange
    var item = new PickingItem 
    { 
        RequiredQuantity = 10,
        Status = PickingItemStatus.Pending
    };
    
    // Act
    item.PickItem(10, "PICKER001");
    
    // Assert
    Assert.That(item.IsAccurate, Is.True);
    Assert.That(item.HasVariance, Is.False);
    Assert.That(item.QuantityVariance, Is.EqualTo(0));
}

[Test]
public void PickItem_WithShortPick_ShouldIndicateVariance()
{
    // Arrange
    var item = new PickingItem 
    { 
        RequiredQuantity = 10,
        Status = PickingItemStatus.Pending
    };
    
    // Act
    item.PickItem(8, "PICKER001");
    
    // Assert
    Assert.That(item.IsShortPick, Is.True);
    Assert.That(item.HasVariance, Is.True);
    Assert.That(item.QuantityVariance, Is.EqualTo(-2));
}

[Test]
public void PickItem_WithOverPick_ShouldIndicateVariance()
{
    // Arrange
    var item = new PickingItem 
    { 
        RequiredQuantity = 10,
        Status = PickingItemStatus.Pending
    };
    
    // Act
    item.PickItem(12, "PICKER001");
    
    // Assert
    Assert.That(item.IsOverPick, Is.True);
    Assert.That(item.HasVariance, Is.True);
    Assert.That(item.QuantityVariance, Is.EqualTo(2));
}

[Test]
public void PickItem_WhenAlreadyPicked_ShouldThrowException()
{
    // Arrange
    var item = new PickingItem 
    { 
        Status = PickingItemStatus.Picked
    };
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        item.PickItem(5, "PICKER001"));
}
```

#### Item Status Management
```csharp
[Test]
public void SkipItem_ShouldUpdateStatusAndReason()
{
    // Arrange
    var item = new PickingItem 
    { 
        Status = PickingItemStatus.Pending
    };
    
    // Act
    item.SkipItem("Item damaged");
    
    // Assert
    Assert.That(item.Status, Is.EqualTo(PickingItemStatus.Skipped));
    Assert.That(item.Notes, Is.EqualTo("Skipped: Item damaged"));
}

[Test]
public void ResetPicking_ShouldClearPickingData()
{
    // Arrange
    var item = new PickingItem 
    { 
        Status = PickingItemStatus.Picked,
        PickedQuantity = 5,
        PickedBy = "PICKER001"
    };
    
    // Act
    item.ResetPicking();
    
    // Assert
    Assert.That(item.Status, Is.EqualTo(PickingItemStatus.Pending));
    Assert.That(item.PickedQuantity, Is.Null);
    Assert.That(item.PickedBy, Is.Null);
}
```

#### Expiration Date Management
```csharp
[Test]
public void IsExpired_WhenDatePassed_ShouldReturnTrue()
{
    // Arrange
    var item = new PickingItem 
    { 
        ExpirationDate = DateTime.Today.AddDays(-1)
    };
    
    // Act & Assert
    Assert.That(item.IsExpired, Is.True);
}

[Test]
public void IsNearExpiry_WhenWithinSevenDays_ShouldReturnTrue()
{
    // Arrange
    var item = new PickingItem 
    { 
        ExpirationDate = DateTime.Today.AddDays(5)
    };
    
    // Act & Assert
    Assert.That(item.IsNearExpiry, Is.True);
}
```

### PickingPerformanceMetrics Value Object Tests

```csharp
[Test]
public void PerformanceGrade_WithHighAccuracyAndSpeed_ShouldBeExcellent()
{
    // Arrange
    var metrics = new PickingPerformanceMetrics
    {
        IsComplete = true,
        AccuracyPercentage = 99.5,
        ItemsPerHour = 55
    };
    
    // Act & Assert
    Assert.That(metrics.PerformanceGrade, Is.EqualTo("Excellent"));
}

[Test]
public void MeetsTargets_WithMinimumThresholds_ShouldReturnTrue()
{
    // Arrange
    var metrics = new PickingPerformanceMetrics
    {
        AccuracyPercentage = 95,
        ItemsPerHour = 40
    };
    
    // Act & Assert
    Assert.That(metrics.MeetsTargets, Is.True);
}
```

## Integration Tests

### Application Service Tests

#### Session Creation and Management
```csharp
[Test]
public async Task CreateSessionAsync_WithValidInput_ShouldCreateSession()
{
    // Arrange
    var input = new CreatePickingSessionDto
    {
        SessionName = "Test Session",
        Type = PickingSessionType.Standard,
        PlannedStartDate = DateTime.Today.AddHours(8),
        Orders = new List<CreatePickingOrderDto>
        {
            new()
            {
                OrderNumber = "ORD001",
                CustomerName = "Customer A",
                Items = new List<CreatePickingItemDto>
                {
                    new() { ProductCode = "PROD001", ProductName = "Product 1", Quantity = 5 }
                }
            }
        }
    };
    
    // Act
    var result = await _pickingService.CreateSessionAsync(input);
    
    // Assert
    Assert.That(result.SessionName, Is.EqualTo("Test Session"));
    Assert.That(result.TotalOrders, Is.EqualTo(1));
    Assert.That(result.TotalItems, Is.EqualTo(1));
}

[Test]
public async Task StartSessionAsync_WithValidSession_ShouldStartSuccessfully()
{
    // Arrange
    var session = await CreateTestSession();
    
    // Act
    var result = await _pickingService.StartSessionAsync(session.Id, "PICKER001");
    
    // Assert
    Assert.That(result.Status, Is.EqualTo(PickingSessionStatus.InProgress));
    Assert.That(result.AssignedPickerId, Is.EqualTo("PICKER001"));
}
```

#### Route Optimization Integration
```csharp
[Test]
public async Task OptimizePickingRouteAsync_WithMultipleItems_ShouldOptimizeRoute()
{
    // Arrange
    var session = await CreateSessionWithMultipleLocations();
    
    // Act
    var result = await _pickingService.OptimizePickingRouteAsync(session.Id);
    
    // Assert
    Assert.That(result.Routes.Count, Is.EqualTo(1));
    Assert.That(result.Items.All(i => i.PickingSequence > 0));
    
    // Verify optimization improved route efficiency
    var route = result.Routes.First();
    Assert.That(route.TotalDistance, Is.LessThan(1000)); // Arbitrary threshold
    Assert.That(route.EstimatedTime.TotalMinutes, Is.LessThan(120));
}
```

#### Inventory Integration
```csharp
[Test]
public async Task PickItemAsync_ShouldUpdateInventoryLevels()
{
    // Arrange
    var session = await CreateActiveSession();
    var item = session.Items.First();
    var initialStock = await _catalogRepository.GetProductStockAsync(item.ProductCode);
    
    var pickDto = new PickItemDto
    {
        ItemId = item.Id,
        PickedQuantity = 5
    };
    
    // Act
    await _pickingService.PickItemAsync(session.Id, pickDto);
    
    // Assert
    var updatedStock = await _catalogRepository.GetProductStockAsync(item.ProductCode);
    Assert.That(updatedStock.CurrentQuantity, Is.EqualTo(initialStock.CurrentQuantity - 5));
}
```

#### Transport Box Integration
```csharp
[Test]
public async Task PickItemAsync_WithTransportBox_ShouldAddItemToBox()
{
    // Arrange
    var session = await CreateActiveSession();
    var item = session.Items.First();
    var transportBox = await CreateTransportBox();
    
    var pickDto = new PickItemDto
    {
        ItemId = item.Id,
        PickedQuantity = 5,
        TransportBoxCode = transportBox.Code
    };
    
    // Act
    await _pickingService.PickItemAsync(session.Id, pickDto);
    
    // Assert
    var updatedBox = await _transportBoxRepository.GetBoxByCodeAsync(transportBox.Code);
    Assert.That(updatedBox.Items.Count, Is.EqualTo(1));
    Assert.That(updatedBox.Items.First().ProductCode, Is.EqualTo(item.ProductCode));
    Assert.That(updatedBox.Items.First().Quantity, Is.EqualTo(5));
}
```

### Performance Tests

#### High Volume Picking
```csharp
[Test]
public async Task CreateLargePickingSession_ShouldCompleteWithinTimeLimit()
{
    // Arrange
    var largeOrderInput = CreateLargePickingSessionInput(1000); // 1000 items
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var result = await _pickingService.CreateSessionAsync(largeOrderInput);
    stopwatch.Stop();
    
    // Assert
    Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000)); // 5 second limit
    Assert.That(result.TotalItems, Is.EqualTo(1000));
}

[Test]
public async Task RouteOptimization_WithManyItems_ShouldCompleteQuickly()
{
    // Arrange
    var session = await CreateLargeSession(500);
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    await _pickingService.OptimizePickingRouteAsync(session.Id);
    stopwatch.Stop();
    
    // Assert
    Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(10000)); // 10 second limit
}
```

#### Concurrent Operations
```csharp
[Test]
public async Task ConcurrentPickingOperations_ShouldHandleCorrectly()
{
    // Arrange
    var session = await CreateLargeActiveSession();
    var items = session.Items.Take(10).ToList();
    
    // Act
    var tasks = items.Select(async item => 
    {
        var pickDto = new PickItemDto 
        { 
            ItemId = item.Id, 
            PickedQuantity = item.RequiredQuantity 
        };
        return await _pickingService.PickItemAsync(session.Id, pickDto);
    });
    
    var results = await Task.WhenAll(tasks);
    
    // Assert
    Assert.That(results.Length, Is.EqualTo(10));
    Assert.That(results.All(r => r != null));
    
    // Verify final state is consistent
    var finalSession = await _pickingService.GetSessionAsync(session.Id);
    Assert.That(finalSession.PickedItems, Is.EqualTo(10));
}
```

### End-to-End Tests

#### Complete Picking Workflow
```csharp
[Test]
public async Task CompletePickingWorkflow_ShouldProcessSuccessfully()
{
    // Arrange - Create session with multiple orders
    var createInput = new CreatePickingSessionDto
    {
        SessionName = "E2E Test Session",
        Type = PickingSessionType.Batch,
        PlannedStartDate = DateTime.Now.AddHours(1),
        Orders = CreateMultipleTestOrders(5) // 5 orders with various items
    };
    
    // Act & Assert - Create session
    var session = await _pickingService.CreateSessionAsync(createInput);
    Assert.That(session.Status, Is.EqualTo(PickingSessionStatus.Created));
    
    // Act & Assert - Optimize route
    session = await _pickingService.OptimizePickingRouteAsync(session.Id);
    Assert.That(session.Routes.Count, Is.EqualTo(1));
    
    // Act & Assert - Start session
    session = await _pickingService.StartSessionAsync(session.Id, "PICKER001");
    Assert.That(session.Status, Is.EqualTo(PickingSessionStatus.InProgress));
    
    // Act & Assert - Pick all items
    foreach (var item in session.Items.OrderBy(i => i.PickingSequence))
    {
        var pickDto = new PickItemDto
        {
            ItemId = item.Id,
            PickedQuantity = item.RequiredQuantity
        };
        session = await _pickingService.PickItemAsync(session.Id, pickDto);
    }
    
    // Act & Assert - Complete session
    session = await _pickingService.CompleteSessionAsync(session.Id);
    Assert.That(session.Status, Is.EqualTo(PickingSessionStatus.Completed));
    Assert.That(session.CompletionPercentage, Is.EqualTo(100));
    
    // Verify performance metrics
    var performanceReport = await _pickingService.GetPerformanceReportAsync(
        "PICKER001", DateTime.Today, DateTime.Today.AddDays(1));
    Assert.That(performanceReport.TotalSessions, Is.EqualTo(1));
    Assert.That(performanceReport.CompletedSessions, Is.EqualTo(1));
}
```

#### Error Recovery Scenarios
```csharp
[Test]
public async Task PickingWithSystemInterruption_ShouldRecoverCorrectly()
{
    // Arrange
    var session = await CreateActiveSessionWithMultipleItems();
    
    // Pick some items
    var itemsToPick = session.Items.Take(3).ToList();
    foreach (var item in itemsToPick)
    {
        await _pickingService.PickItemAsync(session.Id, new PickItemDto 
        { 
            ItemId = item.Id, 
            PickedQuantity = item.RequiredQuantity 
        });
    }
    
    // Simulate system restart by getting fresh session
    var recoveredSession = await _pickingService.GetSessionAsync(session.Id);
    
    // Assert state is preserved
    Assert.That(recoveredSession.Status, Is.EqualTo(PickingSessionStatus.InProgress));
    Assert.That(recoveredSession.PickedItems, Is.EqualTo(3));
    
    // Continue picking remaining items
    var remainingItems = recoveredSession.Items.Where(i => i.Status == PickingItemStatus.Pending);
    foreach (var item in remainingItems)
    {
        await _pickingService.PickItemAsync(recoveredSession.Id, new PickItemDto 
        { 
            ItemId = item.Id, 
            PickedQuantity = item.RequiredQuantity 
        });
    }
    
    // Complete successfully
    var finalSession = await _pickingService.CompleteSessionAsync(recoveredSession.Id);
    Assert.That(finalSession.Status, Is.EqualTo(PickingSessionStatus.Completed));
}
```

## Test Data Builders

### Session Builder
```csharp
public class PickingSessionBuilder
{
    private PickingSession _session = new();
    
    public PickingSessionBuilder WithStatus(PickingSessionStatus status)
    {
        _session.Status = status;
        return this;
    }
    
    public PickingSessionBuilder WithOrder(string orderNumber, string customerName)
    {
        _session.AddOrder(orderNumber, customerName);
        return this;
    }
    
    public PickingSessionBuilder WithItem(string orderNumber, string productCode, int quantity, string location)
    {
        _session.AddItem(orderNumber, productCode, $"Product {productCode}", quantity, location);
        return this;
    }
    
    public PickingSession Build() => _session;
}
```

### Performance Test Data
```csharp
public static class PickingTestDataGenerator
{
    public static CreatePickingSessionDto CreateLargePickingSessionInput(int itemCount)
    {
        var orders = new List<CreatePickingOrderDto>();
        var itemsPerOrder = 20;
        var orderCount = (int)Math.Ceiling(itemCount / (double)itemsPerOrder);
        
        for (int i = 0; i < orderCount; i++)
        {
            var items = new List<CreatePickingItemDto>();
            var remainingItems = Math.Min(itemsPerOrder, itemCount - (i * itemsPerOrder));
            
            for (int j = 0; j < remainingItems; j++)
            {
                items.Add(new CreatePickingItemDto
                {
                    ProductCode = $"PROD{i:D3}{j:D3}",
                    ProductName = $"Product {i}-{j}",
                    Quantity = Random.Shared.Next(1, 10)
                });
            }
            
            orders.Add(new CreatePickingOrderDto
            {
                OrderNumber = $"ORD{i:D6}",
                CustomerName = $"Customer {i}",
                Items = items
            });
        }
        
        return new CreatePickingSessionDto
        {
            SessionName = $"Large Session {itemCount} items",
            Type = PickingSessionType.Batch,
            PlannedStartDate = DateTime.Now.AddHours(1),
            Orders = orders
        };
    }
}