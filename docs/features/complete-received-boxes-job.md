# Transport Box Completion Service - Specification

## Document Information

- **Version**: 2.0
- **Date**: 2026-01-21
- **Status**: Implemented
- **Architecture**: Background Refresh Task (NOT Hangfire recurring job)
- **Related Documentation**:
  - `/docs/features/stock-up-process.md`
  - `/docs/features/receiving.md`

## Overview

**TransportBoxCompletionService** is a background refresh task (managed by BackgroundRefreshSchedulerService) that monitors transport boxes in "Received" state and transitions them to their final state ("Stocked" or "Error") based on the completion status of their associated StockUpOperations.

This service decouples the creation of stock-up operations from the completion of transport box processing, enabling asynchronous stock-up execution while maintaining data consistency.

## Business Problem

Previously, transport boxes were immediately marked as "Stocked" after synchronous stock-up execution. This approach had limitations:

- **Tight coupling**: Box state change was directly coupled with stock-up execution
- **Blocking**: If stock-up failed, the entire batch processing stopped
- **No retry mechanism**: Failed operations required manual intervention at box level

The new approach:
- **Decoupled workflow**: Stock-up operations are created independently
- **Asynchronous completion**: Box state changes only after verification of all operations
- **Better observability**: Clear separation between operation creation and execution

## When This Task Runs

- **Frequency**: Every 2 minutes (configurable in appsettings.json)
- **Implementation**: Background refresh task via BackgroundRefreshSchedulerService
- **Task ID**: `ITransportBoxCompletionService.CompleteReceivedBoxesAsync`
- **Initial Delay**: 10 seconds after application startup
- **Hydration Tier**: 1 (higher priority refresh task)

## Job Responsibilities

### 1. Find Boxes in "Received" State

Query database for all transport boxes where:
- `State = TransportBoxState.Received`
- Box has at least one StockUpOperation linked to it

### 2. Check Stock-Up Operations Status

For each box, query all StockUpOperations where:
- `SourceType = StockUpSourceType.TransportBox`
- `SourceId = box.Id`

Evaluate completion status:
- **All Completed**: All operations have `State = StockUpOperationState.Completed`
- **Any Failed**: At least one operation has `State = StockUpOperationState.Failed`
- **In Progress**: Some operations are still `Pending` or `Submitted`

### 3. Transition Box State

Based on operation status:

| Condition | Action | Box State |
|-----------|--------|-----------|
| All operations Completed | `box.ToPick(DateTime.UtcNow, "System")` | `Stocked` |
| Any operation Failed | `box.Error(DateTime.UtcNow, "System", errorMessage)` | `Error` |
| Still in progress | No action | `Received` (unchanged) |

### 4. Persist Changes

- Save box state changes to database
- Log state transitions for audit trail
- Update `LastStateChanged` timestamp

## Workflow Diagram

```
┌────────────────────────────────────────────────────────────────┐
│ TransportBoxCompletionService (Every 2 minutes)                │
│ Background Refresh Task via BackgroundRefreshSchedulerService  │
└────────────────┬───────────────────────────────────────────────┘
                 │
                 ↓
┌────────────────────────────────────────────────────────────────┐
│ Query: SELECT * FROM TransportBoxes WHERE State = 'Received'  │
└────────────────┬───────────────────────────────────────────────┘
                 │
                 ↓
┌────────────────────────────────────────────────────────────────┐
│ For each box:                                                  │
│   Query StockUpOperations WHERE                                │
│     SourceType = 'TransportBox' AND SourceId = box.Id         │
└────────────────┬───────────────────────────────────────────────┘
                 │
      ┌──────────┴──────────┬──────────────┬───────────────┐
      ↓                     ↓              ↓               ↓
┌──────────┐      ┌──────────────┐  ┌──────────┐   ┌────────────┐
│ All      │      │ Any          │  │ Still    │   │ No         │
│ Completed│      │ Failed       │  │ Pending/ │   │ Operations │
│          │      │              │  │ Submitted│   │ Found      │
└────┬─────┘      └──────┬───────┘  └────┬─────┘   └─────┬──────┘
     │                   │               │              │
     ↓                   ↓               ↓              ↓
┌─────────────┐   ┌──────────────┐  ┌──────────┐  ┌───────────┐
│ box.ToPick()│   │ box.Error()  │  │ Skip     │  │ Log       │
│ State =     │   │ State =      │  │ (leave   │  │ Warning   │
│ Stocked     │   │ Error        │  │ Received)│  │           │
└──────┬──────┘   └──────┬───────┘  └──────────┘  └───────────┘
       │                 │
       ↓                 ↓
┌────────────────────────────────────┐
│ await SaveChangesAsync()           │
│ Log state transition               │
└────────────────────────────────────┘
```

## Implementation Details

### Domain Entity Method

**File**: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs`

**Existing methods to use**:
```csharp
public void ToPick(DateTime date, string userName)
{
    if (State != TransportBoxState.Received)
        throw new InvalidOperationException($"Cannot transition to Stocked from {State} state");

    State = TransportBoxState.Stocked;
    LastStateChanged = date;
    AddStateLog(date, State, userName);
}

public void Error(DateTime date, string userName, string message)
{
    State = TransportBoxState.Error;
    Description = message;
    LastStateChanged = date;
    AddStateLog(date, State, userName, message);
}
```

### Repository Query

**File**: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxRepository.cs`

**Add new method**:
```csharp
public interface ITransportBoxRepository
{
    // ... existing methods ...

    /// <summary>
    /// Get all transport boxes in Received state
    /// </summary>
    Task<List<TransportBox>> GetReceivedBoxesAsync(CancellationToken ct = default);
}
```

**Implementation** in `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxRepository.cs`:
```csharp
public async Task<List<TransportBox>> GetReceivedBoxesAsync(CancellationToken ct = default)
{
    return await _dbContext.TransportBoxes
        .Include(b => b.Items)
        .Include(b => b.StateLog)
        .Where(b => b.State == TransportBoxState.Received)
        .ToListAsync(ct);
}
```

### Stock-Up Operations Repository Query

**File**: `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/IStockUpOperationRepository.cs`

**Add new method**:
```csharp
public interface IStockUpOperationRepository
{
    // ... existing methods ...

    /// <summary>
    /// Get all stock-up operations for a specific transport box
    /// </summary>
    Task<List<StockUpOperation>> GetBySourceAsync(
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default);
}
```

**Implementation** in `backend/src/Anela.Heblo.Persistence/Catalog/StockUpOperationRepository.cs`:
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

### Service Interface

**File**: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/ITransportBoxCompletionService.cs`

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

### Service Implementation

**File**: `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`

```csharp
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Services;

/// <summary>
/// Background service that transitions transport boxes from "Received" to "Stocked"
/// after all their stock-up operations complete successfully
/// </summary>
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
                _logger.LogError(ex, "Unexpected error processing box {BoxId} ({BoxCode})", box.Id, box.Code);
                errorCount++;
            }
        }

        _logger.LogInformation(
            "CompleteReceivedBoxes finished. Completed: {Completed}, Failed: {Failed}, Skipped: {Skipped}",
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

### Service Registration

**File**: `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`

**Service registration**:
```csharp
// Register transport box completion service
services.AddTransient<ITransportBoxCompletionService, TransportBoxCompletionService>();

// Register background refresh task for completing received boxes
services.RegisterRefreshTask<ITransportBoxCompletionService>(
    nameof(ITransportBoxCompletionService.CompleteReceivedBoxesAsync),
    (service, ct) => service.CompleteReceivedBoxesAsync(ct)
);
```

**Key points**:
- Service registered as **Transient** (new instance per execution)
- Background task registered with **RegisterRefreshTask** extension method
- Task ID: `ITransportBoxCompletionService.CompleteReceivedBoxesAsync`
- Managed by BackgroundRefreshSchedulerService, NOT Hangfire

## Configuration

### Background Refresh Task Configuration

**File**: `backend/src/Anela.Heblo.API/appsettings.json`

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
- `Enabled`: true - Task runs automatically on application startup
- `InitialDelay`: 10 seconds - Delay before first execution after startup
- `RefreshInterval`: 2 minutes - Time between executions (recommended)
- `HydrationTier`: 1 - Priority tier for hydration (lower = higher priority)

### Development Configuration

**File**: `backend/src/Anela.Heblo.API/appsettings.Development.json`

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

### Refresh Interval Options

| Frequency | TimeSpan Format | Use Case |
|-----------|----------------|----------|
| Every 1 minute | `"00:01:00"` | Development, high-priority environments |
| Every 2 minutes | `"00:02:00"` | **Recommended** - Balance between responsiveness and load |
| Every 5 minutes | `"00:05:00"` | Low-priority environments, lower system load |

## Monitoring & Observability

### Key Metrics

- **Boxes processed per run**: Number of boxes checked
- **Boxes completed**: Boxes transitioned to Stocked state
- **Boxes failed**: Boxes transitioned to Error state
- **Boxes skipped**: Boxes left in Received (operations still in progress)
- **Average processing time**: Time taken to process all boxes
- **Error rate**: Percentage of boxes that failed

### Logging

**Log levels**:
- **Information**: Job start/finish, box state transitions
- **Debug**: Individual box processing details
- **Warning**: Unexpected states, boxes with no operations, partial failures
- **Error**: Exceptions during processing

**Example logs**:
```
[INFO] Starting CompleteReceivedBoxes background task
[INFO] Found 5 transport boxes in Received state
[DEBUG] Processing box 123 (BOX-000123)
[INFO] All 3 stock-up operations for box 123 (BOX-000123) completed, marking as Stocked
[WARN] Box 456 (BOX-000456) has 1 failed stock-up operations, marking as Error
[DEBUG] Box 789 (BOX-000789) still has 2 operations in progress, skipping
[INFO] CompleteReceivedBoxes finished. Completed: 1, Failed: 1, Skipped: 3
```

### Dashboard Integration

Add to Stock Operations page:
- **Received Boxes Count**: Number of boxes waiting for completion
- **Oldest Received Box**: Time since oldest box was received (alert if > 10 minutes)
- **Stuck Operations**: Operations in Pending/Submitted state for > 5 minutes

## Error Handling

### Failure Scenarios

| Scenario | Handling | Recovery |
|----------|----------|----------|
| **No operations found for box** | Mark box as Error | Manual review, recreate operations if needed |
| **Database connection error** | Log error, skip current run | Automatic retry on next run |
| **Concurrent modification** | Optimistic concurrency exception | Automatic retry on next run |
| **Partial failures** | Mark box as Error, list failed operations | Manual retry of failed operations |

### Error Recovery

1. **Manual Retry via UI**: Failed boxes can be reset to Received state
2. **Operation Retry**: Individual StockUpOperations can be retried
3. **Box Retry**: Reset entire box to Received and restart process

## Testing Strategy

### Unit Tests

**File**: `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs`

**Test cases**:
- ✅ No boxes in Received state → No processing
- ✅ All operations Completed → Box transitions to Stocked
- ✅ Any operation Failed → Box transitions to Error
- ✅ Operations still Pending/Submitted → Box remains Received
- ✅ No operations for box → Box marked as Error
- ✅ Multiple boxes processed correctly
- ✅ Exception handling for individual box does not stop batch

### Integration Tests

**File**: `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceIntegrationTests.cs`

**Test scenarios**:
- Create box with items → Create StockUpOperations → Mark operations Completed → Job runs → Box is Stocked
- Create box with items → Mark one operation Failed → Job runs → Box is Error
- Create box with items → Leave operations Pending → Job runs → Box stays Received

## Performance Considerations

- **Query optimization**: Use indexes on `TransportBox.State` and `StockUpOperation.SourceType + SourceId`
- **Batch size**: Process all boxes in single execution to maintain consistency
- **Concurrency**: Background refresh scheduler ensures single instance execution
- **Transient service**: New instance per execution prevents memory leaks
- **Frequency**: 2 minutes provides good balance between responsiveness and load

## Future Enhancements

1. **Event-driven completion**: Trigger box completion via domain event after last operation completes
2. **Metrics dashboard**: Real-time visualization of box processing pipeline
3. **Alerting**: Notifications for boxes stuck in Received state > 10 minutes
4. **Batch optimization**: Process only boxes with recent operation updates

---

**Document Version**: 2.0
**Last Updated**: 2026-01-21
**Changes**:
- v2.0: Corrected architecture - Background Refresh Task (NOT Hangfire recurring job)
- v2.0: Updated service name: TransportBoxCompletionService (not CompleteReceivedBoxesJob)
- v2.0: Updated configuration structure for BackgroundRefreshSchedulerService
- v2.0: Clarified service registration and task registration patterns
- v1.0: Initial specification for Hangfire job (incorrect architecture)
**Author**: System Documentation
**Status**: Implemented and Documented
