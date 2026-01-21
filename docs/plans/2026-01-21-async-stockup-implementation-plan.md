# Asynchronous Stock-Up Workflow - Implementation Plan

## Document Information

- **Version**: 1.0
- **Date**: 2026-01-21
- **Status**: Ready for Implementation
- **Related Documentation**:
  - `/docs/features/stock-up-process.md` (v1.2)
  - `/docs/features/receiving.md`
  - `/docs/features/complete-received-boxes-job.md`

## Overview

This plan details the implementation steps to transition from synchronous stock-up execution to asynchronous workflow where:
1. **Stock Up operations are created** when box is received (Pending state)
2. **Background orchestration** processes operations one by one
3. **Separate background job** transitions boxes to "Stocked" after all operations complete

## Current vs New Workflow

### Current (Synchronous)
```
Box Received → Create & EXECUTE Stock Up → Box → Stocked
                     (blocking)
```

### New (Asynchronous)
```
Box Received → CREATE Stock Up (Pending) → Box stays Received
                                         ↓
            Background: Process operations (orchestrator)
                                         ↓
            Background: Check completion → Box → Stocked
```

## Implementation Phases

### Phase 1: Repository Extensions
### Phase 2: Modify ProcessReceivedBoxesHandler
### Phase 3: Create CompleteReceivedBoxesJob
### Phase 4: Hangfire Registration & Configuration
### Phase 5: Testing
### Phase 6: Deployment & Monitoring

---

## Phase 1: Repository Extensions

### Task 1.1: Add GetReceivedBoxesAsync to ITransportBoxRepository

**File**: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxRepository.cs`

**Action**: Add new method to interface

**Add**:
```csharp
/// <summary>
/// Get all transport boxes in Received state
/// </summary>
Task<List<TransportBox>> GetReceivedBoxesAsync(CancellationToken ct = default);
```

**Verification**:
- [ ] Interface compiles without errors
- [ ] Method signature matches implementation below

---

### Task 1.2: Implement GetReceivedBoxesAsync in TransportBoxRepository

**File**: `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxRepository.cs`

**Action**: Implement the new method

**Add**:
```csharp
public async Task<List<TransportBox>> GetReceivedBoxesAsync(CancellationToken ct = default)
{
    return await _dbContext.TransportBoxes
        .Include(b => b.Items)
        .Include(b => b.StateLog)
        .Where(b => b.State == TransportBoxState.Received)
        .OrderBy(b => b.LastStateChanged ?? b.CreatedAt) // Oldest first
        .ToListAsync(ct);
}
```

**Verification**:
- [ ] Method compiles without errors
- [ ] Includes Items and StateLog for complete data
- [ ] Orders by oldest first (FIFO processing)

---

### Task 1.3: Add GetBySourceAsync to IStockUpOperationRepository

**File**: `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/IStockUpOperationRepository.cs`

**Action**: Add new method to interface

**Add**:
```csharp
/// <summary>
/// Get all stock-up operations for a specific source (transport box or gift package)
/// </summary>
Task<List<StockUpOperation>> GetBySourceAsync(
    StockUpSourceType sourceType,
    int sourceId,
    CancellationToken ct = default);
```

**Verification**:
- [ ] Interface compiles without errors
- [ ] Method signature matches implementation below

---

### Task 1.4: Implement GetBySourceAsync in StockUpOperationRepository

**File**: `backend/src/Anela.Heblo.Persistence/Catalog/StockUpOperationRepository.cs`

**Action**: Implement the new method

**Add**:
```csharp
public async Task<List<StockUpOperation>> GetBySourceAsync(
    StockUpSourceType sourceType,
    int sourceId,
    CancellationToken ct = default)
{
    return await _dbContext.StockUpOperations
        .Where(op => op.SourceType == sourceType && op.SourceId == sourceId)
        .OrderBy(op => op.CreatedAt)
        .ToListAsync(ct);
}
```

**Verification**:
- [ ] Method compiles without errors
- [ ] Orders by creation time for consistent processing

---

## Phase 2: Modify ProcessReceivedBoxesHandler

### Task 2.1: Update ProcessReceivedBoxesHandler - Remove Box State Change

**File**: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ProcessReceivedBoxes/ProcessReceivedBoxesHandler.cs`

**Location**: Lines 68-76 (after `StockUpBoxItemsAsync` call)

**Action**: Remove box state transition logic

**Find**:
```csharp
// Stock up all items from the box
await StockUpBoxItemsAsync(box, cancellationToken);

// Change box state to Stocked (as we're not implementing InSwap)
box.ToPick(DateTime.UtcNow, userName);

// Save changes
await _transportBoxRepository.UpdateAsync(box, cancellationToken);
await _transportBoxRepository.SaveChangesAsync(cancellationToken);
```

**Replace with**:
```csharp
// Stock up all items from the box
// This CREATES StockUpOperation entities in Pending state
// Box state will be changed by CompleteReceivedBoxesJob after all operations complete
await StockUpBoxItemsAsync(box, cancellationToken);

// ⚠️ DO NOT change box state here!
// Box stays in "Received" until CompleteReceivedBoxesJob verifies all operations completed

// Save changes (box remains in Received state)
await _transportBoxRepository.UpdateAsync(box, cancellationToken);
await _transportBoxRepository.SaveChangesAsync(cancellationToken);
```

**Verification**:
- [ ] `box.ToPick()` call removed
- [ ] Box state remains `Received` after processing
- [ ] Comment clearly explains why state is not changed
- [ ] Code compiles without errors

---

### Task 2.2: Verify StockUpBoxItemsAsync Does NOT Execute Synchronously

**File**: Same as above

**Location**: Lines 128-159 (`StockUpBoxItemsAsync` method)

**Action**: Verify that the method creates operations but does not execute them synchronously

**Current code should already call**:
```csharp
var result = await _stockUpOrchestrationService.ExecuteAsync(
    documentNumber,
    item.ProductCode,
    (int)item.Amount,
    StockUpSourceType.TransportBox,
    box.Id,
    cancellationToken);
```

**Important**: `ExecuteAsync` creates the StockUpOperation entity but the orchestration happens in background.

**Verification**:
- [ ] Method calls `ExecuteAsync` for each item
- [ ] No synchronous Shoptet calls in this method
- [ ] Operations are created in Pending state
- [ ] Method does not wait for operations to complete

---

### Task 2.3: Update Response DTO to Not Include Success Count

**File**: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ProcessReceivedBoxes/ProcessReceivedBoxesResponse.cs`

**Action**: Update response to reflect that boxes are not immediately completed

**Find**:
```csharp
public class ProcessReceivedBoxesResponse
{
    public int ProcessedBoxesCount { get; set; }
    public int SuccessfulBoxesCount { get; set; }
    public int FailedBoxesCount { get; set; }
    public List<string> FailedBoxCodes { get; set; } = new();
    public string BatchId { get; set; } = string.Empty;
}
```

**Update to**:
```csharp
public class ProcessReceivedBoxesResponse
{
    public int ProcessedBoxesCount { get; set; }
    public int OperationsCreatedCount { get; set; }  // Changed from SuccessfulBoxesCount
    public int FailedBoxesCount { get; set; }
    public List<string> FailedBoxCodes { get; set; } = new();
    public string BatchId { get; set; } = string.Empty;
}
```

**Update handler to populate new property**:
```csharp
response.OperationsCreatedCount++; // After creating operations
```

**Verification**:
- [ ] Response DTO reflects async nature (operations created, not completed)
- [ ] Handler correctly populates new property
- [ ] API documentation reflects change

---

## Phase 3: Create CompleteReceivedBoxesJob

### Task 3.1: Create CompleteReceivedBoxesJob Class

**File**: `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/Jobs/CompleteReceivedBoxesJob.cs`

**Action**: Create new background job class

**Create file with**:
```csharp
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Infrastructure.Jobs;

/// <summary>
/// Background job that transitions transport boxes from "Received" to "Stocked"
/// after all their stock-up operations complete successfully
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)] // 10 minutes timeout
public class CompleteReceivedBoxesJob : IRecurringJob
{
    private readonly ILogger<CompleteReceivedBoxesJob> _logger;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly IStockUpOperationRepository _stockUpOperationRepository;

    public CompleteReceivedBoxesJob(
        ILogger<CompleteReceivedBoxesJob> logger,
        ITransportBoxRepository transportBoxRepository,
        IStockUpOperationRepository stockUpOperationRepository)
    {
        _logger = logger;
        _transportBoxRepository = transportBoxRepository;
        _stockUpOperationRepository = stockUpOperationRepository;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting CompleteReceivedBoxesJob");

        var receivedBoxes = await _transportBoxRepository.GetReceivedBoxesAsync(cancellationToken);

        _logger.LogInformation("Found {Count} transport boxes in Received state", receivedBoxes.Count);

        if (receivedBoxes.Count == 0)
        {
            _logger.LogDebug("No boxes to process");
            return;
        }

        int completedCount = 0;
        int errorCount = 0;
        int skippedCount = 0;

        foreach (var box in receivedBoxes)
        {
            try
            {
                var result = await ProcessBoxAsync(box, cancellationToken);

                switch (result)
                {
                    case BoxProcessingResult.Completed:
                        completedCount++;
                        break;
                    case BoxProcessingResult.Failed:
                        errorCount++;
                        break;
                    case BoxProcessingResult.Skipped:
                        skippedCount++;
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing box {BoxId} ({BoxCode})", box.Id, box.Code);
                errorCount++;
            }
        }

        _logger.LogInformation(
            "CompleteReceivedBoxesJob finished. Completed: {Completed}, Failed: {Failed}, Skipped: {Skipped}",
            completedCount, errorCount, skippedCount);
    }

    private async Task<BoxProcessingResult> ProcessBoxAsync(TransportBox box, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing box {BoxId} ({BoxCode})", box.Id, box.Code);

        // Get all stock-up operations for this box
        var operations = await _stockUpOperationRepository.GetBySourceAsync(
            StockUpSourceType.TransportBox,
            box.Id,
            cancellationToken);

        if (operations.Count == 0)
        {
            _logger.LogWarning("Box {BoxId} ({BoxCode}) has no StockUpOperations, marking as Error",
                box.Id, box.Code);

            box.Error(DateTime.UtcNow, "System", "No stock-up operations found for this box");
            await _transportBoxRepository.UpdateAsync(box, cancellationToken);
            await _transportBoxRepository.SaveChangesAsync(cancellationToken);

            return BoxProcessingResult.Failed;
        }

        // Check operation states
        var allCompleted = operations.All(op => op.State == StockUpOperationState.Completed);
        var anyFailed = operations.Any(op => op.State == StockUpOperationState.Failed);
        var pendingOrSubmitted = operations.Any(op =>
            op.State == StockUpOperationState.Pending ||
            op.State == StockUpOperationState.Submitted);

        if (allCompleted)
        {
            _logger.LogInformation("All {Count} stock-up operations for box {BoxId} ({BoxCode}) completed, marking as Stocked",
                operations.Count, box.Id, box.Code);

            box.ToPick(DateTime.UtcNow, "System");
            await _transportBoxRepository.UpdateAsync(box, cancellationToken);
            await _transportBoxRepository.SaveChangesAsync(cancellationToken);

            return BoxProcessingResult.Completed;
        }

        if (anyFailed)
        {
            var failedOps = operations.Where(op => op.State == StockUpOperationState.Failed).ToList();
            var errorMessage = $"{failedOps.Count} stock-up operation(s) failed. " +
                             $"Document numbers: {string.Join(", ", failedOps.Select(op => op.DocumentNumber))}";

            _logger.LogWarning("Box {BoxId} ({BoxCode}) has {FailedCount} failed stock-up operations, marking as Error",
                box.Id, box.Code, failedOps.Count);

            box.Error(DateTime.UtcNow, "System", errorMessage);
            await _transportBoxRepository.UpdateAsync(box, cancellationToken);
            await _transportBoxRepository.SaveChangesAsync(cancellationToken);

            return BoxProcessingResult.Failed;
        }

        if (pendingOrSubmitted)
        {
            _logger.LogDebug("Box {BoxId} ({BoxCode}) still has {Count} operations in progress, skipping",
                box.Id, box.Code, operations.Count(op =>
                    op.State == StockUpOperationState.Pending ||
                    op.State == StockUpOperationState.Submitted));

            return BoxProcessingResult.Skipped;
        }

        // Should not reach here
        _logger.LogWarning("Box {BoxId} ({BoxCode}) in unexpected state, skipping", box.Id, box.Code);
        return BoxProcessingResult.Skipped;
    }

    private enum BoxProcessingResult
    {
        Completed,
        Failed,
        Skipped
    }
}
```

**Verification**:
- [ ] File created in correct location
- [ ] Class implements `IRecurringJob` interface
- [ ] DisableConcurrentExecution attribute prevents parallel runs
- [ ] All dependencies injected via constructor
- [ ] Logging at appropriate levels
- [ ] Code compiles without errors

---

## Phase 4: Hangfire Registration & Configuration

### Task 4.1: Register Job in RecurringJobDiscoveryService

**File**: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs`

**Action**: Add job registration to Hangfire

**Find the method** where other recurring jobs are registered (likely `RegisterRecurringJobs()` or similar)

**Add**:
```csharp
// Complete Received Boxes Job - transitions boxes to Stocked after stock-up completion
RecurringJob.AddOrUpdate<CompleteReceivedBoxesJob>(
    "CompleteReceivedBoxes",
    job => job.ExecuteAsync(CancellationToken.None),
    "*/2 * * * *", // Every 2 minutes
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.Utc
    });
```

**Verification**:
- [ ] Job registered with unique ID "CompleteReceivedBoxes"
- [ ] CRON expression `*/2 * * * *` (every 2 minutes)
- [ ] UTC timezone specified
- [ ] Code compiles without errors

---

### Task 4.2: Add Configuration to appsettings.json

**File**: `backend/src/Anela.Heblo.API/appsettings.json`

**Action**: Add configuration section for the new job

**Find the Hangfire or Jobs section** and add:
```json
{
  "Hangfire": {
    "RecurringJobs": {
      "ProcessReceivedBoxes": {
        "Enabled": true,
        "CronExpression": "*/5 * * * *"
      },
      "CompleteReceivedBoxes": {
        "Enabled": true,
        "CronExpression": "*/2 * * * *",
        "TimeoutSeconds": 600
      }
    }
  }
}
```

**Verification**:
- [ ] Configuration added with correct structure
- [ ] Job enabled by default
- [ ] CRON expression matches registration
- [ ] Timeout specified (10 minutes)

---

### Task 4.3: Add Configuration to appsettings.Development.json

**File**: `backend/src/Anela.Heblo.API/appsettings.Development.json`

**Action**: Add development-specific configuration

**Add**:
```json
{
  "Hangfire": {
    "RecurringJobs": {
      "CompleteReceivedBoxes": {
        "Enabled": true,
        "CronExpression": "*/1 * * * *"  // Every 1 minute in development for faster testing
      }
    }
  }
}
```

**Verification**:
- [ ] Development runs more frequently (1 minute) for testing
- [ ] Production uses 2 minutes for balance

---

## Phase 5: Testing

### Task 5.1: Create Unit Tests for CompleteReceivedBoxesJob

**File**: `backend/test/Anela.Heblo.Tests/Features/Logistics/Jobs/CompleteReceivedBoxesJobTests.cs`

**Action**: Create comprehensive unit tests

**Create file with tests**:

```csharp
using Anela.Heblo.Application.Features.Logistics.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Jobs;

public class CompleteReceivedBoxesJobTests
{
    private readonly ILogger<CompleteReceivedBoxesJob> _logger;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly IStockUpOperationRepository _stockUpOperationRepository;
    private readonly CompleteReceivedBoxesJob _job;

    public CompleteReceivedBoxesJobTests()
    {
        _logger = Substitute.For<ILogger<CompleteReceivedBoxesJob>>();
        _transportBoxRepository = Substitute.For<ITransportBoxRepository>();
        _stockUpOperationRepository = Substitute.For<IStockUpOperationRepository>();
        _job = new CompleteReceivedBoxesJob(_logger, _transportBoxRepository, _stockUpOperationRepository);
    }

    [Fact]
    public async Task ExecuteAsync_NoReceivedBoxes_DoesNothing()
    {
        // Arrange
        _transportBoxRepository.GetReceivedBoxesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TransportBox>());

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        await _transportBoxRepository.DidNotReceive()
            .UpdateAsync(Arg.Any<TransportBox>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AllOperationsCompleted_TransitionsBoxToStocked()
    {
        // Arrange
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepository.GetReceivedBoxesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TransportBox> { box });

        var operations = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed),
            CreateOperation(2, "BOX-000001-PROD2", StockUpOperationState.Completed)
        };
        _stockUpOperationRepository.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                Arg.Any<CancellationToken>())
            .Returns(operations);

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Stocked);
        await _transportBoxRepository.Received(1)
            .UpdateAsync(box, Arg.Any<CancellationToken>());
        await _transportBoxRepository.Received(1)
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AnyOperationFailed_TransitionsBoxToError()
    {
        // Arrange
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepository.GetReceivedBoxesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TransportBox> { box });

        var operations = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed),
            CreateOperation(2, "BOX-000001-PROD2", StockUpOperationState.Failed)
        };
        _stockUpOperationRepository.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                Arg.Any<CancellationToken>())
            .Returns(operations);

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Error);
        box.Description.Should().Contain("failed");
        await _transportBoxRepository.Received(1)
            .UpdateAsync(box, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_OperationsPending_LeavesBoxInReceived()
    {
        // Arrange
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepository.GetReceivedBoxesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TransportBox> { box });

        var operations = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed),
            CreateOperation(2, "BOX-000001-PROD2", StockUpOperationState.Pending)
        };
        _stockUpOperationRepository.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                Arg.Any<CancellationToken>())
            .Returns(operations);

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Received); // Unchanged
        await _transportBoxRepository.DidNotReceive()
            .UpdateAsync(box, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NoOperationsForBox_TransitionsToError()
    {
        // Arrange
        var box = CreateBox(1, "BOX-001", TransportBoxState.Received);
        _transportBoxRepository.GetReceivedBoxesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TransportBox> { box });

        _stockUpOperationRepository.GetBySourceAsync(
                StockUpSourceType.TransportBox,
                box.Id,
                Arg.Any<CancellationToken>())
            .Returns(new List<StockUpOperation>());

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Error);
        box.Description.Should().Contain("No stock-up operations");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleBoxes_ProcessesAllCorrectly()
    {
        // Arrange
        var box1 = CreateBox(1, "BOX-001", TransportBoxState.Received);
        var box2 = CreateBox(2, "BOX-002", TransportBoxState.Received);
        var box3 = CreateBox(3, "BOX-003", TransportBoxState.Received);

        _transportBoxRepository.GetReceivedBoxesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TransportBox> { box1, box2, box3 });

        // Box 1: All completed
        var ops1 = new List<StockUpOperation>
        {
            CreateOperation(1, "BOX-000001-PROD1", StockUpOperationState.Completed)
        };
        _stockUpOperationRepository.GetBySourceAsync(StockUpSourceType.TransportBox, 1, Arg.Any<CancellationToken>())
            .Returns(ops1);

        // Box 2: Failed
        var ops2 = new List<StockUpOperation>
        {
            CreateOperation(2, "BOX-000002-PROD1", StockUpOperationState.Failed)
        };
        _stockUpOperationRepository.GetBySourceAsync(StockUpSourceType.TransportBox, 2, Arg.Any<CancellationToken>())
            .Returns(ops2);

        // Box 3: Still pending
        var ops3 = new List<StockUpOperation>
        {
            CreateOperation(3, "BOX-000003-PROD1", StockUpOperationState.Pending)
        };
        _stockUpOperationRepository.GetBySourceAsync(StockUpSourceType.TransportBox, 3, Arg.Any<CancellationToken>())
            .Returns(ops3);

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        box1.State.Should().Be(TransportBoxState.Stocked);
        box2.State.Should().Be(TransportBoxState.Error);
        box3.State.Should().Be(TransportBoxState.Received);
    }

    // Helper methods
    private TransportBox CreateBox(int id, string code, TransportBoxState state)
    {
        var box = new TransportBox(code, state, DateTime.UtcNow, "Test");
        typeof(TransportBox).GetProperty("Id")!.SetValue(box, id);
        return box;
    }

    private StockUpOperation CreateOperation(int id, string documentNumber, StockUpOperationState state)
    {
        var operation = new StockUpOperation(
            documentNumber,
            "PROD001",
            100,
            StockUpSourceType.TransportBox,
            1);

        typeof(StockUpOperation).GetProperty("Id")!.SetValue(operation, id);
        typeof(StockUpOperation).GetProperty("State")!.SetValue(operation, state);

        return operation;
    }
}
```

**Verification**:
- [ ] All test cases pass
- [ ] Edge cases covered (no boxes, no operations, mixed states)
- [ ] NSubstitute mocks used correctly
- [ ] FluentAssertions used for readable assertions

---

### Task 5.2: Create Integration Tests

**File**: `backend/test/Anela.Heblo.Tests/Features/Logistics/Jobs/CompleteReceivedBoxesJobIntegrationTests.cs`

**Action**: Create integration tests with real database

**Test scenarios**:
- ✅ Create box with items → Mark received → Create operations → Mark completed → Job runs → Box is Stocked
- ✅ Create box → Operations failed → Job runs → Box is Error
- ✅ Create box → Operations pending → Job runs → Box stays Received

**Example test**:
```csharp
[Fact]
public async Task IntegrationTest_CompleteWorkflow_BoxTransitionsToStocked()
{
    // Arrange
    using var scope = CreateScope();
    var boxRepository = scope.ServiceProvider.GetRequiredService<ITransportBoxRepository>();
    var opRepository = scope.ServiceProvider.GetRequiredService<IStockUpOperationRepository>();
    var job = scope.ServiceProvider.GetRequiredService<CompleteReceivedBoxesJob>();

    // Create box
    var box = new TransportBox("TEST-BOX", TransportBoxState.New, DateTime.UtcNow, "Test");
    box.AddItem("PROD001", "Product 1", 100, DateTime.UtcNow, "Test");
    box.Receive(DateTime.UtcNow, "Test", TransportBoxState.Stocked);
    await boxRepository.AddAsync(box);
    await boxRepository.SaveChangesAsync();

    // Create stock-up operation
    var operation = new StockUpOperation(
        $"BOX-{box.Id:000000}-PROD001",
        "PROD001",
        100,
        StockUpSourceType.TransportBox,
        box.Id);
    operation.MarkAsSubmitted(DateTime.UtcNow);
    operation.MarkAsCompleted(DateTime.UtcNow);
    await opRepository.AddAsync(operation);
    await opRepository.SaveChangesAsync();

    // Act
    await job.ExecuteAsync();

    // Assert
    var updatedBox = await boxRepository.GetByIdAsync(box.Id);
    updatedBox.State.Should().Be(TransportBoxState.Stocked);
}
```

**Verification**:
- [ ] Integration tests use real database (in-memory or test DB)
- [ ] Tests verify end-to-end workflow
- [ ] Tests clean up data after execution

---

### Task 5.3: Update Existing Tests

**Files**: Search for tests referencing `ProcessReceivedBoxesHandler`

**Action**: Update assertions to reflect async behavior

**Changes needed**:
- Response DTO assertions (`SuccessfulBoxesCount` → `OperationsCreatedCount`)
- Box state assertions (should remain `Received` after handler)
- Remove assertions about immediate completion

**Verification**:
- [ ] All existing tests updated
- [ ] Tests pass with new async behavior
- [ ] No breaking test failures

---

## Phase 6: Deployment & Monitoring

### Task 6.1: Create Migration Script (if needed)

**Action**: Check if any database schema changes are needed

**Verification**:
- [ ] No schema changes required (existing tables support new workflow)
- [ ] Indexes on `TransportBox.State` and `StockUpOperation.SourceType/SourceId` exist

---

### Task 6.2: Update API Documentation

**Files**:
- API controller comments
- OpenAPI/Swagger documentation

**Action**: Update endpoint descriptions to reflect async behavior

**Example**:
```csharp
/// <summary>
/// Process received transport boxes (creates stock-up operations in Pending state)
/// </summary>
/// <remarks>
/// Boxes remain in "Received" state until CompleteReceivedBoxesJob verifies
/// all stock-up operations completed successfully.
/// </remarks>
```

**Verification**:
- [ ] API documentation updated
- [ ] Swagger UI reflects changes
- [ ] Example responses updated

---

### Task 6.3: Add Monitoring Dashboard

**Action**: Add metrics to existing monitoring dashboard

**Metrics to add**:
- Count of boxes in "Received" state
- Average time box stays in "Received"
- Count of completed vs failed boxes (last 24h)
- Alert if box stays in "Received" > 10 minutes

**Verification**:
- [ ] Dashboard shows new metrics
- [ ] Alerts configured for stuck boxes

---

### Task 6.4: Update Logging Configuration

**File**: `backend/src/Anela.Heblo.API/appsettings.json`

**Action**: Ensure appropriate log levels for new job

**Add/verify**:
```json
{
  "Logging": {
    "LogLevel": {
      "Anela.Heblo.Application.Features.Logistics.Infrastructure.Jobs.CompleteReceivedBoxesJob": "Information"
    }
  }
}
```

**Verification**:
- [ ] Job logs at Information level in production
- [ ] Debug logs available in Development

---

## Testing Strategy

### Manual Testing Checklist

**Environment**: Local development

1. **Basic Workflow**:
   - [ ] Start backend and frontend
   - [ ] Create transport box with items
   - [ ] Mark box as "Received"
   - [ ] Verify StockUpOperations created in Pending state
   - [ ] Verify box remains in "Received" state
   - [ ] Wait for orchestration to process operations
   - [ ] Wait for CompleteReceivedBoxesJob to run (2 minutes max)
   - [ ] Verify box transitions to "Stocked"

2. **Failure Scenario**:
   - [ ] Create box and mark as "Received"
   - [ ] Manually mark one StockUpOperation as "Failed" in database
   - [ ] Wait for CompleteReceivedBoxesJob
   - [ ] Verify box transitions to "Error" state
   - [ ] Verify error message contains failed operation details

3. **Stuck Operations**:
   - [ ] Create box with operations in "Pending" state
   - [ ] Wait for CompleteReceivedBoxesJob
   - [ ] Verify box stays in "Received" (not changed)
   - [ ] Manually complete operations
   - [ ] Wait for next job run
   - [ ] Verify box transitions to "Stocked"

4. **Multiple Boxes**:
   - [ ] Create 3 boxes: one ready (all completed), one failed, one pending
   - [ ] Run CompleteReceivedBoxesJob
   - [ ] Verify each box transitions correctly

**Verification**:
- [ ] All manual test scenarios pass
- [ ] Logs show appropriate messages
- [ ] No errors or warnings in logs

---

## Rollback Plan

### If Issues Discovered After Deployment

**Option 1: Quick Rollback (Disable New Job)**

1. Set job configuration to disabled:
```json
{
  "Hangfire": {
    "RecurringJobs": {
      "CompleteReceivedBoxes": {
        "Enabled": false
      }
    }
  }
}
```

2. Manually transition stuck boxes:
```sql
UPDATE "TransportBoxes"
SET "State" = 5 -- Stocked
WHERE "State" = 3 -- Received
AND NOT EXISTS (
    SELECT 1 FROM "StockUpOperations"
    WHERE "SourceType" = 0
    AND "SourceId" = "TransportBoxes"."Id"
    AND "State" != 2 -- Not Completed
);
```

**Option 2: Full Rollback (Revert Code)**

```bash
git revert <commit-hash>
git push origin main
# Redeploy previous version
```

**Verification**:
- [ ] Rollback procedure tested in staging
- [ ] SQL script validated
- [ ] Backup of database before deployment

---

## Success Criteria

### Technical
- [ ] All unit tests pass (CompleteReceivedBoxesJob)
- [ ] All integration tests pass
- [ ] Existing tests pass with updates
- [ ] No compilation errors
- [ ] API documentation updated
- [ ] Code reviewed and approved

### Functional
- [ ] Box creation workflow unchanged
- [ ] Stock-up operations created correctly
- [ ] CompleteReceivedBoxesJob runs successfully
- [ ] Boxes transition to correct states
- [ ] Failed operations handled correctly
- [ ] No data loss or corruption

### Operational
- [ ] Job appears in Hangfire dashboard
- [ ] Job executes every 2 minutes
- [ ] Logs show appropriate messages
- [ ] No errors in application logs
- [ ] Monitoring dashboard shows metrics
- [ ] Alerts configured for stuck boxes

---

## Implementation Checklist

### Phase 1: Repository Extensions
- [ ] Task 1.1: Add GetReceivedBoxesAsync to interface
- [ ] Task 1.2: Implement GetReceivedBoxesAsync
- [ ] Task 1.3: Add GetBySourceAsync to interface
- [ ] Task 1.4: Implement GetBySourceAsync

### Phase 2: Modify Handler
- [ ] Task 2.1: Remove box state change from handler
- [ ] Task 2.2: Verify StockUpBoxItemsAsync
- [ ] Task 2.3: Update response DTO

### Phase 3: Create Job
- [ ] Task 3.1: Create CompleteReceivedBoxesJob class

### Phase 4: Configuration
- [ ] Task 4.1: Register job in Hangfire
- [ ] Task 4.2: Add configuration to appsettings.json
- [ ] Task 4.3: Add configuration to appsettings.Development.json

### Phase 5: Testing
- [ ] Task 5.1: Create unit tests
- [ ] Task 5.2: Create integration tests
- [ ] Task 5.3: Update existing tests

### Phase 6: Deployment
- [ ] Task 6.1: Verify no migration needed
- [ ] Task 6.2: Update API documentation
- [ ] Task 6.3: Add monitoring dashboard
- [ ] Task 6.4: Update logging configuration

### Testing
- [ ] Manual testing completed
- [ ] All test scenarios pass
- [ ] No regressions found

### Deployment
- [ ] Code reviewed
- [ ] PR approved and merged
- [ ] Deployed to staging
- [ ] Smoke tests on staging pass
- [ ] Deployed to production
- [ ] Post-deployment monitoring (24 hours)

---

**Implementation Status**: [To be filled during implementation]

**Started By**: [To be filled]

**Start Date**: [To be filled]

**Completion Date**: [To be filled]

**Notes**: [Any implementation notes or deviations from plan]
