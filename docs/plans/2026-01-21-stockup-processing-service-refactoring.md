# StockUpProcessingService Refactoring Plan

**Date:** 2026-01-21
**Status:** Approved - Ready for Implementation

## Problem Statement

The current `StockUpOrchestrationService` does not work as a proper state machine. It handles both creation and immediate execution of stock-up operations synchronously. According to the specification:

1. New `StockUpOperations` should be created in `Pending` state during box receipt or gift package manufacture
2. A background task (state machine) should periodically process `Pending` operations and perform the actual stock-up to Shoptet
3. The current `ExecuteAsync()` method couples creation with execution, making it synchronous and not following the intended async workflow

## Solution Overview

Refactor the stock-up flow to work as a proper state machine by separating operation creation (synchronous) from processing (asynchronous background task).

## Architecture

```
CREATION (Synchronous - during user action)
├── TransportBox receipt: HandleReceived() → CreateOperationAsync()
└── GiftPackageManufacture: CreateManufactureAsync() → CreateOperationAsync()
                              ↓
PROCESSING (Asynchronous - background task)
└── StockUpProcessingService.ProcessPendingOperationsAsync()
    - Finds all Pending operations
    - Processes each sequentially (submit to Shoptet)
    - Marks each as Completed or Failed atomically
                              ↓
COMPLETION (Existing background task)
└── TransportBoxCompletionService.CompleteReceivedBoxesAsync()
    - Transitions boxes: Received → Stocked (or Error)
```

### State Flow

```
StockUpOperation States:
Pending → Submitted → Completed
   ↓         ↓
   Failed ← Failed
```

## Implementation Steps

### Step 1: Create StockUpProcessingService

**New files:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/IStockUpProcessingService.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs`

**Interface:**
```csharp
public interface IStockUpProcessingService
{
    // Creation - called by handlers/services
    Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default);

    // Processing - called by background task
    Task ProcessPendingOperationsAsync(CancellationToken ct = default);
}
```

**CreateOperationAsync implementation:**
1. Create new `StockUpOperation` entity in `Pending` state
2. Add to repository and save changes
3. Return immediately (no Shoptet interaction)

**ProcessPendingOperationsAsync implementation:**
1. Get all operations in `Pending` state via `_stockUpOperationRepository.GetByStateAsync(StockUpOperationState.Pending)`
2. For each operation (sequentially, one at a time):
   ```
   try {
       - Mark operation as Submitted
       - Pre-check: Call eshopService.VerifyStockUpExistsAsync(documentNumber)
       - If already exists in Shoptet history:
           → Mark operation as Completed (idempotency)
       - If not exists:
           → Submit to Shoptet via eshopService.StockUpAsync(request)
           → Post-verify: Confirm submission exists in Shoptet
           → Mark operation as Completed
   } catch (Exception ex) {
       - Log error
       - Mark operation as Failed with error message
       - Continue to next operation (no short-circuit)
   }
   ```

**Dependencies:**
- `IStockUpOperationRepository` - for database operations
- `IEshopService` - for Shoptet API calls
- `ILogger<StockUpProcessingService>` - for logging

**4-Layer Protection (preserved from original):**
1. Database unique constraint on DocumentNumber
2. Pre-submit check (verify not already in Shoptet)
3. Transactional submit
4. Post-verify (confirm exists in Shoptet)

### Step 2: Update ChangeTransportBoxStateHandler

**File:** `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`

**Current implementation (lines 209-234):**
```csharp
private async Task<ChangeTransportBoxStateResponse?> HandleReceived(...)
{
    foreach (var item in box.Items)
    {
        var documentNumber = $"BOX-{box.Id:000000}-{item.ProductCode}";

        var operation = new StockUpOperation(
            documentNumber,
            item.ProductCode,
            (int)item.Amount,
            StockUpSourceType.TransportBox,
            box.Id);

        await _stockUpOperationRepository.AddAsync(operation, cancellationToken);
    }
    await _stockUpOperationRepository.SaveChangesAsync(cancellationToken);
    return null;
}
```

**Changes:**
- Replace `IStockUpOperationRepository` constructor parameter with `IStockUpProcessingService`
- Update `HandleReceived()` method:
  ```csharp
  private async Task<ChangeTransportBoxStateResponse?> HandleReceived(...)
  {
      foreach (var item in box.Items)
      {
          var documentNumber = $"BOX-{box.Id:000000}-{item.ProductCode}";

          await _stockUpProcessingService.CreateOperationAsync(
              documentNumber,
              item.ProductCode,
              (int)item.Amount,
              StockUpSourceType.TransportBox,
              box.Id,
              cancellationToken);
      }
      return null;
  }
  ```

**Document Number Format:** `BOX-{boxId:000000}-{productCode}` (unchanged)

### Step 3: Update GiftPackageManufactureService

**File:** `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`

**Current implementation (lines 207-261):**
- Calls `_stockUpOrchestrationService.ExecuteAsync()` for each ingredient (negative amounts)
- Calls `_stockUpOrchestrationService.ExecuteAsync()` for output product (positive amount)
- Throws exception if any operation fails
- Synchronous processing - blocks until all Shoptet operations complete

**Changes:**
- Replace `IStockUpOrchestrationService` constructor parameter with `IStockUpProcessingService`
- Update `CreateManufactureAsync()` method:
  ```csharp
  // For each ingredient (consumption - negative amount)
  foreach (var ingredient in giftPackage.Ingredients ?? new List<GiftPackageIngredientDto>())
  {
      var consumedQuantity = (int)(ingredient.RequiredQuantity * quantity);
      manufactureLog.AddConsumedItem(ingredient.ProductCode, consumedQuantity);

      var documentNumber = $"GPM-{manufactureLog.Id:000000}-{ingredient.ProductCode}";

      await _stockUpProcessingService.CreateOperationAsync(
          documentNumber,
          ingredient.ProductCode,
          -consumedQuantity,  // Negative = consumption
          StockUpSourceType.GiftPackageManufacture,
          manufactureLog.Id,
          cancellationToken);
  }

  // For output product (production - positive amount)
  var outputDocNumber = $"GPM-{manufactureLog.Id:000000}-{giftPackageCode}";

  await _stockUpProcessingService.CreateOperationAsync(
      outputDocNumber,
      giftPackageCode,
      quantity,  // Positive = production
      StockUpSourceType.GiftPackageManufacture,
      manufactureLog.Id,
      cancellationToken);
  ```
- Remove exception throwing on operation results (operations processed async)
- Remove error handling for individual operations (handled by background task)
- **Remove `EnqueueManufactureAsync()` method entirely** (lines 269-281) - no longer needed since operations are async

**Document Number Format:**
- Ingredients: `GPM-{logId:000000}-{productCode}` (negative amount)
- Output: `GPM-{logId:000000}-{giftPackageCode}` (positive amount)

**File:** `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageManufactureService.cs`

**Changes:**
- Remove `EnqueueManufactureAsync()` method from interface

**Impact:**
- `CreateManufactureAsync()` becomes much faster (no Shoptet calls)
- Manufacture log is created immediately, but actual stock changes happen asynchronously
- No need for background job enqueue since processing is already background task

### Step 4: Update DI Registration

**File:** `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`

**Remove:**
```csharp
services.AddTransient<IStockUpOrchestrationService, StockUpOrchestrationService>();
```

**Add:**
```csharp
services.AddTransient<IStockUpProcessingService, StockUpProcessingService>();

services.RegisterRefreshTask<IStockUpProcessingService>(
    nameof(IStockUpProcessingService.ProcessPendingOperationsAsync),
    (service, ct) => service.ProcessPendingOperationsAsync(ct),
    refreshInterval: TimeSpan.FromMinutes(2)  // Same as TransportBoxCompletionService
);
```

**Background Task Registration:**
- Uses existing `BackgroundRefreshSchedulerService` infrastructure
- Runs every 2 minutes (consistent with `TransportBoxCompletionService`)
- Waits for hydration completion before starting

### Step 5: Delete Deprecated Files

**Files to delete:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/IStockUpOrchestrationService.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpOrchestrationService.cs`

**Reason:** The new `StockUpProcessingService` completely replaces the orchestration service with a proper state machine pattern.

### Step 6: Update Tests

#### New Test File

**File:** `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/StockUpProcessingServiceTests.cs`

**Test cases:**
1. `CreateOperationAsync_CreatesOperationInPendingState`
   - Verify operation is created with correct properties
   - Verify state is `Pending`
   - Verify operation is saved to repository

2. `ProcessPendingOperationsAsync_ProcessesAllPendingOperations`
   - Setup multiple pending operations
   - Mock eshop service responses
   - Verify all operations are processed
   - Verify all marked as `Completed`

3. `ProcessPendingOperationsAsync_MarksOperationAsCompleted_WhenAlreadyExistsInShoptet`
   - Setup operation in `Pending` state
   - Mock `VerifyStockUpExistsAsync()` to return true
   - Verify operation marked as `Completed` without submitting
   - Verify `StockUpAsync()` not called (idempotency)

4. `ProcessPendingOperationsAsync_SubmitsToShoptet_WhenNotExists`
   - Setup operation in `Pending` state
   - Mock `VerifyStockUpExistsAsync()` to return false
   - Mock successful `StockUpAsync()` call
   - Verify operation marked as `Completed`
   - Verify `StockUpAsync()` called once

5. `ProcessPendingOperationsAsync_MarksAsFailedAndContinues_WhenOneOperationFails`
   - Setup 3 pending operations
   - Mock second operation to throw exception
   - Verify first operation marked `Completed`
   - Verify second operation marked `Failed` with error message
   - Verify third operation still processed and marked `Completed`
   - Verify atomic failure handling (no short-circuit)

#### Update Existing Tests

**File:** `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ReceiveTransportBoxHandlerTests.cs`

**Changes:**
- Replace `IStockUpOperationRepository` mock with `IStockUpProcessingService` mock
- Update test assertions to verify `CreateOperationAsync()` called with correct parameters
- Remove direct repository interaction assertions

#### Delete Test Files

**Files to delete (if they exist):**
- Any `StockUpOrchestrationServiceTests.cs` files

## Critical Files Summary

| File | Action | Location |
|------|--------|----------|
| `IStockUpProcessingService.cs` | CREATE | `Application/Features/Catalog/Services/` |
| `StockUpProcessingService.cs` | CREATE | `Application/Features/Catalog/Services/` |
| `StockUpOrchestrationService.cs` | DELETE | `Application/Features/Catalog/Services/` |
| `IStockUpOrchestrationService.cs` | DELETE | `Application/Features/Catalog/Services/` |
| `ChangeTransportBoxStateHandler.cs` | MODIFY | `Application/Features/Logistics/UseCases/ChangeTransportBoxState/` |
| `GiftPackageManufactureService.cs` | MODIFY | `Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/` |
| `IGiftPackageManufactureService.cs` | MODIFY | `Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/` |
| `CatalogModule.cs` | MODIFY | `Application/Features/Catalog/` |
| `StockUpProcessingServiceTests.cs` | CREATE | `test/Anela.Heblo.Tests/Features/Catalog/Services/` |
| `ReceiveTransportBoxHandlerTests.cs` | MODIFY | `test/Anela.Heblo.Tests/Features/Logistics/Transport/` |

## Benefits

1. **Proper State Machine:** Operations transition through well-defined states (Pending → Submitted → Completed/Failed)
2. **Asynchronous Processing:** User actions (box receipt, manufacture) return immediately without blocking on Shoptet calls
3. **Fault Isolation:** One failed operation doesn't block others
4. **Idempotency:** Pre-check prevents duplicate submissions to Shoptet
5. **Consistent Pattern:** Both TransportBox and GiftPackageManufacture follow same creation pattern
6. **Single Responsibility:** Service encapsulates all stock-up operation logic
7. **Testability:** Clear separation makes mocking and testing easier

## Verification Steps

1. **Build Backend:**
   ```bash
   cd backend
   dotnet build
   ```
   - Verify no compilation errors
   - Verify all references resolved

2. **Run Tests:**
   ```bash
   cd backend
   dotnet test
   ```
   - Verify all unit tests pass
   - Verify integration tests pass
   - Verify no regressions

3. **Manual Testing (Optional):**
   - Test TransportBox receipt flow
   - Test GiftPackageManufacture flow
   - Verify operations created in Pending state
   - Verify background task processes operations
   - Verify boxes transition to Stocked when operations complete

## Rollback Plan

If issues arise during implementation:

1. **Revert commits:** Use git to revert changes
2. **Restore orchestration service:** Keep backup of deleted files until verification complete
3. **Update DI registration:** Switch back to old service registration
4. **Restore test mocks:** Revert test changes

## Future Enhancements

1. **Retry Logic:** Add automatic retry for failed operations after delay
2. **Batch Processing:** Process operations in configurable batch sizes
3. **Priority Queue:** Process high-priority operations first
4. **Monitoring:** Add metrics for processing times and failure rates
5. **Admin UI:** Add interface to manually retry failed operations

## References

- Original discussion: StockUpOrchestrationService refactoring requirements
- Existing pattern: `TransportBoxCompletionService` (background task reference)
- Existing pattern: `BackgroundRefreshSchedulerService` (scheduling infrastructure)
