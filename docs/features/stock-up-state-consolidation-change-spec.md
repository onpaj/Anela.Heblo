# Stock-Up Process: State Consolidation and UI Retry - Change Specification

## Document Information

- **Version**: 1.1
- **Date**: 2026-01-20
- **Status**: Ready for Implementation
- **Related Documentation**: `/docs/features/stock-up-process.md`
- **Changes in v1.1**: Added stuck operations detection and enhanced retry functionality for all retryable states

## Overview

This change specification addresses three key improvements to the Stock-Up process:

1. **State Consolidation**: Consolidate `Verified` and `Completed` states into a single `Completed` state, reflecting the actual implementation where both states are set atomically.

2. **Enhanced Retry Functionality**: Extend retry capability in the UI to all retryable states (Failed, Submitted, Pending), not just Failed operations. This addresses scenarios where operations can become "stuck" due to process crashes or timeouts.

3. **Stuck Operation Detection**: Add visual indicators in the UI to identify operations that have been in non-terminal states (Pending, Submitted) for an unusually long time, indicating potential issues requiring manual intervention.

## Business Rationale

### Current Issue
The codebase currently maintains a `Verified` state that serves no functional purpose:
- `MarkAsVerified()` and `MarkAsCompleted()` are called immediately after each other (same timestamp)
- There is no business logic between verification and completion
- This creates confusion about the state machine and adds unnecessary complexity

### Solution
Consolidate `Verified` and `Completed` into a single `Completed` state:
- Simplifies state machine: `Pending → Submitted → Completed`
- Reduces cognitive load for developers
- More accurately reflects actual business process
- Maintains all existing functionality

### Benefits
- **Simpler State Machine**: 4 states instead of 5
- **Clearer Semantics**: "Completed" implies verification was successful
- **Easier Maintenance**: Less state transitions to manage
- **No Functional Loss**: Both verification and completion still happen, just not tracked separately

## Scope

### In Scope
**State Consolidation**:
- Remove `Verified` state from `StockUpOperationState` enum
- Remove `VerifiedAt` property from domain entity and DTOs
- Update domain methods: remove `MarkAsVerified()`, modify `MarkAsCompleted()`
- Update orchestration service to skip verification state
- Update database migration (add new migration to remove column)

**Enhanced Retry Functionality**:
- Extend UI retry button to support `Pending`, `Submitted`, and `Failed` states
- Add visual differentiation for retry buttons based on operation state
- Add confirmation dialogs with state-specific messaging

**Stuck Operation Detection**:
- Add helper function to detect operations stuck in non-terminal states
- Add visual warning indicators for stuck operations (age-based)
- Add tooltip explanations for stuck operation warnings

**Testing**:
- Update all unit and integration tests
- Add tests for stuck operation detection logic
- Add UI tests for retry button on all retryable states

### Out of Scope
- Automatic retry/recovery mechanisms (future enhancement)
- Background monitoring jobs for stuck operations
- Email/webhook notifications for stuck operations
- Changes to 4-layer protection system (no changes needed)
- Changes to Shoptet integration (no changes needed)

## Stuck Operations Analysis

### Problem Statement

The current retry functionality only supports `Failed` operations. However, operations can become "stuck" in non-Failed states due to process crashes, timeouts, or infrastructure issues. These stuck operations require manual intervention but are not easily identifiable in the UI.

### When Operations Get Stuck

The orchestration service has critical points where process termination can leave operations in intermediate states:

```csharp
// === Submit to Shoptet ===
try
{
    operation.MarkAsSubmitted(DateTime.UtcNow);
    await _repository.SaveChangesAsync(ct);  // ← COMMIT TO DATABASE!

    // [CRASH POINT 1: Process killed after DB commit but before Shoptet call]

    await _eshopService.StockUpAsync(request);  // ← Playwright automation (can timeout/crash)

    // [CRASH POINT 2: Process killed after Shoptet call but before verification]
}
catch (Exception ex)
{
    operation.MarkAsFailed(...);  // ← This will NEVER execute if process crashes!
}
```

### Stuck Operation Scenarios

| Scenario | Cause | Stuck State | Can Recover? |
|----------|-------|-------------|--------------|
| **Process crash after `SaveChanges`** | Pod restart, OOM kill, infrastructure failure | `Submitted` | ✅ Yes (via retry) |
| **Playwright timeout during submit** | Shoptet slow/unresponsive, network issues | `Submitted` | ✅ Yes (via retry) |
| **Process crash during verification** | Crash after successful submit | `Submitted` | ✅ Yes (may cause duplicate) |
| **Kubernetes pod eviction** | Node maintenance, resource constraints | `Pending` or `Submitted` | ✅ Yes (via retry) |
| **Database connection lost** | Network partition during commit | `Pending` | ✅ Yes (via retry) |
| **Unhandled exception outside try-catch** | Programming error | Any state | ⚠️ Depends on state |

**Key Insight**: Operations stuck in `Submitted` state are the most common and critical case. The operation was committed to the database but may not have reached Shoptet.

### Backend Retry Support (Already Implemented)

The backend **already supports retry from any state** via `ForceReset()`:

```csharp
// RetryStockUpOperationHandler.cs
if (operation.State == StockUpOperationState.Failed)
{
    operation.Reset();  // Normal reset for explicitly failed operations
}
else
{
    _logger.LogWarning("Force resetting stuck operation {OperationId} from {State} state",
        operation.Id, operation.State);
    operation.ForceReset();  // Force reset for Pending/Submitted/Verified
}
```

**The UI just needs to expose this capability for non-Failed states.**

### Stuck Operation Detection Rules

Operations should be considered "stuck" based on age:

| State | Stuck Threshold | Rationale |
|-------|----------------|-----------|
| **Pending** | > 10 minutes | Should be picked up by orchestrator within seconds |
| **Submitted** | > 5 minutes | Shoptet submit + verify should complete in < 2 minutes |
| **Completed** | Never | Terminal state |
| **Failed** | Never | Terminal state (explicitly marked) |

**Age Calculation**: Time difference between current time and the relevant state timestamp:
- **Pending**: `now - createdAt`
- **Submitted**: `now - submittedAt`

## Technical Changes

### 1. Domain Layer Changes

#### 1.1. Update `StockUpOperationState` Enum

**File**: `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperationState.cs`

**Current**:
```csharp
public enum StockUpOperationState
{
    Pending = 0,
    Submitted = 1,
    Verified = 2,
    Completed = 3,
    Failed = 4
}
```

**New**:
```csharp
public enum StockUpOperationState
{
    Pending = 0,
    Submitted = 1,
    Completed = 2,
    Failed = 3
}
```

**Impact**: This is a breaking change for database records with `state = 2` (Verified). Migration needed.

#### 1.2. Update `StockUpOperation` Entity

**File**: `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs`

**Changes**:

1. **Remove property**:
```csharp
public DateTime? VerifiedAt { get; private set; }
```

2. **Remove method**:
```csharp
public void MarkAsVerified(DateTime timestamp) { ... }
```

3. **Update `MarkAsCompleted()` method**:

**Current**:
```csharp
public void MarkAsCompleted(DateTime timestamp)
{
    if (State != StockUpOperationState.Verified && State != StockUpOperationState.Pending)
        throw new InvalidOperationException($"Cannot mark as Completed from {State} state");

    State = StockUpOperationState.Completed;
    CompletedAt = timestamp;
}
```

**New**:
```csharp
public void MarkAsCompleted(DateTime timestamp)
{
    // Can transition from Submitted (after verification) or Pending (if already in Shoptet)
    if (State != StockUpOperationState.Submitted && State != StockUpOperationState.Pending)
        throw new InvalidOperationException($"Cannot mark as Completed from {State} state");

    State = StockUpOperationState.Completed;
    CompletedAt = timestamp;
}
```

4. **Update `Reset()` method**:

**Current**:
```csharp
public void Reset()
{
    if (State != StockUpOperationState.Failed)
        throw new InvalidOperationException($"Can only reset Failed operations, current state: {State}");

    State = StockUpOperationState.Pending;
    SubmittedAt = null;
    VerifiedAt = null;  // <-- Remove this line
    CompletedAt = null;
    ErrorMessage = null;
}
```

**New**:
```csharp
public void Reset()
{
    if (State != StockUpOperationState.Failed)
        throw new InvalidOperationException($"Can only reset Failed operations, current state: {State}");

    State = StockUpOperationState.Pending;
    SubmittedAt = null;
    CompletedAt = null;
    ErrorMessage = null;
}
```

5. **Update `ForceReset()` method**:

**Current**:
```csharp
public void ForceReset()
{
    if (State == StockUpOperationState.Completed)
        throw new InvalidOperationException("Cannot force reset Completed operations");

    State = StockUpOperationState.Pending;
    SubmittedAt = null;
    VerifiedAt = null;  // <-- Remove this line
    CompletedAt = null;
    ErrorMessage = null;
}
```

**New**:
```csharp
public void ForceReset()
{
    if (State == StockUpOperationState.Completed)
        throw new InvalidOperationException("Cannot force reset Completed operations");

    State = StockUpOperationState.Pending;
    SubmittedAt = null;
    CompletedAt = null;
    ErrorMessage = null;
}
```

### 2. Application Layer Changes

#### 2.1. Update `StockUpOrchestrationService`

**File**: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpOrchestrationService.cs`

**Change in `ExecuteAsync()` method** (lines 124-151):

**Current**:
```csharp
// === LAYER 4: Post-verify in Shoptet history ===
_logger.LogDebug("Verifying {DocumentNumber} in Shoptet history after submission", documentNumber);

try
{
    var verified = await _eshopService.VerifyStockUpExistsAsync(documentNumber);
    if (verified)
    {
        operation.MarkAsVerified(DateTime.UtcNow);  // <-- Remove this call
        operation.MarkAsCompleted(DateTime.UtcNow);
        await _repository.SaveChangesAsync(ct);
        _logger.LogInformation("Operation {DocumentNumber} verified and completed successfully", documentNumber);
        return StockUpOperationResult.Success(operation);
    }
    else
    {
        _logger.LogError("Verification failed: {DocumentNumber} not found in Shoptet history after submission",
            documentNumber);
        operation.MarkAsFailed(DateTime.UtcNow,
            "Verification failed: Record not found in Shoptet history");
        await _repository.SaveChangesAsync(ct);
        return StockUpOperationResult.VerificationFailed(operation);
    }
}
catch (Exception ex)
{
    _logger.LogError(ex, "Verification error for {DocumentNumber}", documentNumber);
    operation.MarkAsFailed(DateTime.UtcNow, $"Verification error: {ex.Message}");
    await _repository.SaveChangesAsync(ct);
    return StockUpOperationResult.VerificationError(operation, ex);
}
```

**New**:
```csharp
// === LAYER 4: Post-verify in Shoptet history ===
_logger.LogDebug("Verifying {DocumentNumber} in Shoptet history after submission", documentNumber);

try
{
    var verified = await _eshopService.VerifyStockUpExistsAsync(documentNumber);
    if (verified)
    {
        operation.MarkAsCompleted(DateTime.UtcNow);
        await _repository.SaveChangesAsync(ct);
        _logger.LogInformation("Operation {DocumentNumber} verified and completed successfully", documentNumber);
        return StockUpOperationResult.Success(operation);
    }
    else
    {
        _logger.LogError("Verification failed: {DocumentNumber} not found in Shoptet history after submission",
            documentNumber);
        operation.MarkAsFailed(DateTime.UtcNow,
            "Verification failed: Record not found in Shoptet history");
        await _repository.SaveChangesAsync(ct);
        return StockUpOperationResult.VerificationFailed(operation);
    }
}
catch (Exception ex)
{
    _logger.LogError(ex, "Verification error for {DocumentNumber}", documentNumber);
    operation.MarkAsFailed(DateTime.UtcNow, $"Verification error: {ex.Message}");
    await _repository.SaveChangesAsync(ct);
    return StockUpOperationResult.VerificationError(operation, ex);
}
```

**Comment Update**: Change comment on line 122:
```csharp
// === LAYER 4: Post-verify in Shoptet history (marks as Completed if found) ===
```

#### 2.2. Update `StockUpOperationDto`

**File**: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperations/GetStockUpOperationsResponse.cs`

**Remove property**:
```csharp
public DateTime? VerifiedAt { get; set; }
```

**Result**:
```csharp
public class StockUpOperationDto
{
    public int Id { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public int Amount { get; set; }
    public StockUpOperationState State { get; set; }
    public StockUpSourceType SourceType { get; set; }
    public int SourceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
```

### 3. Infrastructure Layer Changes

#### 3.1. Database Migration

**Create new migration**: `RemoveVerifiedStateFromStockUpOperations`

**Purpose**: Remove `VerifiedAt` column and migrate existing `Verified` state records to `Completed`

**Migration Up**:
```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Step 1: Update existing records with State = 2 (Verified) to State = 3 (Completed - old value)
    // Note: After enum change, Completed will be 2, but we need to handle existing data first
    migrationBuilder.Sql(@"
        UPDATE ""StockUpOperations""
        SET ""State"" = 3
        WHERE ""State"" = 2;
    ");

    // Step 2: Update records with State = 3 or 4 to new values (Completed = 2, Failed = 3)
    migrationBuilder.Sql(@"
        UPDATE ""StockUpOperations""
        SET ""State"" = 2
        WHERE ""State"" = 3;

        UPDATE ""StockUpOperations""
        SET ""State"" = 3
        WHERE ""State"" = 4;
    ");

    // Step 3: Remove VerifiedAt column
    migrationBuilder.DropColumn(
        name: "VerifiedAt",
        table: "StockUpOperations");
}
```

**Migration Down**:
```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    // Step 1: Add VerifiedAt column back
    migrationBuilder.AddColumn<DateTime>(
        name: "VerifiedAt",
        table: "StockUpOperations",
        type: "timestamp with time zone",
        nullable: true);

    // Step 2: Restore old state values (reverse mapping)
    migrationBuilder.Sql(@"
        UPDATE ""StockUpOperations""
        SET ""State"" = 4
        WHERE ""State"" = 3;

        UPDATE ""StockUpOperations""
        SET ""State"" = 3
        WHERE ""State"" = 2;
    ");

    // Note: We cannot restore Verified state as we don't know which Completed records were originally Verified
    // All Completed records will remain Completed after downgrade
}
```

**Command to generate**:
```bash
cd backend/src/Anela.Heblo.Persistence
dotnet ef migrations add RemoveVerifiedStateFromStockUpOperations --startup-project ../Anela.Heblo.API
```

### 4. Frontend Changes

#### 4.1. Update `StockOperationsPage.tsx`

**File**: `frontend/src/pages/StockOperationsPage.tsx`

**Changes**:

1. **Remove `Verified` from `getStateColor()` function** (lines 20-35):

**Current**:
```typescript
const getStateColor = (state: StockUpOperationState) => {
  switch (state) {
    case StockUpOperationState.Completed:
      return 'bg-green-100 text-green-800';
    case StockUpOperationState.Failed:
      return 'bg-red-100 text-red-800';
    case StockUpOperationState.Pending:
      return 'bg-yellow-100 text-yellow-800';
    case StockUpOperationState.Submitted:
      return 'bg-blue-100 text-blue-800';
    case StockUpOperationState.Verified:  // <-- Remove this case
      return 'bg-indigo-100 text-indigo-800';
    default:
      return 'bg-gray-100 text-gray-800';
  }
};
```

**New**:
```typescript
const getStateColor = (state: StockUpOperationState) => {
  switch (state) {
    case StockUpOperationState.Completed:
      return 'bg-green-100 text-green-800';
    case StockUpOperationState.Failed:
      return 'bg-red-100 text-red-800';
    case StockUpOperationState.Pending:
      return 'bg-yellow-100 text-yellow-800';
    case StockUpOperationState.Submitted:
      return 'bg-blue-100 text-blue-800';
    default:
      return 'bg-gray-100 text-gray-800';
  }
};
```

2. **Remove `Verified` from `getStateIcon()` function** (lines 37-51):

**Current**:
```typescript
const getStateIcon = (state: StockUpOperationState) => {
  switch (state) {
    case StockUpOperationState.Completed:
      return <CheckCircle className="h-4 w-4" />;
    case StockUpOperationState.Failed:
      return <XCircle className="h-4 w-4" />;
    case StockUpOperationState.Pending:
      return <Clock className="h-4 w-4" />;
    case StockUpOperationState.Submitted:
    case StockUpOperationState.Verified:  // <-- Remove this case
      return <RefreshCw className="h-4 w-4" />;
    default:
      return null;
  }
};
```

**New**:
```typescript
const getStateIcon = (state: StockUpOperationState) => {
  switch (state) {
    case StockUpOperationState.Completed:
      return <CheckCircle className="h-4 w-4" />;
    case StockUpOperationState.Failed:
      return <XCircle className="h-4 w-4" />;
    case StockUpOperationState.Pending:
      return <Clock className="h-4 w-4" />;
    case StockUpOperationState.Submitted:
      return <RefreshCw className="h-4 w-4" />;
    default:
      return null;
  }
};
```

3. **Remove `Verified` from filter dropdown** (lines 112-138):

**Current**:
```tsx
<select>
  <option value="">Všechny</option>
  <option value={StockUpOperationState.Failed}>Failed</option>
  <option value={StockUpOperationState.Pending}>Pending</option>
  <option value={StockUpOperationState.Submitted}>Submitted</option>
  <option value={StockUpOperationState.Verified}>Verified</option>  {/* <-- Remove this line */}
  <option value={StockUpOperationState.Completed}>Completed</option>
</select>
```

**New**:
```tsx
<select>
  <option value="">Všechny</option>
  <option value={StockUpOperationState.Failed}>Failed</option>
  <option value={StockUpOperationState.Pending}>Pending</option>
  <option value={StockUpOperationState.Submitted}>Submitted</option>
  <option value={StockUpOperationState.Completed}>Completed</option>
</select>
```

#### 4.2. Regenerate OpenAPI Client

After backend changes are complete, regenerate the TypeScript API client:

**Command**:
```bash
cd backend/src/Anela.Heblo.API
dotnet build
# This will trigger PostBuild event that regenerates frontend client
```

**Verify**: Check that `frontend/src/api/generated/api-client.ts` no longer contains `Verified` in `StockUpOperationState` enum.

#### 4.3. Add Stuck Operation Detection Helper

**File**: `frontend/src/pages/StockOperationsPage.tsx`

**Add new helper function** (after state declarations, before `useStockUpOperationsQuery`):

```typescript
import { differenceInMinutes } from 'date-fns';

// Helper to detect stuck operations
const isOperationStuck = (operation: StockUpOperationDto): boolean => {
  const now = new Date();

  if (operation.state === StockUpOperationState.Submitted && operation.submittedAt) {
    const minutesSinceSubmit = differenceInMinutes(now, new Date(operation.submittedAt));
    return minutesSinceSubmit > 5; // Submitted for more than 5 minutes
  }

  if (operation.state === StockUpOperationState.Pending) {
    const minutesSinceCreation = differenceInMinutes(now, new Date(operation.createdAt));
    return minutesSinceCreation > 10; // Pending for more than 10 minutes
  }

  return false;
};

// Helper to get stuck operation message
const getStuckMessage = (operation: StockUpOperationDto): string => {
  const now = new Date();

  if (operation.state === StockUpOperationState.Submitted && operation.submittedAt) {
    const minutes = differenceInMinutes(now, new Date(operation.submittedAt));
    return `Operace je ve stavu Submitted ${minutes} minut. Může být uvízlá.`;
  }

  if (operation.state === StockUpOperationState.Pending) {
    const minutes = differenceInMinutes(now, new Date(operation.createdAt));
    return `Operace je ve stavu Pending ${minutes} minut. Nebyla zpracována.`;
  }

  return '';
};
```

**Install date-fns** if not already present:
```bash
npm install date-fns
```

#### 4.4. Enhanced Retry Button Logic

**File**: `frontend/src/pages/StockOperationsPage.tsx`

**Update retry button rendering** (currently at lines 208-219):

**Current**:
```tsx
<td className="px-6 py-4 whitespace-nowrap text-sm">
  {(operation.state ?? StockUpOperationState.Pending) === StockUpOperationState.Failed && operation.id && (
    <button
      onClick={() => handleRetry(operation.id!)}
      disabled={retryMutation.isPending}
      className="inline-flex items-center px-3 py-1 bg-orange-600 hover:bg-orange-700 disabled:bg-gray-400 text-white text-xs font-medium rounded transition-colors duration-200"
    >
      <RefreshCw className="h-3 w-3 mr-1" />
      Opakovat
    </button>
  )}
</td>
```

**New**:
```tsx
<td className="px-6 py-4 whitespace-nowrap text-sm">
  {canRetry(operation.state) && operation.id && (
    <button
      onClick={() => handleRetryWithConfirmation(operation)}
      disabled={retryMutation.isPending}
      className={`inline-flex items-center px-3 py-1 disabled:bg-gray-400 text-white text-xs font-medium rounded transition-colors duration-200 ${getRetryButtonColor(operation.state)}`}
      title={getRetryButtonTooltip(operation.state)}
    >
      {getRetryButtonIcon(operation.state)}
      <span className="ml-1">{getRetryButtonLabel(operation.state)}</span>
    </button>
  )}
</td>
```

**Add new helper functions** (after `handleRetry` function):

```typescript
// Check if operation can be retried
const canRetry = (state?: StockUpOperationState): boolean => {
  if (!state) return false;
  return state === StockUpOperationState.Failed ||
         state === StockUpOperationState.Submitted ||
         state === StockUpOperationState.Pending;
};

// Get retry button color based on state
const getRetryButtonColor = (state?: StockUpOperationState): string => {
  switch (state) {
    case StockUpOperationState.Failed:
      return 'bg-red-600 hover:bg-red-700';
    case StockUpOperationState.Submitted:
      return 'bg-orange-500 hover:bg-orange-600';
    case StockUpOperationState.Pending:
      return 'bg-yellow-600 hover:bg-yellow-700';
    default:
      return 'bg-gray-400';
  }
};

// Get retry button label based on state
const getRetryButtonLabel = (state?: StockUpOperationState): string => {
  switch (state) {
    case StockUpOperationState.Failed:
      return 'Opakovat';
    case StockUpOperationState.Submitted:
      return 'Znovu zkusit';
    case StockUpOperationState.Pending:
      return 'Spustit';
    default:
      return 'Retry';
  }
};

// Get retry button icon based on state
const getRetryButtonIcon = (state?: StockUpOperationState): JSX.Element => {
  switch (state) {
    case StockUpOperationState.Failed:
      return <RefreshCw className="h-3 w-3" />;
    case StockUpOperationState.Submitted:
      return <AlertTriangle className="h-3 w-3" />;
    case StockUpOperationState.Pending:
      return <Play className="h-3 w-3" />;
    default:
      return <RefreshCw className="h-3 w-3" />;
  }
};

// Get retry button tooltip
const getRetryButtonTooltip = (state?: StockUpOperationState): string => {
  switch (state) {
    case StockUpOperationState.Failed:
      return 'Operace explicitně selhala. Klikněte pro nový pokus.';
    case StockUpOperationState.Submitted:
      return 'Operace může být uvízlá po selhání procesu. Klikněte pro restart.';
    case StockUpOperationState.Pending:
      return 'Operace nebyla nikdy zpracována. Klikněte pro spuštění.';
    default:
      return 'Retry operation';
  }
};

// Handle retry with state-specific confirmation
const handleRetryWithConfirmation = async (operation: StockUpOperationDto) => {
  const messages: Record<StockUpOperationState, string> = {
    [StockUpOperationState.Failed]: 'Opravdu chcete znovu spustit tuto selhanou operaci?',
    [StockUpOperationState.Submitted]:
      'Tato operace je ve stavu Submitted. Pokud je uvízlá, retry může způsobit duplikát v Shoptet. Pokračovat?',
    [StockUpOperationState.Pending]:
      'Tato operace nebyla nikdy zpracována. Chcete ji spustit?',
    [StockUpOperationState.Completed]: '', // Won't be shown
  };

  const confirmMessage = operation.state
    ? messages[operation.state]
    : 'Opravdu chcete znovu spustit tuto operaci?';

  if (window.confirm(confirmMessage)) {
    try {
      await retryMutation.mutateAsync(operation.id!);
      refetch(); // Refresh the list after retry
    } catch (error) {
      console.error('Chyba při opakování operace:', error);
    }
  }
};
```

**Add new imports** (at top of file):
```typescript
import { AlertTriangle, Play } from 'lucide-react';
```

#### 4.5. Add Stuck Operation Visual Indicator

**File**: `frontend/src/pages/StockOperationsPage.tsx`

**Update state badge rendering** (currently around line 196-201):

**Current**:
```tsx
<td className="px-6 py-4 whitespace-nowrap">
  <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStateColor(operation.state ?? StockUpOperationState.Pending)}`}>
    {getStateIcon(operation.state ?? StockUpOperationState.Pending)}
    <span className="ml-1">{StockUpOperationState[operation.state ?? StockUpOperationState.Pending]}</span>
  </span>
</td>
```

**New**:
```tsx
<td className="px-6 py-4 whitespace-nowrap">
  <div className="flex items-center space-x-2">
    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStateColor(operation.state ?? StockUpOperationState.Pending)}`}>
      {getStateIcon(operation.state ?? StockUpOperationState.Pending)}
      <span className="ml-1">{StockUpOperationState[operation.state ?? StockUpOperationState.Pending]}</span>
    </span>

    {isOperationStuck(operation) && (
      <span
        className="inline-flex items-center text-red-600"
        title={getStuckMessage(operation)}
      >
        <AlertTriangle className="h-4 w-4 animate-pulse" />
      </span>
    )}
  </div>
</td>
```

**This adds**:
- Pulsing warning triangle icon for stuck operations
- Tooltip with detailed stuck message
- Visual indicator next to state badge

### 5. Test Updates

#### 5.1. Update Domain Entity Tests

**File**: `backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpOperationTests.cs`

**Changes needed**:
- Remove all tests for `MarkAsVerified()` method
- Update tests for `MarkAsCompleted()` to work with `Submitted` state instead of `Verified`
- Update tests for `Reset()` and `ForceReset()` to not check `VerifiedAt`

**Example test to update**:

**Current**:
```csharp
[Fact]
public void MarkAsCompleted_FromVerified_ShouldSucceed()
{
    // Arrange
    var operation = CreateOperation();
    operation.MarkAsSubmitted(DateTime.UtcNow);
    operation.MarkAsVerified(DateTime.UtcNow);

    // Act
    var timestamp = DateTime.UtcNow;
    operation.MarkAsCompleted(timestamp);

    // Assert
    operation.State.Should().Be(StockUpOperationState.Completed);
    operation.CompletedAt.Should().Be(timestamp);
}
```

**New**:
```csharp
[Fact]
public void MarkAsCompleted_FromSubmitted_ShouldSucceed()
{
    // Arrange
    var operation = CreateOperation();
    operation.MarkAsSubmitted(DateTime.UtcNow);

    // Act
    var timestamp = DateTime.UtcNow;
    operation.MarkAsCompleted(timestamp);

    // Assert
    operation.State.Should().Be(StockUpOperationState.Completed);
    operation.CompletedAt.Should().Be(timestamp);
}
```

**Tests to remove**:
- `MarkAsVerified_FromSubmitted_ShouldSucceed`
- `MarkAsVerified_FromPending_ShouldThrow`
- `MarkAsVerified_SetsVerifiedAtTimestamp`
- Any other tests specifically for `MarkAsVerified()`

#### 5.2. Update Orchestration Service Tests

**File**: `backend/test/Anela.Heblo.Tests/Features/Catalog/Services/StockUpOrchestrationServiceTests.cs`

**Changes needed**:
- Update assertions to check for `Completed` instead of `Verified`
- Remove checks for `VerifiedAt` timestamp
- Verify that `MarkAsCompleted()` is called directly after successful verification

**Example**:

**Current**:
```csharp
// Assert
result.Status.Should().Be(StockUpResultStatus.Success);
operation.State.Should().Be(StockUpOperationState.Completed);
operation.VerifiedAt.Should().NotBeNull();
operation.CompletedAt.Should().NotBeNull();
```

**New**:
```csharp
// Assert
result.Status.Should().Be(StockUpResultStatus.Success);
operation.State.Should().Be(StockUpOperationState.Completed);
operation.CompletedAt.Should().NotBeNull();
```

### 6. API Documentation Updates

No changes needed - OpenAPI documentation will automatically reflect the updated enum and DTO structure after regeneration.

## Implementation Order

**CRITICAL**: Follow this exact order to avoid breaking changes during deployment:

### Phase 1: Backend Changes (Database Compatible)
1. ✅ Update documentation (`stock-up-process.md`) - **COMPLETED**
2. Update domain entity:
   - Remove `MarkAsVerified()` method
   - Update `MarkAsCompleted()` to accept transition from `Submitted`
   - Remove `VerifiedAt` property
   - Update `Reset()` and `ForceReset()` methods
3. Update enum: Remove `Verified` state, renumber `Completed` and `Failed`
4. Update orchestration service: Remove `MarkAsVerified()` call
5. Update DTO: Remove `VerifiedAt` property
6. Update all backend tests
7. Run backend tests: `dotnet test`
8. Create and run database migration

### Phase 2: Frontend Changes
9. Regenerate API client: `dotnet build` (in API project)
10. Install date-fns: `npm install date-fns`
11. Update `StockOperationsPage.tsx`:
    - Remove `Verified` from `getStateColor()`
    - Remove `Verified` from `getStateIcon()`
    - Remove `Verified` from filter dropdown
    - Add stuck operation detection helpers (`isOperationStuck`, `getStuckMessage`)
    - Add enhanced retry helpers (`canRetry`, `getRetryButtonColor`, etc.)
    - Update retry button to support all retryable states
    - Add stuck operation visual indicator (pulsing warning icon)
    - Update imports (add `AlertTriangle`, `Play`, `differenceInMinutes`)
12. Run frontend tests: `npm test`
13. Build frontend: `npm run build`

### Phase 3: Validation
14. Test locally:
    - Start backend: `dotnet run` (in API project)
    - Start frontend: `npm start`
    - Navigate to Stock Operations page
    - Verify states display correctly
    - Test retry button on Failed, Submitted, and Pending operations
    - Verify stuck operation warnings appear for old operations
    - Test confirmation dialogs for each state
15. Run E2E tests (if available)
16. Deploy to staging environment
17. Smoke test on staging

## Rollback Plan

If issues are discovered after deployment:

### Immediate Rollback (Before Database Migration)
- Revert code changes via Git
- No database changes needed

### Post-Migration Rollback
1. Revert code via Git
2. Run migration down: `dotnet ef database update <previous-migration-name>`
3. Note: Any operations completed during the new version will lose their `VerifiedAt` timestamp

## Testing Strategy

### Unit Tests
- ✅ All domain entity state transitions
- ✅ Orchestration service execution paths
- ✅ Retry logic for all applicable states (Failed, Submitted, Pending)
- ✅ Stuck operation detection logic (`isOperationStuck`)
  - Test Pending operation older than 10 minutes returns true
  - Test Submitted operation older than 5 minutes returns true
  - Test Completed operation never returns true
  - Test Failed operation never returns true
  - Test recent operations return false

### Integration Tests
- Test complete flow: Create → Submit → Complete
- Test retry flow from Failed: Create → Submit → Fail → Retry → Complete
- Test retry flow from Submitted: Create → Submit → [Stuck] → Retry → Complete
- Test retry flow from Pending: Create → [Never processed] → Retry → Complete
- Test pre-check flow: Create → Already in Shoptet → Complete

### Frontend Unit Tests (Jest/React Testing Library)
**New tests for `StockOperationsPage.tsx`**:
- Test `isOperationStuck()` helper function
  - Mock operations with different ages and states
  - Verify stuck detection thresholds (5min for Submitted, 10min for Pending)
- Test `canRetry()` helper function
  - Verify returns true for Failed, Submitted, Pending
  - Verify returns false for Completed
- Test retry button rendering
  - Verify button appears for Failed operations (red)
  - Verify button appears for Submitted operations (orange)
  - Verify button appears for Pending operations (yellow)
  - Verify button does not appear for Completed operations
- Test stuck operation visual indicator
  - Verify warning icon appears for stuck operations
  - Verify tooltip shows correct message
  - Verify icon has pulse animation
- Test retry confirmation dialogs
  - Verify different confirmation messages for each state
  - Verify Submitted state warns about potential duplicates

### UI Tests (Manual or Playwright)
**State Consolidation Tests**:
- Verify all states display correctly (no Verified state)
- Verify filtering by state works (no Verified option)
- Verify pagination works

**Enhanced Retry Tests**:
- Verify retry button appears for Failed operations (red, "Opakovat")
- Verify retry button appears for Submitted operations (orange, "Znovu zkusit")
- Verify retry button appears for Pending operations (yellow, "Spustit")
- Verify retry button does not appear for Completed operations
- Verify retry button triggers backend call correctly
- Verify confirmation dialog appears with state-specific message
- Verify operation list refreshes after retry

**Stuck Operation Detection Tests**:
- Create operation and manually set `submittedAt` to 6 minutes ago
  - Verify pulsing warning icon appears
  - Hover over icon, verify tooltip shows stuck message
- Create operation and manually set `createdAt` to 11 minutes ago (Pending)
  - Verify pulsing warning icon appears
  - Hover over icon, verify tooltip shows stuck message
- Verify recent operations do not show warning icon

### Smoke Tests (Staging)
1. Create new stock-up operation (via transport box receiving)
2. Verify it completes successfully (no Verified state shown)
3. Manually create a stuck Submitted operation (e.g., kill backend during submit)
4. Verify Submitted state displays in UI
5. Wait 6 minutes (or manually adjust timestamp in DB)
6. Verify pulsing warning icon appears
7. Click orange "Znovu zkusit" button
8. Confirm retry in dialog (with duplicate warning)
9. Verify operation completes successfully
10. Create operation in Pending state (manually via DB if needed)
11. Click yellow "Spustit" button
12. Verify operation processes and completes

## Risk Assessment

### Low Risk
- **State consolidation**: No functional change, just cleaner model
- **UI retry**: Already implemented and working

### Medium Risk
- **Database migration**: State enum value changes require careful migration
- **Existing Verified records**: Need to be migrated to Completed

### Mitigation
- Test migration thoroughly on copy of production database
- Include rollback steps in deployment plan
- Monitor logs after deployment for any unexpected errors

## Success Criteria

### Technical
- ✅ All tests pass (backend + frontend)
- ✅ No compilation errors
- ✅ Database migration runs successfully
- ✅ OpenAPI client regenerates without errors

### Functional
- ✅ Stock-up operations complete successfully
- ✅ Failed operations can be retried from UI
- ✅ All states display correctly in UI
- ✅ No regressions in existing functionality

### Operational
- ✅ No errors in application logs
- ✅ No user complaints about broken functionality
- ✅ Performance metrics unchanged

## Post-Implementation Tasks

1. Update CLAUDE.md if needed (architecture documentation)
2. Announce change to team (if applicable)
3. Monitor error logs for 24 hours after deployment
4. Archive this change specification document

## Questions & Decisions

### Q: Should we keep `VerifiedAt` in the database for audit purposes?
**A**: No. Since verification and completion happen atomically, `CompletedAt` serves the same audit purpose. Keeping `VerifiedAt` would maintain the confusion we're trying to eliminate.

### Q: What happens to existing operations in `Verified` state?
**A**: Database migration will automatically convert them to `Completed` state. This is safe because `Verified` was always immediately followed by `Completed`.

### Q: Should retry button be available for `Submitted` state?
**A**: Current implementation already handles this via `ForceReset()` in the retry handler. Submitted operations can be retried, but confirmation is recommended. Consider adding retry button for Submitted state as future enhancement.

### Q: Do we need to update API versioning?
**A**: No. This is a non-breaking change from the API perspective - we're removing a state that was never stable. Existing clients filtering by `Verified` will simply get empty results (acceptable).

---

**Prepared by**: Claude Code (AI)
**Reviewed by**: [To be filled by human reviewer]
**Approved by**: [To be filled by project owner]
**Implementation Start Date**: [To be filled]
**Implementation Completion Date**: [To be filled]
