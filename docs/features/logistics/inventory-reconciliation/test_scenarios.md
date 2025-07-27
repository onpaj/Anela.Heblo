# Test Scenarios: Inventory Reconciliation

## Unit Tests

### InventoryReconciliation Aggregate Tests

#### Reconciliation Creation and Configuration
```csharp
[Test]
public void StartReconciliation_WhenPlanned_ShouldStartSuccessfully()
{
    // Arrange
    var reconciliation = new InventoryReconciliation 
    { 
        Id = Guid.NewGuid(),
        Status = ReconciliationStatus.Planned 
    };
    
    // Act
    reconciliation.StartReconciliation("USER001");
    
    // Assert
    Assert.That(reconciliation.Status, Is.EqualTo(ReconciliationStatus.InProgress));
    Assert.That(reconciliation.ResponsibleUserId, Is.EqualTo("USER001"));
    Assert.That(reconciliation.StartDate, Is.Not.Null);
    Assert.That(reconciliation.Snapshots.Count, Is.GreaterThan(0));
}

[Test]
public void StartReconciliation_WhenNotPlanned_ShouldThrowException()
{
    // Arrange
    var reconciliation = new InventoryReconciliation 
    { 
        Status = ReconciliationStatus.InProgress 
    };
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        reconciliation.StartReconciliation("USER001"));
}

[Test]
public void AddReconciliationRule_ShouldCreateRule()
{
    // Arrange
    var reconciliation = new InventoryReconciliation { Id = Guid.NewGuid() };
    
    // Act
    reconciliation.AddReconciliationRule("HV-*", 10.0m, 1.0m, ReconciliationActionType.Investigate);
    
    // Assert
    Assert.That(reconciliation.Rules.Count, Is.EqualTo(1));
    Assert.That(reconciliation.Rules.First().ProductPattern, Is.EqualTo("HV-*"));
    Assert.That(reconciliation.Rules.First().ToleranceAmount, Is.EqualTo(10.0m));
    Assert.That(reconciliation.Rules.First().TolerancePercentage, Is.EqualTo(1.0m));
    Assert.That(reconciliation.Rules.First().AutoAction, Is.EqualTo(ReconciliationActionType.Investigate));
}
```

#### Variance Detection and Management
```csharp
[Test]
public void AddVariance_WithNoActualVariance_ShouldNotCreateVariance()
{
    // Arrange
    var reconciliation = new InventoryReconciliation 
    { 
        Id = Guid.NewGuid(),
        Status = ReconciliationStatus.InProgress 
    };
    
    // Act
    reconciliation.AddVariance("PROD001", "Warehouse", "System", 100, 100, "A-15-B", VarianceCategory.Regular);
    
    // Assert
    Assert.That(reconciliation.Variances.Count, Is.EqualTo(0));
}

[Test]
public void AddVariance_WithActualVariance_ShouldCreateVariance()
{
    // Arrange
    var reconciliation = new InventoryReconciliation 
    { 
        Id = Guid.NewGuid(),
        Status = ReconciliationStatus.InProgress 
    };
    
    // Act
    reconciliation.AddVariance("PROD001", "Warehouse", "System", 100, 95, "A-15-B", VarianceCategory.Regular);
    
    // Assert
    Assert.That(reconciliation.Variances.Count, Is.EqualTo(1));
    Assert.That(reconciliation.Variances.First().ProductCode, Is.EqualTo("PROD001"));
    Assert.That(reconciliation.Variances.First().Variance, Is.EqualTo(5));
    Assert.That(reconciliation.Variances.First().VariancePercentage, Is.EqualTo(5));
    Assert.That(reconciliation.TotalVariances, Is.EqualTo(1));
    Assert.That(reconciliation.PendingVariances, Is.EqualTo(1));
}

[Test]
public void AddVariance_WithAutoResolutionRule_ShouldAutoResolve()
{
    // Arrange
    var reconciliation = new InventoryReconciliation 
    { 
        Id = Guid.NewGuid(),
        Status = ReconciliationStatus.InProgress 
    };
    
    // Add auto-resolution rule for variances <= 2 units or <= 2%
    reconciliation.AddReconciliationRule("*", 2.0m, 2.0m, ReconciliationActionType.AutoResolve);
    
    // Act
    reconciliation.AddVariance("PROD001", "Warehouse", "System", 100, 99, "A-15-B", VarianceCategory.Regular);
    
    // Assert
    Assert.That(reconciliation.Variances.Count, Is.EqualTo(1));
    Assert.That(reconciliation.Variances.First().Status, Is.EqualTo(VarianceStatus.Resolved));
    Assert.That(reconciliation.Actions.Count, Is.EqualTo(1));
    Assert.That(reconciliation.Actions.First().ActionType, Is.EqualTo(ReconciliationActionType.AutoResolve));
    Assert.That(reconciliation.AutoResolvedVariances, Is.EqualTo(1));
}

[Test]
public void AddVariance_WithSignificantVariance_ShouldMarkAsSignificant()
{
    // Arrange
    var reconciliation = new InventoryReconciliation 
    { 
        Id = Guid.NewGuid(),
        Status = ReconciliationStatus.InProgress 
    };
    
    // Act - 20% variance should be significant for high-value category
    reconciliation.AddVariance("HV-PROD001", "Warehouse", "System", 1000, 800, "A-15-B", VarianceCategory.HighValue);
    
    // Assert
    Assert.That(reconciliation.Variances.First().IsSignificant, Is.True);
    Assert.That(reconciliation.HasSignificantVariances, Is.True);
}
```

#### Variance Resolution
```csharp
[Test]
public void ResolveVariance_WithValidVariance_ShouldResolveSuccessfully()
{
    // Arrange
    var reconciliation = CreateReconciliationWithVariance();
    var variance = reconciliation.Variances.First();
    
    // Act
    reconciliation.ResolveVariance(variance.Id, ReconciliationActionType.ManualResolve, "USER001", "Manual count confirmed", "Warehouse");
    
    // Assert
    Assert.That(variance.Status, Is.EqualTo(VarianceStatus.Resolved));
    Assert.That(variance.ResolvedBy, Is.EqualTo("USER001"));
    Assert.That(variance.Resolution, Is.EqualTo("Manual count confirmed"));
    Assert.That(reconciliation.Actions.Count, Is.EqualTo(1));
    Assert.That(reconciliation.ResolvedVariances, Is.EqualTo(1));
    Assert.That(reconciliation.PendingVariances, Is.EqualTo(0));
}

[Test]
public void ResolveVariance_WhenAlreadyResolved_ShouldThrowException()
{
    // Arrange
    var reconciliation = CreateReconciliationWithResolvedVariance();
    var variance = reconciliation.Variances.First();
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        reconciliation.ResolveVariance(variance.Id, ReconciliationActionType.ManualResolve, "USER001", "Duplicate resolution"));
}

[Test]
public void ResolveVariance_WithNonExistentVariance_ShouldThrowException()
{
    // Arrange
    var reconciliation = new InventoryReconciliation { Id = Guid.NewGuid() };
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        reconciliation.ResolveVariance(Guid.NewGuid(), ReconciliationActionType.ManualResolve, "USER001", "Resolution"));
}
```

#### Reconciliation Completion
```csharp
[Test]
public void CompleteReconciliation_WhenAllVariancesResolved_ShouldCompleteSuccessfully()
{
    // Arrange
    var reconciliation = CreateReconciliationWithResolvedVariances();
    
    // Act
    reconciliation.CompleteReconciliation();
    
    // Assert
    Assert.That(reconciliation.Status, Is.EqualTo(ReconciliationStatus.Completed));
    Assert.That(reconciliation.CompletionDate, Is.Not.Null);
    Assert.That(reconciliation.IsCompleted, Is.True);
}

[Test]
public void CompleteReconciliation_WithPendingVariances_ShouldThrowException()
{
    // Arrange
    var reconciliation = CreateReconciliationWithPendingVariances();
    
    // Act & Assert
    Assert.Throws<BusinessException>(() => 
        reconciliation.CompleteReconciliation());
}

[Test]
public void ApproveReconciliation_WhenCompleted_ShouldApproveSuccessfully()
{
    // Arrange
    var reconciliation = CreateCompletedReconciliation();
    
    // Act
    reconciliation.ApproveReconciliation("SUPERVISOR001");
    
    // Assert
    Assert.That(reconciliation.Status, Is.EqualTo(ReconciliationStatus.Approved));
    Assert.That(reconciliation.Notes, Does.Contain("SUPERVISOR001"));
}
```

#### Summary and Analytics
```csharp
[Test]
public void GetSummary_ShouldCalculateCorrectStatistics()
{
    // Arrange
    var reconciliation = CreateReconciliationWithMixedVariances();
    
    // Act
    var summary = reconciliation.GetSummary();
    
    // Assert
    Assert.That(summary.ReconciliationId, Is.EqualTo(reconciliation.Id));
    Assert.That(summary.TotalVariances, Is.EqualTo(reconciliation.TotalVariances));
    Assert.That(summary.ResolvedVariances, Is.EqualTo(reconciliation.ResolvedVariances));
    Assert.That(summary.ResolutionPercentage, Is.EqualTo(reconciliation.ResolutionPercentage));
    Assert.That(summary.VariancesByCategory.Count, Is.GreaterThan(0));
    Assert.That(summary.VariancesBySource.Count, Is.GreaterThan(0));
}

[Test]
public void GetSummary_WithNoVariances_ShouldShowCompleteResolution()
{
    // Arrange
    var reconciliation = CreateCompletedReconciliationWithoutVariances();
    
    // Act
    var summary = reconciliation.GetSummary();
    
    // Assert
    Assert.That(summary.ResolutionPercentage, Is.EqualTo(100));
    Assert.That(summary.OverallHealth, Is.EqualTo("Healthy"));
    Assert.That(summary.HasIssues, Is.False);
}
```

### InventoryVariance Entity Tests

#### Variance Properties and Calculations
```csharp
[Test]
public void VarianceDirection_WithPositiveVariance_ShouldShowIncrease()
{
    // Arrange
    var variance = new InventoryVariance 
    { 
        Quantity1 = 105,
        Quantity2 = 100,
        Variance = 5
    };
    
    // Act & Assert
    Assert.That(variance.IsIncrease, Is.True);
    Assert.That(variance.IsDecrease, Is.False);
    Assert.That(variance.VarianceDirection, Is.EqualTo("Increase"));
    Assert.That(variance.AbsoluteVariance, Is.EqualTo(5));
}

[Test]
public void VarianceDirection_WithNegativeVariance_ShouldShowDecrease()
{
    // Arrange
    var variance = new InventoryVariance 
    { 
        Quantity1 = 95,
        Quantity2 = 100,
        Variance = -5
    };
    
    // Act & Assert
    Assert.That(variance.IsIncrease, Is.False);
    Assert.That(variance.IsDecrease, Is.True);
    Assert.That(variance.VarianceDirection, Is.EqualTo("Decrease"));
    Assert.That(variance.AbsoluteVariance, Is.EqualTo(5));
}
```

#### Variance Status Management
```csharp
[Test]
public void Resolve_ShouldUpdateResolutionInfo()
{
    // Arrange
    var variance = new InventoryVariance 
    { 
        Status = VarianceStatus.Identified 
    };
    
    // Act
    variance.Resolve("USER001", "Manual recount confirmed variance");
    
    // Assert
    Assert.That(variance.Status, Is.EqualTo(VarianceStatus.Resolved));
    Assert.That(variance.ResolvedBy, Is.EqualTo("USER001"));
    Assert.That(variance.Resolution, Is.EqualTo("Manual recount confirmed variance"));
    Assert.That(variance.ResolvedDate, Is.Not.Null);
    Assert.That(variance.IsResolved, Is.True);
}

[Test]
public void Investigate_ShouldUpdateStatusAndNotes()
{
    // Arrange
    var variance = new InventoryVariance 
    { 
        Status = VarianceStatus.Identified 
    };
    
    // Act
    variance.Investigate("INVESTIGATOR001", "Found damaged items not recorded in system");
    
    // Assert
    Assert.That(variance.Status, Is.EqualTo(VarianceStatus.Investigating));
    Assert.That(variance.Notes, Does.Contain("INVESTIGATOR001"));
    Assert.That(variance.Notes, Does.Contain("damaged items"));
}

[Test]
public void Escalate_ShouldMarkAsSignificantAndEscalated()
{
    // Arrange
    var variance = new InventoryVariance 
    { 
        Status = VarianceStatus.Identified,
        IsSignificant = false
    };
    
    // Act
    variance.Escalate("Complex variance requiring management review");
    
    // Assert
    Assert.That(variance.Status, Is.EqualTo(VarianceStatus.Escalated));
    Assert.That(variance.IsSignificant, Is.True);
    Assert.That(variance.Notes, Does.Contain("Escalated"));
}

[Test]
public void SetRootCause_ShouldUpdateCauseAndNotes()
{
    // Arrange
    var variance = new InventoryVariance();
    
    // Act
    variance.SetRootCause("Receiving error - items not properly logged", "Need to improve receiving process");
    
    // Assert
    Assert.That(variance.RootCause, Is.EqualTo("Receiving error - items not properly logged"));
    Assert.That(variance.Notes, Is.EqualTo("Need to improve receiving process"));
}

[Test]
public void UpdateValue_ShouldCalculateVarianceValue()
{
    // Arrange
    var variance = new InventoryVariance 
    { 
        Variance = 5 
    };
    
    // Act
    variance.UpdateValue(25.50m); // $25.50 per unit
    
    // Assert
    Assert.That(variance.VarianceValue, Is.EqualTo(127.50m)); // 5 units * $25.50
}
```

### ReconciliationAction Entity Tests

#### Action Status Management
```csharp
[Test]
public void MarkCompleted_ShouldUpdateStatusAndResult()
{
    // Arrange
    var action = new ReconciliationAction 
    { 
        Status = ActionStatus.InProgress 
    };
    
    // Act
    action.MarkCompleted("Warehouse inventory updated successfully");
    
    // Assert
    Assert.That(action.Status, Is.EqualTo(ActionStatus.Completed));
    Assert.That(action.Result, Is.EqualTo("Warehouse inventory updated successfully"));
    Assert.That(action.CompletedDate, Is.Not.Null);
    Assert.That(action.IsCompleted, Is.True);
}

[Test]
public void MarkFailed_ShouldUpdateStatusAndError()
{
    // Arrange
    var action = new ReconciliationAction 
    { 
        Status = ActionStatus.InProgress 
    };
    
    // Act
    action.MarkFailed("System connection timeout");
    
    // Assert
    Assert.That(action.Status, Is.EqualTo(ActionStatus.Failed));
    Assert.That(action.ErrorMessage, Is.EqualTo("System connection timeout"));
    Assert.That(action.HasError, Is.True);
}

[Test]
public void Retry_ShouldIncrementCountAndResetStatus()
{
    // Arrange
    var action = new ReconciliationAction 
    { 
        Status = ActionStatus.Failed,
        RetryCount = 1,
        ErrorMessage = "Previous error"
    };
    
    // Act
    action.Retry();
    
    // Assert
    Assert.That(action.Status, Is.EqualTo(ActionStatus.Pending));
    Assert.That(action.RetryCount, Is.EqualTo(2));
    Assert.That(action.ErrorMessage, Is.Null);
}
```

### ReconciliationRule Entity Tests

#### Rule Management
```csharp
[Test]
public void UpdateTolerance_ShouldUpdateToleranceValues()
{
    // Arrange
    var rule = new ReconciliationRule 
    { 
        ToleranceAmount = 5.0m,
        TolerancePercentage = 2.0m
    };
    
    // Act
    rule.UpdateTolerance(10.0m, 5.0m);
    
    // Assert
    Assert.That(rule.ToleranceAmount, Is.EqualTo(10.0m));
    Assert.That(rule.TolerancePercentage, Is.EqualTo(5.0m));
}

[Test]
public void Activate_ShouldSetActiveStatus()
{
    // Arrange
    var rule = new ReconciliationRule 
    { 
        IsActive = false 
    };
    
    // Act
    rule.Activate();
    
    // Assert
    Assert.That(rule.IsActive, Is.True);
}

[Test]
public void Deactivate_ShouldSetInactiveStatus()
{
    // Arrange
    var rule = new ReconciliationRule 
    { 
        IsActive = true 
    };
    
    // Act
    rule.Deactivate();
    
    // Assert
    Assert.That(rule.IsActive, Is.False);
}
```

### ReconciliationSummary Value Object Tests

#### Summary Calculations
```csharp
[Test]
public void AutoResolutionRate_ShouldCalculateCorrectly()
{
    // Arrange
    var summary = new ReconciliationSummary
    {
        TotalVariances = 100,
        AutoResolvedCount = 75
    };
    
    // Act & Assert
    Assert.That(summary.AutoResolutionRate, Is.EqualTo(75.0).Within(0.1));
}

[Test]
public void SignificanceRate_ShouldCalculateCorrectly()
{
    // Arrange
    var summary = new ReconciliationSummary
    {
        TotalVariances = 50,
        SignificantVariances = 5
    };
    
    // Act & Assert
    Assert.That(summary.SignificanceRate, Is.EqualTo(10.0).Within(0.1));
}

[Test]
public void OverallHealth_WithIssues_ShouldShowIssuesDetected()
{
    // Arrange
    var summary = new ReconciliationSummary
    {
        Status = ReconciliationStatus.Completed,
        SignificantVariances = 5,
        PendingVariances = 0
    };
    
    // Act & Assert
    Assert.That(summary.HasIssues, Is.True);
    Assert.That(summary.OverallHealth, Is.EqualTo("Issues Detected"));
}

[Test]
public void OverallHealth_WithoutIssues_ShouldShowHealthy()
{
    // Arrange
    var summary = new ReconciliationSummary
    {
        Status = ReconciliationStatus.Completed,
        SignificantVariances = 0,
        PendingVariances = 0
    };
    
    // Act & Assert
    Assert.That(summary.HasIssues, Is.False);
    Assert.That(summary.OverallHealth, Is.EqualTo("Healthy"));
}
```

## Integration Tests

### Application Service Tests

#### Reconciliation Creation and Management
```csharp
[Test]
public async Task CreateReconciliationAsync_WithValidInput_ShouldCreateReconciliation()
{
    // Arrange
    var input = new CreateInventoryReconciliationDto
    {
        ReconciliationName = "Monthly Inventory Reconciliation",
        Type = ReconciliationType.FullInventory,
        PlannedDate = DateTime.Today.AddDays(1),
        WarehouseCode = "WH001",
        IncludeTransportBoxes = true,
        IncludeExternalSystems = true,
        Scope = ReconciliationScope.All,
        UseDefaultRules = true
    };
    
    // Act
    var result = await _reconciliationService.CreateReconciliationAsync(input);
    
    // Assert
    Assert.That(result.ReconciliationName, Is.EqualTo("Monthly Inventory Reconciliation"));
    Assert.That(result.Type, Is.EqualTo(ReconciliationType.FullInventory));
    Assert.That(result.Status, Is.EqualTo(ReconciliationStatus.Planned));
    Assert.That(result.Rules.Count, Is.GreaterThan(0)); // Default rules added
}

[Test]
public async Task StartReconciliationAsync_WithValidReconciliation_ShouldStartSuccessfully()
{
    // Arrange
    var reconciliation = await CreateTestReconciliation();
    
    // Act
    var result = await _reconciliationService.StartReconciliationAsync(reconciliation.Id);
    
    // Assert
    Assert.That(result.Status, Is.EqualTo(ReconciliationStatus.InProgress));
    Assert.That(result.StartDate, Is.Not.Null);
    Assert.That(result.Snapshots.Count, Is.GreaterThan(0));
    
    // Verify background job was queued
    _mockBackgroundJobManager.Verify(x => x.EnqueueAsync<DetectInventoryVariancesJob>(
        It.IsAny<DetectInventoryVariancesArgs>()), Times.Once);
}
```

#### Variance Detection
```csharp
[Test]
public async Task DetectVariancesAsync_WithInventoryDifferences_ShouldDetectVariances()
{
    // Arrange
    var reconciliation = await CreateActiveReconciliation();
    
    var warehouseInventory = new List<SystemInventoryItem>
    {
        new() { ProductCode = "PROD001", Quantity = 100, Location = "A-15-B" },
        new() { ProductCode = "PROD002", Quantity = 50, Location = "B-20-C" }
    };
    
    var transportBoxInventory = new List<SystemInventoryItem>
    {
        new() { ProductCode = "PROD001", Quantity = 95, Location = "TransportBox-TB001" },
        new() { ProductCode = "PROD002", Quantity = 55, Location = "TransportBox-TB002" }
    };
    
    _mockCatalogRepository
        .Setup(x => x.GetWarehouseInventoryAsync(It.IsAny<string>()))
        .ReturnsAsync(warehouseInventory.Select(i => new WarehouseInventoryItem 
        { 
            ProductCode = i.ProductCode, 
            AvailableQuantity = (int)i.Quantity,
            LocationCode = i.Location
        }).ToList());
    
    _mockComparisonService
        .Setup(x => x.CompareInventoriesAsync(It.IsAny<List<SystemInventoryItem>>(), 
                                            It.IsAny<List<SystemInventoryItem>>(), 
                                            It.IsAny<string>(), It.IsAny<string>()))
        .ReturnsAsync(new List<InventoryVarianceData>
        {
            new() { ProductCode = "PROD001", Source1 = "Warehouse", Source2 = "TransportBoxes", 
                    Quantity1 = 100, Quantity2 = 95, Location = "A-15-B" },
            new() { ProductCode = "PROD002", Source1 = "Warehouse", Source2 = "TransportBoxes", 
                    Quantity1 = 50, Quantity2 = 55, Location = "B-20-C" }
        });
    
    // Act
    var result = await _reconciliationService.DetectVariancesAsync(reconciliation.Id);
    
    // Assert
    Assert.That(result.VariancesDetected, Is.EqualTo(2));
    Assert.That(result.ReconciliationId, Is.EqualTo(reconciliation.Id));
    Assert.That(result.Duration, Is.Not.Null);
}

[Test]
public async Task DetectVariancesAsync_WhenNotInProgress_ShouldThrowException()
{
    // Arrange
    var reconciliation = await CreatePlannedReconciliation();
    
    // Act & Assert
    await Assert.ThrowsAsync<BusinessException>(() => 
        _reconciliationService.DetectVariancesAsync(reconciliation.Id));
}
```

#### Variance Resolution
```csharp
[Test]
public async Task ResolveVarianceAsync_WithValidVariance_ShouldResolveSuccessfully()
{
    // Arrange
    var variance = await CreateTestVariance();
    
    var resolveDto = new ResolveVarianceDto
    {
        ActionType = ReconciliationActionType.ManualResolve,
        Resolution = "Physical count confirmed system quantity is correct",
        TargetSystem = "Warehouse",
        ExecuteImmediately = false
    };
    
    // Act
    await _reconciliationService.ResolveVarianceAsync(variance.Id, resolveDto);
    
    // Assert
    var updatedReconciliation = await _reconciliationService.GetReconciliationAsync(variance.ReconciliationId);
    var resolvedVariance = updatedReconciliation.Variances.First(v => v.Id == variance.Id);
    
    Assert.That(resolvedVariance.Status, Is.EqualTo(VarianceStatus.Resolved));
    Assert.That(resolvedVariance.Resolution, Is.EqualTo(resolveDto.Resolution));
    Assert.That(updatedReconciliation.Actions.Count, Is.EqualTo(1));
}

[Test]
public async Task InvestigateVarianceAsync_WithFindings_ShouldUpdateVariance()
{
    // Arrange
    var variance = await CreateTestVariance();
    
    var investigateDto = new InvestigateVarianceDto
    {
        Findings = "Found inventory tracking error in receiving process",
        RootCause = "Receiving process gap",
        Notes = "Need to update receiving procedures"
    };
    
    // Act
    var result = await _reconciliationService.InvestigateVarianceAsync(variance.Id, investigateDto);
    
    // Assert
    Assert.That(result.Status, Is.EqualTo(VarianceStatus.Investigating));
    Assert.That(result.RootCause, Is.EqualTo("Receiving process gap"));
    Assert.That(result.Notes, Does.Contain("receiving procedures"));
}
```

#### Reporting and Analytics
```csharp
[Test]
public async Task GetSummaryAsync_ShouldProvideComprehensiveSummary()
{
    // Arrange
    var reconciliation = await CreateReconciliationWithVariances();
    
    // Act
    var summary = await _reconciliationService.GetSummaryAsync(reconciliation.Id);
    
    // Assert
    Assert.That(summary.ReconciliationId, Is.EqualTo(reconciliation.Id));
    Assert.That(summary.TotalVariances, Is.GreaterThan(0));
    Assert.That(summary.VariancesByCategory.Count, Is.GreaterThan(0));
    Assert.That(summary.VariancesBySource.Count, Is.GreaterThan(0));
}

[Test]
public async Task GetVarianceAnalysisAsync_ShouldProvideDetailedAnalysis()
{
    // Arrange
    var reconciliation = await CreateReconciliationWithMixedVariances();
    
    // Act
    var analysis = await _reconciliationService.GetVarianceAnalysisAsync(reconciliation.Id);
    
    // Assert
    Assert.That(analysis.TotalVariances, Is.GreaterThan(0));
    Assert.That(analysis.VariancesByCategory.Count, Is.GreaterThan(0));
    Assert.That(analysis.VariancesByDirection, Is.Not.Null);
    Assert.That(analysis.TopVariancesByValue.Count, Is.GreaterThan(0));
}
```

#### System Integration
```csharp
[Test]
public async Task GetSystemInventoryAsync_ShouldReturnInventoryFromCorrectSystem()
{
    // Arrange
    var warehouseInventory = new List<WarehouseInventoryItem>
    {
        new() { ProductCode = "PROD001", AvailableQuantity = 100, LocationCode = "A-15-B" },
        new() { ProductCode = "PROD002", AvailableQuantity = 50, LocationCode = "B-20-C" }
    };
    
    _mockCatalogRepository
        .Setup(x => x.GetWarehouseInventoryAsync(It.IsAny<string>()))
        .ReturnsAsync(warehouseInventory);
    
    // Act
    var result = await _reconciliationService.GetSystemInventoryAsync("Warehouse");
    
    // Assert
    Assert.That(result.Count, Is.EqualTo(2));
    Assert.That(result.All(i => !string.IsNullOrEmpty(i.ProductCode)));
    Assert.That(result.All(i => i.Quantity > 0));
}

[Test]
public async Task CompareSystemsAsync_ShouldIdentifyDifferences()
{
    // Arrange
    var compareDto = new CompareSystemsDto
    {
        System1 = "Warehouse",
        System2 = "TransportBoxes",
        ProductFilter = "PROD*"
    };
    
    // Mock comparison service to return differences
    _mockComparisonService
        .Setup(x => x.PerformDetailedComparisonAsync(It.IsAny<List<SystemInventoryItem>>(),
                                                   It.IsAny<List<SystemInventoryItem>>(),
                                                   It.IsAny<ComparisonSettings>()))
        .ReturnsAsync(new InventoryComparisonResult
        {
            TotalItems = 100,
            MatchingItems = 95,
            VarianceCount = 5,
            Variances = new List<InventoryVarianceData>()
        });
    
    // Act
    var result = await _reconciliationService.CompareSystemsAsync(compareDto);
    
    // Assert
    Assert.That(result.System1, Is.EqualTo("Warehouse"));
    Assert.That(result.System2, Is.EqualTo("TransportBoxes"));
    Assert.That(result.VarianceCount, Is.EqualTo(5));
    Assert.That(result.MatchPercentage, Is.EqualTo(95));
}
```

### Performance Tests

#### Large Scale Reconciliation
```csharp
[Test]
public async Task DetectVariancesAsync_WithLargeInventory_ShouldCompleteWithinTimeLimit()
{
    // Arrange
    var reconciliation = await CreateActiveReconciliation();
    var largeInventory = GenerateLargeInventory(10000); // 10,000 items
    
    _mockCatalogRepository
        .Setup(x => x.GetWarehouseInventoryAsync(It.IsAny<string>()))
        .ReturnsAsync(largeInventory);
    
    _mockComparisonService
        .Setup(x => x.CompareInventoriesAsync(It.IsAny<List<SystemInventoryItem>>(),
                                            It.IsAny<List<SystemInventoryItem>>(),
                                            It.IsAny<string>(), It.IsAny<string>()))
        .ReturnsAsync(GenerateVariances(500)); // 500 variances
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var result = await _reconciliationService.DetectVariancesAsync(reconciliation.Id);
    stopwatch.Stop();
    
    // Assert
    Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(60000)); // 60 second limit
    Assert.That(result.VariancesDetected, Is.EqualTo(500));
}

[Test]
public async Task CreateReconciliationWithManyRules_ShouldPerformWell()
{
    // Arrange
    var input = new CreateInventoryReconciliationDto
    {
        ReconciliationName = "Performance Test",
        Type = ReconciliationType.FullInventory,
        PlannedDate = DateTime.Today.AddDays(1),
        UseDefaultRules = false,
        Rules = GenerateManyRules(100) // 100 custom rules
    };
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var result = await _reconciliationService.CreateReconciliationAsync(input);
    stopwatch.Stop();
    
    // Assert
    Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000)); // 5 second limit
    Assert.That(result.Rules.Count, Is.EqualTo(100));
}
```

#### Concurrent Operations
```csharp
[Test]
public async Task ConcurrentVarianceResolution_ShouldHandleCorrectly()
{
    // Arrange
    var reconciliation = await CreateReconciliationWithManyVariances(20);
    var variances = reconciliation.Variances.Take(10).ToList();
    
    // Act
    var tasks = variances.Select(async (variance, index) => 
    {
        var resolveDto = new ResolveVarianceDto 
        { 
            ActionType = ReconciliationActionType.ManualResolve,
            Resolution = $"Resolution {index}",
            ExecuteImmediately = false
        };
        return await _reconciliationService.ResolveVarianceAsync(variance.Id, resolveDto);
    });
    
    await Task.WhenAll(tasks);
    
    // Assert
    var finalReconciliation = await _reconciliationService.GetReconciliationAsync(reconciliation.Id);
    Assert.That(finalReconciliation.ResolvedVariances, Is.EqualTo(10));
    Assert.That(finalReconciliation.Actions.Count, Is.EqualTo(10));
}
```

### End-to-End Tests

#### Complete Reconciliation Workflow
```csharp
[Test]
public async Task CompleteReconciliationWorkflow_ShouldProcessSuccessfully()
{
    // Arrange - Create reconciliation
    var createInput = new CreateInventoryReconciliationDto
    {
        ReconciliationName = "E2E Test Reconciliation",
        Type = ReconciliationType.FullInventory,
        PlannedDate = DateTime.Today.AddHours(8),
        WarehouseCode = "WH001",
        IncludeTransportBoxes = true,
        IncludeExternalSystems = true,
        UseDefaultRules = true
    };
    
    // Act & Assert - Create reconciliation
    var reconciliation = await _reconciliationService.CreateReconciliationAsync(createInput);
    Assert.That(reconciliation.Status, Is.EqualTo(ReconciliationStatus.Planned));
    
    // Act & Assert - Start reconciliation
    reconciliation = await _reconciliationService.StartReconciliationAsync(reconciliation.Id);
    Assert.That(reconciliation.Status, Is.EqualTo(ReconciliationStatus.InProgress));
    
    // Setup mock data for variance detection
    SetupMockInventoryData();
    
    // Act & Assert - Detect variances
    var varianceResult = await _reconciliationService.DetectVariancesAsync(reconciliation.Id);
    Assert.That(varianceResult.VariancesDetected, Is.GreaterThan(0));
    
    // Act & Assert - Get variances and resolve them
    var variances = await _reconciliationService.GetVariancesAsync(reconciliation.Id);
    Assert.That(variances.Count, Is.EqualTo(varianceResult.VariancesDetected));
    
    // Resolve all variances
    foreach (var variance in variances)
    {
        var resolveDto = new ResolveVarianceDto
        {
            ActionType = ReconciliationActionType.ManualResolve,
            Resolution = $"Resolved variance for {variance.ProductCode}",
            ExecuteImmediately = false
        };
        await _reconciliationService.ResolveVarianceAsync(variance.Id, resolveDto);
    }
    
    // Act & Assert - Complete reconciliation
    reconciliation = await _reconciliationService.CompleteReconciliationAsync(reconciliation.Id);
    Assert.That(reconciliation.Status, Is.EqualTo(ReconciliationStatus.Completed));
    Assert.That(reconciliation.ResolutionPercentage, Is.EqualTo(100));
    
    // Act & Assert - Approve reconciliation
    reconciliation = await _reconciliationService.ApproveReconciliationAsync(reconciliation.Id);
    Assert.That(reconciliation.Status, Is.EqualTo(ReconciliationStatus.Approved));
    
    // Verify final summary
    var summary = await _reconciliationService.GetSummaryAsync(reconciliation.Id);
    Assert.That(summary.IsCompleted, Is.True);
    Assert.That(summary.OverallHealth, Is.EqualTo("Healthy"));
}
```

#### Auto-Resolution Workflow
```csharp
[Test]
public async Task AutoResolutionWorkflow_ShouldResolveMinorVariances()
{
    // Arrange
    var reconciliation = await CreateReconciliationWithAutoResolutionRules();
    
    // Setup inventory with minor variances that should auto-resolve
    SetupInventoryWithMinorVariances();
    
    // Act - Start and detect variances
    await _reconciliationService.StartReconciliationAsync(reconciliation.Id);
    var result = await _reconciliationService.DetectVariancesAsync(reconciliation.Id);
    
    // Assert
    var updatedReconciliation = await _reconciliationService.GetReconciliationAsync(reconciliation.Id);
    
    Assert.That(result.AutoResolvedCount, Is.GreaterThan(0));
    Assert.That(updatedReconciliation.AutoResolvedVariances, Is.GreaterThan(0));
    Assert.That(updatedReconciliation.PendingVariances, Is.LessThan(result.VariancesDetected));
    
    // Verify auto-resolution actions were created
    var actions = await _reconciliationService.GetActionsAsync(reconciliation.Id);
    Assert.That(actions.Count(a => a.ActionType == ReconciliationActionType.AutoResolve), Is.GreaterThan(0));
}
```

## Test Data Builders

### Reconciliation Builder
```csharp
public class InventoryReconciliationBuilder
{
    private InventoryReconciliation _reconciliation = new() { Id = Guid.NewGuid() };
    
    public InventoryReconciliationBuilder WithStatus(ReconciliationStatus status)
    {
        _reconciliation.Status = status;
        return this;
    }
    
    public InventoryReconciliationBuilder WithVariance(string productCode, decimal quantity1, decimal quantity2, VarianceCategory category = VarianceCategory.Regular)
    {
        _reconciliation.AddVariance(productCode, "Warehouse", "System", quantity1, quantity2, "TEST-LOC", category);
        return this;
    }
    
    public InventoryReconciliationBuilder WithRule(string pattern, decimal toleranceAmount, decimal tolerancePercentage, ReconciliationActionType action)
    {
        _reconciliation.AddReconciliationRule(pattern, toleranceAmount, tolerancePercentage, action);
        return this;
    }
    
    public InventoryReconciliation Build() => _reconciliation;
}
```

### Performance Test Data
```csharp
public static class ReconciliationTestDataGenerator
{
    public static List<WarehouseInventoryItem> GenerateLargeInventory(int count)
    {
        var inventory = new List<WarehouseInventoryItem>();
        
        for (int i = 0; i < count; i++)
        {
            inventory.Add(new WarehouseInventoryItem
            {
                ProductCode = $"PROD{i:D6}",
                ProductName = $"Product {i}",
                AvailableQuantity = Random.Shared.Next(0, 1000),
                LocationCode = $"A-{(i / 100) + 1:D2}-{(i % 10) + 1}",
                UnitValue = Random.Shared.Next(1, 100)
            });
        }
        
        return inventory;
    }
    
    public static List<InventoryVarianceData> GenerateVariances(int count)
    {
        var variances = new List<InventoryVarianceData>();
        
        for (int i = 0; i < count; i++)
        {
            var systemQty = Random.Shared.Next(50, 200);
            var actualQty = systemQty + Random.Shared.Next(-10, 10);
            
            variances.Add(new InventoryVarianceData
            {
                ProductCode = $"PROD{i:D6}",
                Source1 = "Warehouse",
                Source2 = "TransportBoxes",
                Quantity1 = systemQty,
                Quantity2 = actualQty,
                Location = $"A-{(i / 100) + 1:D2}-{(i % 10) + 1}"
            });
        }
        
        return variances;
    }
    
    public static List<AddReconciliationRuleDto> GenerateManyRules(int count)
    {
        var rules = new List<AddReconciliationRuleDto>();
        
        for (int i = 0; i < count; i++)
        {
            rules.Add(new AddReconciliationRuleDto
            {
                ProductPattern = $"CAT{i % 10}-*",
                ToleranceAmount = Random.Shared.Next(1, 10),
                TolerancePercentage = Random.Shared.Next(1, 5),
                AutoAction = (ReconciliationActionType)(i % 3)
            });
        }
        
        return rules;
    }
}