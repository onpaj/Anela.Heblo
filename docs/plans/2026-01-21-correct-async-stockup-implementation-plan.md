# Correct Async Stock-Up Implementation Plan

## Document Information

- **Version**: 2.0 (Corrected Architecture)
- **Date**: 2026-01-21
- **Status**: Ready for Implementation
- **Supersedes**: `2026-01-21-async-stockup-implementation-plan.md` (v1.0 - incorrect architecture)

## Architecture Correction

### ❌ Previous Incorrect Architecture (v1.0)
- CompleteReceivedBoxesJob as Hangfire recurring job
- ProcessReceivedBoxesHandler creates StockUpOperations
- Separate background job for box completion

### ✅ Correct Architecture (v2.0)
- **ReceiveTransportBoxHandler**: Creates StockUpOperations immediately when box is received
- **CompleteReceivedBoxesService**: Background refresh task (via BackgroundRefreshSchedulerService)
- **ProcessReceivedBoxesHandler**: Deleted (no longer needed)

## System Flow

```
User receives box (API call)
    ↓
ReceiveTransportBoxHandler
    ├─ Box: InTransit/Reserve → Received
    ├─ Create StockUpOperations (Pending) for each item
    └─ Return success

Background (every 2 minutes):
    ↓
CompleteReceivedBoxesService (via BackgroundRefreshSchedulerService)
    ├─ Find boxes in Received state
    ├─ Check StockUpOperations for each box
    ├─ All Completed? → Box → Stocked
    ├─ Any Failed? → Box → Error
    └─ Still Pending? → Skip (leave in Received)
```

---

## Implementation Phases

### Phase 1: Update ReceiveTransportBoxHandler (Create StockUpOperations)
### Phase 2: Create CompleteReceivedBoxes Background Service
### Phase 3: Delete Obsolete Code
### Phase 4: Configuration
### Phase 5: Testing
### Phase 6: Deployment Verification

---

## Phase 1: Update ReceiveTransportBoxHandler

### Task 1.1: Add IStockUpOperationRepository Dependency

**File**: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ReceiveTransportBox/ReceiveTransportBoxHandler.cs`

**Action**: Add new dependency to constructor

**Current constructor** (lines 14-20):
```csharp
public ReceiveTransportBoxHandler(
    ILogger<ReceiveTransportBoxHandler> logger,
    ITransportBoxRepository repository)
{
    _logger = logger;
    _repository = repository;
}
```

**Update to**:
```csharp
private readonly ILogger<ReceiveTransportBoxHandler> _logger;
private readonly ITransportBoxRepository _repository;
private readonly IStockUpOperationRepository _stockUpOperationRepository;

public ReceiveTransportBoxHandler(
    ILogger<ReceiveTransportBoxHandler> logger,
    ITransportBoxRepository repository,
    IStockUpOperationRepository stockUpOperationRepository)
{
    _logger = logger;
    _repository = repository;
    _stockUpOperationRepository = stockUpOperationRepository;
}
```

**Verification**:
- [ ] New field added
- [ ] Constructor parameter added
- [ ] Field assigned in constructor

---

### Task 1.2: Create StockUpOperations After Receiving Box

**File**: Same as above

**Action**: Add logic to create StockUpOperations after `transportBox.Receive()` call

**Location**: After line 63 (`transportBox.Receive(DateTime.UtcNow, request.UserName);`)

**Add before `await _repository.UpdateAsync(transportBox);`**:
```csharp
// Use the domain method to receive the box
transportBox.Receive(DateTime.UtcNow, request.UserName);

// ⚠️ NEW: Create StockUpOperations for each item in the box
_logger.LogInformation("Creating {Count} StockUpOperations for box {BoxId} ({BoxCode})",
    transportBox.Items.Count, transportBox.Id, transportBox.Code);

foreach (var item in transportBox.Items)
{
    var documentNumber = $"BOX-{transportBox.Id:000000}-{item.ProductCode}";

    var operation = new StockUpOperation(
        documentNumber,
        item.ProductCode,
        (int)item.Amount,
        StockUpSourceType.TransportBox,
        transportBox.Id);

    await _stockUpOperationRepository.AddAsync(operation, cancellationToken);

    _logger.LogDebug("Created StockUpOperation {DocumentNumber} for product {ProductCode}, amount {Amount}",
        documentNumber, item.ProductCode, item.Amount);
}

await _stockUpOperationRepository.SaveChangesAsync(cancellationToken);

_logger.LogInformation("Successfully created {Count} StockUpOperations for box {BoxId} ({BoxCode})",
    transportBox.Items.Count, transportBox.Id, transportBox.Code);

// Save the box changes
await _repository.UpdateAsync(transportBox);
await _repository.SaveChangesAsync();
```

**Verification**:
- [ ] StockUpOperations created for each item
- [ ] Document number format: `BOX-{boxId:000000}-{productCode}`
- [ ] Operations saved before box is saved
- [ ] Proper logging at Information and Debug levels
- [ ] Code compiles without errors

---

## Phase 2: Create CompleteReceivedBoxes Background Service

### Task 2.1: Create ITransportBoxCompletionService Interface

**File**: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxCompletionService.cs` (NEW)

**Create file with**:
```csharp
namespace Anela.Heblo.Domain.Features.Logistics.Transport;

/// <summary>
/// Service for completing transport boxes after stock-up operations finish
/// </summary>
public interface ITransportBoxCompletionService
{
    /// <summary>
    /// Check all boxes in Received state and transition them to Stocked/Error
    /// based on their StockUpOperations completion status
    /// </summary>
    Task CompleteReceivedBoxesAsync(CancellationToken cancellationToken = default);
}
```

**Verification**:
- [ ] File created in Domain layer
- [ ] Interface follows domain conventions
- [ ] Method signature correct

---

### Task 2.2: Implement TransportBoxCompletionService

**File**: `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` (NEW)

**Create file with**:
```csharp
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Services;

public class TransportBoxCompletionService : ITransportBoxCompletionService
{
    private readonly ILogger<TransportBoxCompletionService> _logger;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly IStockUpOperationRepository _stockUpOperationRepository;

    public TransportBoxCompletionService(
        ILogger<TransportBoxCompletionService> logger,
        ITransportBoxRepository transportBoxRepository,
        IStockUpOperationRepository stockUpOperationRepository)
    {
        _logger = logger;
        _transportBoxRepository = transportBoxRepository;
        _stockUpOperationRepository = stockUpOperationRepository;
    }

    public async Task CompleteReceivedBoxesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting CompleteReceivedBoxes background task");

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
                _logger.LogError(ex, "Unexpected error processing box {BoxId} ({BoxCode})",
                    box.Id, box.Code);
                errorCount++;
            }
        }

        _logger.LogInformation(
            "CompleteReceivedBoxes finished. Completed: {Completed}, Failed: {Failed}, Skipped: {Skipped}",
            completedCount, errorCount, skippedCount);
    }

    private async Task<BoxProcessingResult> ProcessBoxAsync(
        TransportBox box,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing box {BoxId} ({BoxCode})", box.Id, box.Code);

        // Get all stock-up operations for this box
        var operations = await _stockUpOperationRepository.GetBySourceAsync(
            StockUpSourceType.TransportBox,
            box.Id,
            cancellationToken);

        if (operations.Count == 0)
        {
            _logger.LogWarning(
                "Box {BoxId} ({BoxCode}) has no StockUpOperations, marking as Error",
                box.Id, box.Code);

            box.Error(DateTime.UtcNow, "System",
                "No stock-up operations found for this box");
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
            _logger.LogInformation(
                "All {Count} stock-up operations for box {BoxId} ({BoxCode}) completed, marking as Stocked",
                operations.Count, box.Id, box.Code);

            box.ToPick(DateTime.UtcNow, "System");
            await _transportBoxRepository.UpdateAsync(box, cancellationToken);
            await _transportBoxRepository.SaveChangesAsync(cancellationToken);

            return BoxProcessingResult.Completed;
        }

        if (anyFailed)
        {
            var failedOps = operations
                .Where(op => op.State == StockUpOperationState.Failed)
                .ToList();

            var errorMessage = $"{failedOps.Count} stock-up operation(s) failed. " +
                             $"Document numbers: {string.Join(", ", failedOps.Select(op => op.DocumentNumber))}";

            _logger.LogWarning(
                "Box {BoxId} ({BoxCode}) has {FailedCount} failed stock-up operations, marking as Error",
                box.Id, box.Code, failedOps.Count);

            box.Error(DateTime.UtcNow, "System", errorMessage);
            await _transportBoxRepository.UpdateAsync(box, cancellationToken);
            await _transportBoxRepository.SaveChangesAsync(cancellationToken);

            return BoxProcessingResult.Failed;
        }

        if (pendingOrSubmitted)
        {
            _logger.LogDebug(
                "Box {BoxId} ({BoxCode}) still has {Count} operations in progress, skipping",
                box.Id, box.Code,
                operations.Count(op =>
                    op.State == StockUpOperationState.Pending ||
                    op.State == StockUpOperationState.Submitted));

            return BoxProcessingResult.Skipped;
        }

        // Should not reach here
        _logger.LogWarning("Box {BoxId} ({BoxCode}) in unexpected state, skipping",
            box.Id, box.Code);
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
- [ ] File created in Application/Logistics/Services
- [ ] Implements ITransportBoxCompletionService
- [ ] All logic matches specification
- [ ] Proper error handling
- [ ] Comprehensive logging
- [ ] Code compiles without errors

---

### Task 2.3: Register Service and Background Task

**File**: `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`

**Action**: Register the service and background refresh task

**Find the service registration section** and add:
```csharp
// Register transport box completion service
services.AddTransient<ITransportBoxCompletionService, TransportBoxCompletionService>();
```

**Find the background task registration section** (or create it if missing) and add:
```csharp
// Register background refresh task for completing received boxes
services.RegisterRefreshTask<ITransportBoxCompletionService>(
    nameof(ITransportBoxCompletionService.CompleteReceivedBoxesAsync),
    (service, ct) => service.CompleteReceivedBoxesAsync(ct)
);
```

**Verification**:
- [ ] Service registered as Transient
- [ ] Background refresh task registered
- [ ] TaskId will be: `ITransportBoxCompletionService.CompleteReceivedBoxesAsync`
- [ ] Code compiles without errors

---

## Phase 3: Delete Obsolete Code

### Task 3.1: Delete ProcessReceivedBoxesHandler and Related Files

**Files to delete**:
1. `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ProcessReceivedBoxes/ProcessReceivedBoxesHandler.cs`
2. `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ProcessReceivedBoxes/ProcessReceivedBoxesRequest.cs`
3. `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ProcessReceivedBoxes/ProcessReceivedBoxesResponse.cs`
4. Delete entire folder: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ProcessReceivedBoxes/`

**Verification**:
- [ ] All ProcessReceivedBoxes files deleted
- [ ] No references to ProcessReceivedBoxesHandler remain in codebase
- [ ] Solution compiles without errors

---

### Task 3.2: Delete CompleteReceivedBoxesJob (Incorrect Hangfire Implementation)

**File to delete**:
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/Jobs/CompleteReceivedBoxesJob.cs`

**Verification**:
- [ ] CompleteReceivedBoxesJob file deleted
- [ ] No references remain in codebase

---

### Task 3.3: Delete ProcessReceivedBoxesHandler Tests

**Files to delete**:
1. `backend/test/Anela.Heblo.Tests/Features/Logistics/UseCases/ProcessReceivedBoxesHandlerTests.cs`
2. Any other test files referencing ProcessReceivedBoxes

**Verification**:
- [ ] All test files deleted
- [ ] Test project compiles without errors

---

### Task 3.4: Remove ProcessReceivedBoxes Hangfire Job Configuration

**File**: `backend/src/Anela.Heblo.API/appsettings.json`

**Action**: Find and remove ProcessReceivedBoxes configuration

**Look for**:
```json
{
  "Hangfire": {
    "RecurringJobs": {
      "ProcessReceivedBoxes": {
        // ... configuration ...
      }
    }
  }
}
```

**Remove** the entire `ProcessReceivedBoxes` section.

**Also check**:
- `appsettings.Development.json`
- `appsettings.Test.json`
- Any other appsettings files

**Verification**:
- [ ] ProcessReceivedBoxes removed from all appsettings files
- [ ] JSON remains valid
- [ ] No other references to ProcessReceivedBoxes in config

---

### Task 3.5: Remove ProcessReceivedBoxes Job from Hangfire Dashboard

**File**: Find where Hangfire recurring jobs are registered

**Search for**: `ProcessReceivedBoxes` in job registration code

**Likely location**:
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs`
- Or any file with `RecurringJob.AddOrUpdate`

**Action**: Remove or comment out the job registration

**Verification**:
- [ ] Job registration removed
- [ ] Code compiles without errors
- [ ] Hangfire dashboard will not show ProcessReceivedBoxes job

---

## Phase 4: Configuration

### Task 4.1: Add Background Task Configuration to appsettings.json

**File**: `backend/src/Anela.Heblo.API/appsettings.json`

**Action**: Add configuration for CompleteReceivedBoxes background task

**Find the `BackgroundRefresh` section** (or create if missing) and add:
```json
{
  "BackgroundRefresh": {
    "ITransportBoxCompletionService": {
      "CompleteReceivedBoxesAsync": {
        "Enabled": true,
        "InitialDelay": "00:00:10",
        "RefreshInterval": "00:02:00",
        "HydrationTier": 1
      }
    }
  }
}
```

**Configuration explanation**:
- `Enabled`: true - Task runs automatically
- `InitialDelay`: 10 seconds - Delay before first execution
- `RefreshInterval`: 2 minutes - Time between executions
- `HydrationTier`: 1 - Priority tier for hydration (lower = higher priority)

**Verification**:
- [ ] Configuration added under correct path
- [ ] JSON is valid
- [ ] Intervals are appropriate (2 minutes for production)

---

### Task 4.2: Add Development Configuration

**File**: `backend/src/Anela.Heblo.API/appsettings.Development.json`

**Action**: Add faster refresh interval for development

**Add**:
```json
{
  "BackgroundRefresh": {
    "ITransportBoxCompletionService": {
      "CompleteReceivedBoxesAsync": {
        "RefreshInterval": "00:01:00"
      }
    }
  }
}
```

**Note**: In development, task runs every 1 minute instead of 2 for faster testing.

**Verification**:
- [ ] Development config added
- [ ] Only overrides RefreshInterval
- [ ] JSON is valid

---

## Phase 5: Testing

### Task 5.1: Create Unit Tests for TransportBoxCompletionService

**File**: `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` (NEW)

**Create comprehensive unit tests**:

```csharp
using Anela.Heblo.Application.Features.Logistics.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Services;

public class TransportBoxCompletionServiceTests
{
    private readonly ILogger<TransportBoxCompletionService> _logger;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly IStockUpOperationRepository _stockUpOperationRepository;
    private readonly TransportBoxCompletionService _service;

    public TransportBoxCompletionServiceTests()
    {
        _logger = Substitute.For<ILogger<TransportBoxCompletionService>>();
        _transportBoxRepository = Substitute.For<ITransportBoxRepository>();
        _stockUpOperationRepository = Substitute.For<IStockUpOperationRepository>();
        _service = new TransportBoxCompletionService(
            _logger,
            _transportBoxRepository,
            _stockUpOperationRepository);
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_NoReceivedBoxes_DoesNothing()
    {
        // Arrange
        _transportBoxRepository.GetReceivedBoxesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TransportBox>());

        // Act
        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        // Assert
        await _transportBoxRepository.DidNotReceive()
            .UpdateAsync(Arg.Any<TransportBox>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_AllOperationsCompleted_TransitionsBoxToStocked()
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
        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Stocked);
        await _transportBoxRepository.Received(1)
            .UpdateAsync(box, Arg.Any<CancellationToken>());
        await _transportBoxRepository.Received(1)
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_AnyOperationFailed_TransitionsBoxToError()
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
        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Error);
        box.Description.Should().Contain("failed");
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_OperationsPending_LeavesBoxInReceived()
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
        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Received); // Unchanged
        await _transportBoxRepository.DidNotReceive()
            .UpdateAsync(box, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteReceivedBoxesAsync_NoOperationsForBox_TransitionsToError()
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
        await _service.CompleteReceivedBoxesAsync(CancellationToken.None);

        // Assert
        box.State.Should().Be(TransportBoxState.Error);
        box.Description.Should().Contain("No stock-up operations");
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
- [ ] All test cases implemented
- [ ] Tests use NSubstitute for mocking
- [ ] FluentAssertions for assertions
- [ ] Tests compile and pass

---

### Task 5.2: Update ReceiveTransportBoxHandler Tests

**File**: `backend/test/Anela.Heblo.Tests/Features/Logistics/UseCases/ReceiveTransportBoxHandlerTests.cs`

**Action**: Update existing tests to verify StockUpOperations are created

**Add new test**:
```csharp
[Fact]
public async Task Handle_ReceiveBox_CreatesStockUpOperationsForEachItem()
{
    // Arrange
    var box = CreateBoxWithItems(1, "BOX-001", TransportBoxState.InTransit, 3); // 3 items
    _transportBoxRepository.GetByIdWithDetailsAsync(1)
        .Returns(box);

    var request = new ReceiveTransportBoxRequest
    {
        BoxId = 1,
        UserName = "TestUser"
    };

    // Act
    var response = await _handler.Handle(request, CancellationToken.None);

    // Assert
    response.IsSuccess.Should().BeTrue();
    box.State.Should().Be(TransportBoxState.Received);

    // Verify StockUpOperations were created
    await _stockUpOperationRepository.Received(3)
        .AddAsync(Arg.Any<StockUpOperation>(), Arg.Any<CancellationToken>());
    await _stockUpOperationRepository.Received(1)
        .SaveChangesAsync(Arg.Any<CancellationToken>());
}
```

**Update existing tests** to mock `_stockUpOperationRepository`.

**Verification**:
- [ ] New test added
- [ ] Existing tests updated with mock
- [ ] All tests pass

---

### Task 5.3: Run All Tests

**Command**:
```bash
cd backend
dotnet test
```

**Verification**:
- [ ] All tests pass (0 failures)
- [ ] No compilation errors
- [ ] No test timeouts

---

## Phase 6: Deployment Verification

### Task 6.1: Build Verification

**Commands**:
```bash
cd backend
dotnet build --configuration Release
dotnet format --verify-no-changes
```

**Verification**:
- [ ] Backend builds successfully (0 errors)
- [ ] Code formatting passes
- [ ] No warnings introduced

---

### Task 6.2: Integration Test (Manual)

**Prerequisites**:
- Local development environment running
- Database accessible

**Test steps**:
1. **Create transport box** with items
2. **Receive the box** via API (InTransit → Received)
3. **Verify**:
   - Box state is Received
   - StockUpOperations created (check database)
   - Operations are in Pending state
4. **Wait 2 minutes** for background task
5. **Verify**:
   - Background task logs show execution
   - Operations transition through states (Pending → Submitted → Completed)
   - Box transitions to Stocked
6. **Test failure scenario**:
   - Create box and receive it
   - Manually mark one StockUpOperation as Failed in database
   - Wait for background task
   - Verify box transitions to Error state

**Verification**:
- [ ] Box receives correctly
- [ ] StockUpOperations created
- [ ] Background task runs every 2 minutes
- [ ] Boxes transition to Stocked when all operations complete
- [ ] Boxes transition to Error when any operation fails

---

### Task 6.3: Update Documentation

**Files to update**:
1. `docs/features/receiving.md` - Update workflow diagram and ProcessReceivedBoxesHandler section
2. `docs/features/stock-up-process.md` - Update business triggers section
3. `docs/features/complete-received-boxes-job.md` - Update to reflect background service implementation

**Verification**:
- [ ] Documentation reflects new architecture
- [ ] Workflow diagrams updated
- [ ] No references to obsolete ProcessReceivedBoxesHandler

---

## Success Criteria

### Technical
- [ ] ReceiveTransportBoxHandler creates StockUpOperations
- [ ] TransportBoxCompletionService implemented correctly
- [ ] Background refresh task registered
- [ ] ProcessReceivedBoxesHandler deleted
- [ ] Old Hangfire job removed
- [ ] All tests pass
- [ ] Build succeeds with 0 errors
- [ ] Code formatting validated

### Functional
- [ ] Box receives correctly (InTransit → Received)
- [ ] StockUpOperations created for each item
- [ ] Background task runs every 2 minutes
- [ ] Boxes transition to Stocked when operations complete
- [ ] Boxes transition to Error when operations fail
- [ ] No data loss or corruption

### Operational
- [ ] Background task appears in BackgroundRefresh registry
- [ ] Logs show task execution
- [ ] No errors in application logs
- [ ] Configuration works in dev and production

---

## Rollback Plan

### If Issues Discovered

**Option 1: Disable Background Task**

Edit appsettings.json:
```json
{
  "BackgroundRefresh": {
    "ITransportBoxCompletionService": {
      "CompleteReceivedBoxesAsync": {
        "Enabled": false
      }
    }
  }
}
```

**Option 2: Full Rollback**

```bash
git revert <commit-hash>
git push origin main
# Redeploy previous version
```

**Manual Box Completion** (if needed):
```sql
-- Find boxes stuck in Received with all operations completed
UPDATE "TransportBoxes"
SET "State" = 5, -- Stocked
    "LastStateChanged" = NOW()
WHERE "State" = 3 -- Received
AND NOT EXISTS (
    SELECT 1 FROM "StockUpOperations"
    WHERE "SourceType" = 0
    AND "SourceId" = "TransportBoxes"."Id"
    AND "State" != 2 -- Not Completed
);
```

---

## Implementation Checklist

### Phase 1: Update ReceiveTransportBoxHandler
- [ ] Task 1.1: Add IStockUpOperationRepository dependency
- [ ] Task 1.2: Create StockUpOperations after receiving box

### Phase 2: Create Background Service
- [ ] Task 2.1: Create ITransportBoxCompletionService interface
- [ ] Task 2.2: Implement TransportBoxCompletionService
- [ ] Task 2.3: Register service and background task

### Phase 3: Delete Obsolete Code
- [ ] Task 3.1: Delete ProcessReceivedBoxesHandler files
- [ ] Task 3.2: Delete CompleteReceivedBoxesJob file
- [ ] Task 3.3: Delete ProcessReceivedBoxesHandler tests
- [ ] Task 3.4: Remove ProcessReceivedBoxes from appsettings
- [ ] Task 3.5: Remove ProcessReceivedBoxes from Hangfire registration

### Phase 4: Configuration
- [ ] Task 4.1: Add configuration to appsettings.json
- [ ] Task 4.2: Add development configuration

### Phase 5: Testing
- [ ] Task 5.1: Create unit tests for TransportBoxCompletionService
- [ ] Task 5.2: Update ReceiveTransportBoxHandler tests
- [ ] Task 5.3: Run all tests

### Phase 6: Deployment
- [ ] Task 6.1: Build verification
- [ ] Task 6.2: Integration test (manual)
- [ ] Task 6.3: Update documentation

---

**Implementation Status**: [To be filled during implementation]

**Started By**: [To be filled]

**Start Date**: [To be filled]

**Completion Date**: [To be filled]

**Notes**: [Any implementation notes or deviations from plan]
