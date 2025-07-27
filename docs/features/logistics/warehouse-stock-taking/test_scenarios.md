# Test Scenarios: Warehouse Stock Taking

## Unit Tests

### WarehouseStockTaking Aggregate Tests

#### Stock Taking Creation and Location Management
```csharp
[Test]
public void AddLocation_WhenStockTakingPlanned_ShouldAddLocationSuccessfully()
{
    // Arrange
    var stockTaking = new WarehouseStockTaking 
    { 
        Id = Guid.NewGuid(), 
        Status = WarehouseStockTakingStatus.Planned 
    };
    
    // Act
    stockTaking.AddLocation("A-15-B", "Location A-15-B", "Zone A", WarehouseLocationPriority.High);
    
    // Assert
    Assert.That(stockTaking.TotalLocations, Is.EqualTo(1));
    Assert.That(stockTaking.Locations.First().LocationCode, Is.EqualTo("A-15-B"));
    Assert.That(stockTaking.Locations.First().Priority, Is.EqualTo(WarehouseLocationPriority.High));
    Assert.That(stockTaking.Locations.First().Zone, Is.EqualTo("Zone A"));
}

[Test]
public void AddLocation_WhenStockTakingInProgress_ShouldThrowException()
{
    // Arrange
    var stockTaking = new WarehouseStockTaking 
    { 
        Status = WarehouseStockTakingStatus.InProgress 
    };
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        stockTaking.AddLocation("A-15-B", "Location A-15-B"));
}
```

#### Stock Taking Lifecycle Management
```csharp
[Test]
public void StartStockTaking_WhenPlannedWithLocations_ShouldStartSuccessfully()
{
    // Arrange
    var stockTaking = CreatePlannedStockTakingWithLocations();
    
    // Act
    stockTaking.StartStockTaking("USER001");
    
    // Assert
    Assert.That(stockTaking.Status, Is.EqualTo(WarehouseStockTakingStatus.InProgress));
    Assert.That(stockTaking.ResponsibleUserId, Is.EqualTo("USER001"));
    Assert.That(stockTaking.StartDate, Is.Not.Null);
    Assert.That(stockTaking.TotalItems, Is.GreaterThan(0)); // Count records created
}

[Test]
public void StartStockTaking_WhenNoLocations_ShouldThrowException()
{
    // Arrange
    var stockTaking = new WarehouseStockTaking 
    { 
        Status = WarehouseStockTakingStatus.Planned 
    };
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        stockTaking.StartStockTaking("USER001"));
}

[Test]
public void CompleteStockTaking_WhenAllItemsCounted_ShouldCompleteSuccessfully()
{
    // Arrange
    var stockTaking = CreateInProgressStockTakingWithCountedItems();
    
    // Act
    stockTaking.CompleteStockTaking();
    
    // Assert
    Assert.That(stockTaking.Status, Is.EqualTo(WarehouseStockTakingStatus.Completed));
    Assert.That(stockTaking.CompletionDate, Is.Not.Null);
    Assert.That(stockTaking.Assignments.All(a => a.Status == AssignmentStatus.Completed));
}

[Test]
public void CompleteStockTaking_WhenUnCountedItemsExist_ShouldThrowException()
{
    // Arrange
    var stockTaking = CreateInProgressStockTakingWithUnCountedItems();
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        stockTaking.CompleteStockTaking());
}
```

#### Counter Assignment Management
```csharp
[Test]
public void AssignCounter_WhenLocationExists_ShouldCreateAssignment()
{
    // Arrange
    var stockTaking = CreateInProgressStockTaking();
    var locationCode = stockTaking.Locations.First().LocationCode;
    
    // Act
    stockTaking.AssignCounter(locationCode, "COUNTER001", "John Doe");
    
    // Assert
    Assert.That(stockTaking.Assignments.Count, Is.EqualTo(1));
    Assert.That(stockTaking.Assignments.First().CounterId, Is.EqualTo("COUNTER001"));
    Assert.That(stockTaking.Assignments.First().LocationCode, Is.EqualTo(locationCode));
}

[Test]
public void AssignCounter_WhenLocationNotFound_ShouldThrowException()
{
    // Arrange
    var stockTaking = CreateInProgressStockTaking();
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        stockTaking.AssignCounter("UNKNOWN", "COUNTER001", "John Doe"));
}

[Test]
public void AssignCounter_WhenLocationAlreadyAssigned_ShouldReassign()
{
    // Arrange
    var stockTaking = CreateInProgressStockTaking();
    var locationCode = stockTaking.Locations.First().LocationCode;
    stockTaking.AssignCounter(locationCode, "COUNTER001", "John Doe");
    
    // Act
    stockTaking.AssignCounter(locationCode, "COUNTER002", "Jane Smith");
    
    // Assert
    Assert.That(stockTaking.Assignments.Count, Is.EqualTo(1)); // Same assignment updated
    Assert.That(stockTaking.Assignments.First().CounterId, Is.EqualTo("COUNTER002"));
    Assert.That(stockTaking.Assignments.First().CounterName, Is.EqualTo("Jane Smith"));
}
```

#### Count Recording and Discrepancy Detection
```csharp
[Test]
public void RecordCount_WhenValidRecord_ShouldUpdateRecord()
{
    // Arrange
    var stockTaking = CreateInProgressStockTakingWithRecords();
    var record = stockTaking.CountRecords.First();
    
    // Act
    stockTaking.RecordCount(record.LocationCode, record.ProductCode, 15, "COUNTER001", record.LotNumber, "Good condition");
    
    // Assert
    Assert.That(record.Status, Is.EqualTo(CountRecordStatus.Counted));
    Assert.That(record.CountedQuantity, Is.EqualTo(15));
    Assert.That(record.CountedBy, Is.EqualTo("COUNTER001"));
    Assert.That(record.Notes, Is.EqualTo("Good condition"));
}

[Test]
public void RecordCount_WithVariance_ShouldCreateDiscrepancy()
{
    // Arrange
    var stockTaking = CreateInProgressStockTakingWithRecords();
    var record = stockTaking.CountRecords.First();
    record.SystemQuantity = 10; // Expected quantity
    
    // Act
    stockTaking.RecordCount(record.LocationCode, record.ProductCode, 8, "COUNTER001"); // Short by 2
    
    // Assert
    Assert.That(stockTaking.Discrepancies.Count, Is.EqualTo(1));
    Assert.That(stockTaking.Discrepancies.First().Variance, Is.EqualTo(-2));
    Assert.That(stockTaking.Discrepancies.First().VariancePercentage, Is.EqualTo(-20));
}

[Test]
public void RecordCount_WithSignificantVariance_ShouldMarkAsSignificant()
{
    // Arrange
    var stockTaking = CreateInProgressStockTakingWithRecords();
    var record = stockTaking.CountRecords.First();
    record.SystemQuantity = 100;
    
    // Act
    stockTaking.RecordCount(record.LocationCode, record.ProductCode, 90, "COUNTER001"); // 10% variance
    
    // Assert
    Assert.That(stockTaking.Discrepancies.Count, Is.EqualTo(1));
    Assert.That(stockTaking.Discrepancies.First().IsSignificant, Is.True);
}

[Test]
public void RecordCount_WhenRecordNotFound_ShouldThrowException()
{
    // Arrange
    var stockTaking = CreateInProgressStockTaking();
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        stockTaking.RecordCount("UNKNOWN", "PROD999", 10, "COUNTER001"));
}
```

#### Location Completion Management
```csharp
[Test]
public void CompleteLocationCount_WhenAllItemsCounted_ShouldCompleteLocation()
{
    // Arrange
    var stockTaking = CreateInProgressStockTakingWithCountedLocation();
    var location = stockTaking.Locations.First();
    
    // Act
    stockTaking.CompleteLocationCount(location.LocationCode, "COUNTER001");
    
    // Assert
    Assert.That(location.Status, Is.EqualTo(LocationCountStatus.Completed));
    Assert.That(location.CountedBy, Is.EqualTo("COUNTER001"));
    Assert.That(location.CompletionTime, Is.Not.Null);
}

[Test]
public void CompleteLocationCount_WithUnCountedItems_ShouldThrowException()
{
    // Arrange
    var stockTaking = CreateInProgressStockTakingWithUnCountedItems();
    var location = stockTaking.Locations.First();
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        stockTaking.CompleteLocationCount(location.LocationCode, "COUNTER001"));
}
```

#### Summary and Analytics
```csharp
[Test]
public void GetSummary_ShouldCalculateCorrectStatistics()
{
    // Arrange
    var stockTaking = CreateCompletedStockTaking();
    
    // Act
    var summary = stockTaking.GetSummary();
    
    // Assert
    Assert.That(summary.StockTakingId, Is.EqualTo(stockTaking.Id));
    Assert.That(summary.TotalLocations, Is.EqualTo(stockTaking.TotalLocations));
    Assert.That(summary.CompletedLocations, Is.EqualTo(stockTaking.CompletedLocations));
    Assert.That(summary.CompletionPercentage, Is.EqualTo(100));
    Assert.That(summary.IsCompleted, Is.True);
}

[Test]
public void GetSummary_WithDiscrepancies_ShouldIndicateIssues()
{
    // Arrange
    var stockTaking = CreateCompletedStockTakingWithDiscrepancies();
    
    // Act
    var summary = stockTaking.GetSummary();
    
    // Assert
    Assert.That(summary.HasIssues, Is.True);
    Assert.That(summary.SignificantDiscrepancies, Is.GreaterThan(0));
    Assert.That(summary.OverallHealth, Is.EqualTo("Issues Detected"));
}
```

### WarehouseLocation Entity Tests

#### Location Status Management
```csharp
[Test]
public void StartCount_WhenLocationPending_ShouldStartSuccessfully()
{
    // Arrange
    var location = new WarehouseLocation 
    { 
        Status = LocationCountStatus.Pending 
    };
    
    // Act
    location.StartCount("COUNTER001");
    
    // Assert
    Assert.That(location.Status, Is.EqualTo(LocationCountStatus.InProgress));
    Assert.That(location.CountedBy, Is.EqualTo("COUNTER001"));
    Assert.That(location.StartTime, Is.Not.Null);
}

[Test]
public void CompleteCount_WhenCountingComplete_ShouldCompleteSuccessfully()
{
    // Arrange
    var location = new WarehouseLocation 
    { 
        Status = LocationCountStatus.CountingComplete 
    };
    
    // Act
    location.CompleteCount("COUNTER001");
    
    // Assert
    Assert.That(location.Status, Is.EqualTo(LocationCountStatus.Completed));
    Assert.That(location.CompletionTime, Is.Not.Null);
}

[Test]
public void ResetCount_ShouldClearCountingData()
{
    // Arrange
    var location = new WarehouseLocation 
    { 
        Status = LocationCountStatus.InProgress,
        CountedBy = "COUNTER001",
        StartTime = DateTime.UtcNow
    };
    
    // Act
    location.ResetCount();
    
    // Assert
    Assert.That(location.Status, Is.EqualTo(LocationCountStatus.Pending));
    Assert.That(location.CountedBy, Is.Null);
    Assert.That(location.StartTime, Is.Null);
}
```

### StockCountRecord Entity Tests

#### Count Recording
```csharp
[Test]
public void RecordCount_WithValidData_ShouldUpdateRecord()
{
    // Arrange
    var record = new StockCountRecord 
    { 
        SystemQuantity = 10,
        Status = CountRecordStatus.Pending
    };
    
    // Act
    record.RecordCount(10, "COUNTER001", "Perfect condition");
    
    // Assert
    Assert.That(record.Status, Is.EqualTo(CountRecordStatus.Counted));
    Assert.That(record.CountedQuantity, Is.EqualTo(10));
    Assert.That(record.CountedBy, Is.EqualTo("COUNTER001"));
    Assert.That(record.IsAccurate, Is.True);
    Assert.That(record.HasVariance, Is.False);
}

[Test]
public void RecordCount_WithVariance_ShouldCalculateCorrectly()
{
    // Arrange
    var record = new StockCountRecord 
    { 
        SystemQuantity = 20,
        Status = CountRecordStatus.Pending
    };
    
    // Act
    record.RecordCount(18, "COUNTER001");
    
    // Assert
    Assert.That(record.Variance, Is.EqualTo(-2));
    Assert.That(record.VariancePercentage, Is.EqualTo(-10));
    Assert.That(record.HasVariance, Is.True);
}

[Test]
public void RecordCount_WhenAlreadyCounted_ShouldThrowException()
{
    // Arrange
    var record = new StockCountRecord 
    { 
        Status = CountRecordStatus.Counted
    };
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        record.RecordCount(10, "COUNTER001"));
}
```

#### Validation and Status Management
```csharp
[Test]
public void ValidateRecord_WithIssues_ShouldIdentifyProblems()
{
    // Arrange
    var record = new StockCountRecord 
    { 
        SystemQuantity = 10,
        CountedQuantity = -5, // Negative quantity
        ExpirationDate = DateTime.Today.AddDays(-1) // Expired
    };
    
    // Act
    record.ValidateRecord();
    
    // Assert
    Assert.That(record.ValidationMessage, Is.Not.Null);
    Assert.That(record.ValidationMessage, Does.Contain("expired"));
    Assert.That(record.ValidationMessage, Does.Contain("Negative quantity"));
}

[Test]
public void SkipCount_ShouldUpdateStatusAndReason()
{
    // Arrange
    var record = new StockCountRecord 
    { 
        Status = CountRecordStatus.Pending
    };
    
    // Act
    record.SkipCount("Item damaged and unreachable");
    
    // Assert
    Assert.That(record.Status, Is.EqualTo(CountRecordStatus.Skipped));
    Assert.That(record.Notes, Is.EqualTo("Skipped: Item damaged and unreachable"));
}

[Test]
public void ResetCount_ShouldClearCountingData()
{
    // Arrange
    var record = new StockCountRecord 
    { 
        Status = CountRecordStatus.Counted,
        CountedQuantity = 10,
        CountedBy = "COUNTER001"
    };
    
    // Act
    record.ResetCount();
    
    // Assert
    Assert.That(record.Status, Is.EqualTo(CountRecordStatus.Pending));
    Assert.That(record.CountedQuantity, Is.Null);
    Assert.That(record.CountedBy, Is.Null);
}
```

#### Expiration Date Management
```csharp
[Test]
public void IsExpired_WhenDatePassed_ShouldReturnTrue()
{
    // Arrange
    var record = new StockCountRecord 
    { 
        ExpirationDate = DateTime.Today.AddDays(-1)
    };
    
    // Act & Assert
    Assert.That(record.IsExpired, Is.True);
}

[Test]
public void IsNearExpiry_WhenWithinThirtyDays_ShouldReturnTrue()
{
    // Arrange
    var record = new StockCountRecord 
    { 
        ExpirationDate = DateTime.Today.AddDays(15)
    };
    
    // Act & Assert
    Assert.That(record.IsNearExpiry, Is.True);
}

[Test]
public void LocationDisplay_ForTransportBoxItem_ShouldShowBoxFormat()
{
    // Arrange
    var record = new StockCountRecord 
    { 
        LocationCode = "BOX-TB001",
        IsTransportBoxItem = true
    };
    
    // Act & Assert
    Assert.That(record.LocationDisplay, Is.EqualTo("Transport Box: BOX-TB001"));
}
```

### CountingAssignment Entity Tests

#### Assignment Lifecycle
```csharp
[Test]
public void StartWork_WhenAssigned_ShouldStartSuccessfully()
{
    // Arrange
    var assignment = new CountingAssignment 
    { 
        Status = AssignmentStatus.Assigned 
    };
    
    // Act
    assignment.StartWork();
    
    // Assert
    Assert.That(assignment.Status, Is.EqualTo(AssignmentStatus.InProgress));
    Assert.That(assignment.StartTime, Is.Not.Null);
}

[Test]
public void CompleteAssignment_ShouldUpdateStatusAndTime()
{
    // Arrange
    var assignment = new CountingAssignment 
    { 
        Status = AssignmentStatus.InProgress,
        StartTime = DateTime.UtcNow.AddHours(-2)
    };
    
    // Act
    assignment.CompleteAssignment();
    
    // Assert
    Assert.That(assignment.Status, Is.EqualTo(AssignmentStatus.Completed));
    Assert.That(assignment.CompletionTime, Is.Not.Null);
    Assert.That(assignment.WorkingTime, Is.Not.Null);
}

[Test]
public void ReassignCounter_ShouldUpdateCounterInfo()
{
    // Arrange
    var assignment = new CountingAssignment 
    { 
        CounterId = "COUNTER001",
        CounterName = "John Doe"
    };
    
    // Act
    assignment.ReassignCounter("COUNTER002", "Jane Smith");
    
    // Assert
    Assert.That(assignment.CounterId, Is.EqualTo("COUNTER002"));
    Assert.That(assignment.CounterName, Is.EqualTo("Jane Smith"));
}
```

### StockDiscrepancy Entity Tests

#### Discrepancy Management
```csharp
[Test]
public void CategorizeDiscrepancy_ShouldUpdateCategory()
{
    // Arrange
    var discrepancy = new StockDiscrepancy 
    { 
        Category = DiscrepancyCategory.Unknown 
    };
    
    // Act
    discrepancy.CategorizeDiscrepancy(DiscrepancyCategory.CountingError);
    
    // Assert
    Assert.That(discrepancy.Category, Is.EqualTo(DiscrepancyCategory.CountingError));
}

[Test]
public void ResolveDiscrepancy_ShouldUpdateResolutionInfo()
{
    // Arrange
    var discrepancy = new StockDiscrepancy 
    { 
        Status = DiscrepancyStatus.Identified
    };
    
    // Act
    discrepancy.ResolveDiscrepancy("Recounted - system error", "SUPERVISOR001", DiscrepancyCategory.SystemError);
    
    // Assert
    Assert.That(discrepancy.Status, Is.EqualTo(DiscrepancyStatus.Resolved));
    Assert.That(discrepancy.Resolution, Is.EqualTo("Recounted - system error"));
    Assert.That(discrepancy.ResolvedBy, Is.EqualTo("SUPERVISOR001"));
    Assert.That(discrepancy.Category, Is.EqualTo(DiscrepancyCategory.SystemError));
    Assert.That(discrepancy.ResolvedDate, Is.Not.Null);
}

[Test]
public void UpdateDiscrepancy_ShouldRecalculateSignificance()
{
    // Arrange
    var discrepancy = new StockDiscrepancy 
    { 
        Variance = -2,
        VariancePercentage = -10,
        IsSignificant = true
    };
    
    // Act
    discrepancy.UpdateDiscrepancy(-1, -2); // Reduce variance to 2%
    
    // Assert
    Assert.That(discrepancy.Variance, Is.EqualTo(-1));
    Assert.That(discrepancy.VariancePercentage, Is.EqualTo(-2));
    Assert.That(discrepancy.IsSignificant, Is.False); // Below 5% threshold
}
```

#### Variance Analysis
```csharp
[Test]
public void VarianceDirection_WithPositiveVariance_ShouldShowOverage()
{
    // Arrange
    var discrepancy = new StockDiscrepancy 
    { 
        Variance = 5 
    };
    
    // Act & Assert
    Assert.That(discrepancy.IsIncrease, Is.True);
    Assert.That(discrepancy.VarianceDirection, Is.EqualTo("Overage"));
}

[Test]
public void VarianceDirection_WithNegativeVariance_ShouldShowShortage()
{
    // Arrange
    var discrepancy = new StockDiscrepancy 
    { 
        Variance = -3 
    };
    
    // Act & Assert
    Assert.That(discrepancy.IsDecrease, Is.True);
    Assert.That(discrepancy.VarianceDirection, Is.EqualTo("Shortage"));
}
```

## Integration Tests

### Application Service Tests

#### Stock Taking Creation and Management
```csharp
[Test]
public async Task CreateStockTakingAsync_WithValidInput_ShouldCreateStockTaking()
{
    // Arrange
    var input = new CreateWarehouseStockTakingDto
    {
        StockTakingName = "Monthly Warehouse Count",
        Type = WarehouseStockTakingType.Full,
        PlannedDate = DateTime.Today.AddDays(1),
        WarehouseCode = "WH001",
        IncludeTransportBoxes = true,
        LocationCodes = new List<string> { "A-15-B", "B-20-C", "C-10-A" }
    };
    
    // Act
    var result = await _stockTakingService.CreateStockTakingAsync(input);
    
    // Assert
    Assert.That(result.StockTakingName, Is.EqualTo("Monthly Warehouse Count"));
    Assert.That(result.TotalLocations, Is.EqualTo(3));
    Assert.That(result.Status, Is.EqualTo(WarehouseStockTakingStatus.Planned));
}

[Test]
public async Task StartStockTakingAsync_WithValidStockTaking_ShouldStartSuccessfully()
{
    // Arrange
    var stockTaking = await CreateTestStockTaking();
    
    // Act
    var result = await _stockTakingService.StartStockTakingAsync(stockTaking.Id);
    
    // Assert
    Assert.That(result.Status, Is.EqualTo(WarehouseStockTakingStatus.InProgress));
    Assert.That(result.TotalItems, Is.GreaterThan(0));
    Assert.That(result.StartDate, Is.Not.Null);
}
```

#### Counter Assignment and Count Recording
```csharp
[Test]
public async Task AssignCounterAsync_WithValidAssignment_ShouldCreateAssignment()
{
    // Arrange
    var stockTaking = await CreateActiveStockTaking();
    var location = stockTaking.Locations.First();
    
    var assignmentDto = new AssignCounterDto
    {
        LocationCode = location.LocationCode,
        CounterId = "COUNTER001",
        CounterName = "John Doe"
    };
    
    // Act
    var result = await _stockTakingService.AssignCounterAsync(stockTaking.Id, assignmentDto);
    
    // Assert
    Assert.That(result.Assignments.Count, Is.EqualTo(1));
    Assert.That(result.Assignments.First().CounterId, Is.EqualTo("COUNTER001"));
}

[Test]
public async Task RecordCountAsync_WithValidCount_ShouldUpdateRecord()
{
    // Arrange
    var stockTaking = await CreateActiveStockTakingWithRecords();
    var record = stockTaking.CountRecords.First();
    
    var countDto = new RecordCountDto
    {
        LocationCode = record.LocationCode,
        ProductCode = record.ProductCode,
        CountedQuantity = 15,
        LotNumber = record.LotNumber,
        Notes = "Good condition"
    };
    
    // Act
    var result = await _stockTakingService.RecordCountAsync(stockTaking.Id, countDto);
    
    // Assert
    var updatedRecord = result.CountRecords.First(r => r.ProductCode == record.ProductCode);
    Assert.That(updatedRecord.Status, Is.EqualTo(CountRecordStatus.Counted));
    Assert.That(updatedRecord.CountedQuantity, Is.EqualTo(15));
}
```

#### Location and Transport Box Integration
```csharp
[Test]
public async Task GetWarehouseLocationsAsync_ShouldReturnFilteredLocations()
{
    // Arrange & Act
    var locations = await _stockTakingService.GetWarehouseLocationsAsync("WH001", "Zone A");
    
    // Assert
    Assert.That(locations.Count, Is.GreaterThan(0));
    Assert.That(locations.All(l => l.Zone == "Zone A"));
}

[Test]
public async Task GetLocationInventoryAsync_ShouldReturnCurrentInventory()
{
    // Arrange
    var locationCode = "A-15-B";
    
    // Act
    var inventory = await _stockTakingService.GetLocationInventoryAsync(locationCode);
    
    // Assert
    Assert.That(inventory.Count, Is.GreaterThan(0));
    Assert.That(inventory.All(i => !string.IsNullOrEmpty(i.ProductCode)));
}

[Test]
public async Task GetWarehouseTransportBoxesAsync_ShouldReturnActiveBoxes()
{
    // Arrange
    var warehouseCode = "WH001";
    
    // Act
    var transportBoxes = await _stockTakingService.GetWarehouseTransportBoxesAsync(warehouseCode);
    
    // Assert
    Assert.That(transportBoxes.Count, Is.GreaterThanOrEqualTo(0));
    Assert.That(transportBoxes.All(b => b.IsActive));
}
```

#### Progress Tracking and Reporting
```csharp
[Test]
public async Task GetProgressReportAsync_ShouldProvideDetailedProgress()
{
    // Arrange
    var stockTaking = await CreatePartiallyCompletedStockTaking();
    
    // Act
    var report = await _stockTakingService.GetProgressReportAsync(stockTaking.Id);
    
    // Assert
    Assert.That(report.OverallProgress, Is.InRange(0, 100));
    Assert.That(report.LocationProgress.Count, Is.EqualTo(stockTaking.TotalLocations));
    Assert.That(report.DiscrepancySummary, Is.Not.Null);
}

[Test]
public async Task GetSummaryAsync_ShouldProvideComprehensiveSummary()
{
    // Arrange
    var stockTaking = await CreateCompletedStockTaking();
    
    // Act
    var summary = await _stockTakingService.GetSummaryAsync(stockTaking.Id);
    
    // Assert
    Assert.That(summary.IsCompleted, Is.True);
    Assert.That(summary.LocationCompletionPercentage, Is.EqualTo(100));
    Assert.That(summary.LocationsByZone.Count, Is.GreaterThan(0));
}
```

### Performance Tests

#### Large Scale Stock Taking
```csharp
[Test]
public async Task CreateLargeStockTaking_ShouldCompleteWithinTimeLimit()
{
    // Arrange
    var largeInput = CreateLargeStockTakingInput(1000); // 1000 locations
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var result = await _stockTakingService.CreateStockTakingAsync(largeInput);
    stopwatch.Stop();
    
    // Assert
    Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(10000)); // 10 second limit
    Assert.That(result.TotalLocations, Is.EqualTo(1000));
}

[Test]
public async Task StartLargeStockTaking_ShouldInitializeRecordsEfficiently()
{
    // Arrange
    var stockTaking = await CreateLargeStockTaking(500);
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var result = await _stockTakingService.StartStockTakingAsync(stockTaking.Id);
    stopwatch.Stop();
    
    // Assert
    Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(15000)); // 15 second limit
    Assert.That(result.TotalItems, Is.GreaterThan(5000)); // Assume average 10+ items per location
}
```

#### Concurrent Count Recording
```csharp
[Test]
public async Task ConcurrentCountRecording_ShouldHandleCorrectly()
{
    // Arrange
    var stockTaking = await CreateActiveStockTakingWithManyRecords();
    var records = stockTaking.CountRecords.Take(20).ToList();
    
    // Act
    var tasks = records.Select(async (record, index) => 
    {
        var countDto = new RecordCountDto 
        { 
            LocationCode = record.LocationCode,
            ProductCode = record.ProductCode,
            CountedQuantity = record.SystemQuantity + index, // Vary quantities
            LotNumber = record.LotNumber
        };
        return await _stockTakingService.RecordCountAsync(stockTaking.Id, countDto);
    });
    
    var results = await Task.WhenAll(tasks);
    
    // Assert
    Assert.That(results.Length, Is.EqualTo(20));
    Assert.That(results.All(r => r != null));
    
    // Verify final state is consistent
    var finalStockTaking = await _stockTakingService.GetStockTakingAsync(stockTaking.Id);
    Assert.That(finalStockTaking.CountedItems, Is.EqualTo(20));
}
```

### End-to-End Tests

#### Complete Stock Taking Workflow
```csharp
[Test]
public async Task CompleteStockTakingWorkflow_ShouldProcessSuccessfully()
{
    // Arrange - Create stock taking with multiple locations
    var createInput = new CreateWarehouseStockTakingDto
    {
        StockTakingName = "E2E Test Stock Taking",
        Type = WarehouseStockTakingType.Full,
        PlannedDate = DateTime.Today.AddHours(8),
        WarehouseCode = "WH001",
        IncludeTransportBoxes = true,
        LocationCodes = new List<string> { "A-15-B", "B-20-C", "C-10-A" }
    };
    
    // Act & Assert - Create stock taking
    var stockTaking = await _stockTakingService.CreateStockTakingAsync(createInput);
    Assert.That(stockTaking.Status, Is.EqualTo(WarehouseStockTakingStatus.Planned));
    
    // Act & Assert - Start stock taking
    stockTaking = await _stockTakingService.StartStockTakingAsync(stockTaking.Id);
    Assert.That(stockTaking.Status, Is.EqualTo(WarehouseStockTakingStatus.InProgress));
    Assert.That(stockTaking.TotalItems, Is.GreaterThan(0));
    
    // Act & Assert - Assign counters to locations
    foreach (var location in stockTaking.Locations)
    {
        var assignmentDto = new AssignCounterDto
        {
            LocationCode = location.LocationCode,
            CounterId = $"COUNTER_{location.LocationCode}",
            CounterName = $"Counter for {location.LocationCode}"
        };
        stockTaking = await _stockTakingService.AssignCounterAsync(stockTaking.Id, assignmentDto);
    }
    Assert.That(stockTaking.Assignments.Count, Is.EqualTo(stockTaking.TotalLocations));
    
    // Act & Assert - Record counts for all items
    foreach (var record in stockTaking.CountRecords)
    {
        var countDto = new RecordCountDto
        {
            LocationCode = record.LocationCode,
            ProductCode = record.ProductCode,
            CountedQuantity = record.SystemQuantity, // Perfect count
            LotNumber = record.LotNumber
        };
        stockTaking = await _stockTakingService.RecordCountAsync(stockTaking.Id, countDto);
    }
    Assert.That(stockTaking.CountedItems, Is.EqualTo(stockTaking.TotalItems));
    
    // Act & Assert - Complete locations
    foreach (var location in stockTaking.Locations)
    {
        var completeDto = new CompleteLocationDto { LocationCode = location.LocationCode };
        stockTaking = await _stockTakingService.CompleteLocationAsync(stockTaking.Id, completeDto);
    }
    Assert.That(stockTaking.CompletedLocations, Is.EqualTo(stockTaking.TotalLocations));
    
    // Act & Assert - Complete stock taking
    stockTaking = await _stockTakingService.CompleteStockTakingAsync(stockTaking.Id);
    Assert.That(stockTaking.Status, Is.EqualTo(WarehouseStockTakingStatus.Completed));
    Assert.That(stockTaking.CompletionPercentage, Is.EqualTo(100));
    
    // Act & Assert - Validate stock taking
    stockTaking = await _stockTakingService.ValidateStockTakingAsync(stockTaking.Id);
    Assert.That(stockTaking.Status, Is.EqualTo(WarehouseStockTakingStatus.Validated));
    
    // Verify summary
    var summary = await _stockTakingService.GetSummaryAsync(stockTaking.Id);
    Assert.That(summary.IsCompleted, Is.True);
    Assert.That(summary.OverallHealth, Is.EqualTo("Healthy"));
}
```

#### Stock Taking with Discrepancies
```csharp
[Test]
public async Task StockTakingWithDiscrepancies_ShouldHandleCorrectly()
{
    // Arrange
    var stockTaking = await CreateActiveStockTakingWithRecords();
    
    // Act - Record counts with some variances
    var recordsWithVariance = stockTaking.CountRecords.Take(3).ToList();
    
    foreach (var (record, index) in recordsWithVariance.Select((r, i) => (r, i)))
    {
        var countDto = new RecordCountDto
        {
            LocationCode = record.LocationCode,
            ProductCode = record.ProductCode,
            CountedQuantity = index switch
            {
                0 => record.SystemQuantity - 5, // Shortage
                1 => record.SystemQuantity + 3, // Overage
                2 => record.SystemQuantity * 0.8m, // Significant variance
                _ => record.SystemQuantity
            },
            LotNumber = record.LotNumber,
            Notes = $"Variance test {index}"
        };
        stockTaking = await _stockTakingService.RecordCountAsync(stockTaking.Id, countDto);
    }
    
    // Record remaining items normally
    var remainingRecords = stockTaking.CountRecords.Skip(3);
    foreach (var record in remainingRecords)
    {
        var countDto = new RecordCountDto
        {
            LocationCode = record.LocationCode,
            ProductCode = record.ProductCode,
            CountedQuantity = record.SystemQuantity,
            LotNumber = record.LotNumber
        };
        stockTaking = await _stockTakingService.RecordCountAsync(stockTaking.Id, countDto);
    }
    
    // Complete stock taking
    foreach (var location in stockTaking.Locations)
    {
        await _stockTakingService.CompleteLocationAsync(stockTaking.Id, 
            new CompleteLocationDto { LocationCode = location.LocationCode });
    }
    stockTaking = await _stockTakingService.CompleteStockTakingAsync(stockTaking.Id);
    
    // Assert discrepancies were created
    var discrepancies = await _stockTakingService.GetDiscrepanciesAsync(stockTaking.Id);
    Assert.That(discrepancies.Count, Is.EqualTo(3));
    Assert.That(discrepancies.Any(d => d.IsSignificant), Is.True);
    
    // Resolve discrepancies
    foreach (var discrepancy in discrepancies)
    {
        var resolveDto = new ResolveDiscrepancyDto
        {
            Resolution = "Investigated and resolved",
            Category = DiscrepancyCategory.CountingError
        };
        await _stockTakingService.ResolveDiscrepancyAsync(discrepancy.Id, resolveDto);
    }
    
    // Verify resolution
    var resolvedDiscrepancies = await _stockTakingService.GetDiscrepanciesAsync(stockTaking.Id);
    Assert.That(resolvedDiscrepancies.All(d => d.IsResolved), Is.True);
}
```

## Test Data Builders

### Stock Taking Builder
```csharp
public class WarehouseStockTakingBuilder
{
    private WarehouseStockTaking _stockTaking = new() { Id = Guid.NewGuid() };
    
    public WarehouseStockTakingBuilder WithStatus(WarehouseStockTakingStatus status)
    {
        _stockTaking.Status = status;
        return this;
    }
    
    public WarehouseStockTakingBuilder WithLocation(string locationCode, string locationName, string? zone = null)
    {
        _stockTaking.AddLocation(locationCode, locationName, zone);
        return this;
    }
    
    public WarehouseStockTakingBuilder WithCountRecord(string locationCode, string productCode, decimal systemQty)
    {
        var record = new StockCountRecord
        {
            Id = Guid.NewGuid(),
            StockTakingId = _stockTaking.Id,
            LocationCode = locationCode,
            ProductCode = productCode,
            ProductName = $"Product {productCode}",
            SystemQuantity = systemQty,
            Status = CountRecordStatus.Pending
        };
        _stockTaking.CountRecords.Add(record);
        return this;
    }
    
    public WarehouseStockTaking Build() => _stockTaking;
}
```

### Performance Test Data
```csharp
public static class WarehouseStockTakingTestDataGenerator
{
    public static CreateWarehouseStockTakingDto CreateLargeStockTakingInput(int locationCount)
    {
        var locationCodes = new List<string>();
        
        for (int i = 0; i < locationCount; i++)
        {
            var zone = (char)('A' + (i / 100));
            var rack = (i % 100) + 1;
            var shelf = (i % 10) + 1;
            locationCodes.Add($"{zone}-{rack:D2}-{shelf}");
        }
        
        return new CreateWarehouseStockTakingDto
        {
            StockTakingName = $"Large Stock Taking {locationCount} locations",
            Type = WarehouseStockTakingType.Full,
            PlannedDate = DateTime.Today.AddDays(1),
            WarehouseCode = "WH001",
            IncludeTransportBoxes = true,
            LocationCodes = locationCodes
        };
    }
    
    public static List<StockCountRecord> GenerateCountRecords(int count, Guid stockTakingId)
    {
        var records = new List<StockCountRecord>();
        
        for (int i = 0; i < count; i++)
        {
            records.Add(new StockCountRecord
            {
                Id = Guid.NewGuid(),
                StockTakingId = stockTakingId,
                LocationCode = $"A-{(i / 20) + 1:D2}-{(i % 10) + 1}",
                ProductCode = $"PROD{i:D6}",
                ProductName = $"Product {i}",
                SystemQuantity = Random.Shared.Next(1, 100),
                Status = CountRecordStatus.Pending
            });
        }
        
        return records;
    }
}