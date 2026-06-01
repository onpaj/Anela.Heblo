# Stock-Up Process: Implementation Plan

## Document Information

- **Related Change Spec**: `/docs/features/stock-up-state-consolidation-change-spec.md`
- **Version**: 1.0
- **Date**: 2026-01-20
- **Status**: Ready for Implementation

## Overview

This implementation plan provides step-by-step instructions for implementing:
1. State consolidation (remove `Verified` state)
2. Enhanced retry functionality (support all retryable states)
3. Stuck operation detection (visual warnings for old operations)

Each task includes file paths, exact code changes, and verification steps.

---

## Phase 1: Backend Changes

### Task 1.1: Update StockUpOperationState Enum

**File**: `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperationState.cs`

**Action**: Remove `Verified` state and renumber remaining states

**Changes**:
```csharp
// OLD:
public enum StockUpOperationState
{
    Pending = 0,
    Submitted = 1,
    Verified = 2,      // <-- REMOVE
    Completed = 3,     // <-- Change to 2
    Failed = 4         // <-- Change to 3
}

// NEW:
public enum StockUpOperationState
{
    Pending = 0,
    Submitted = 1,
    Completed = 2,
    Failed = 3
}
```

**Verification**:
- [ ] Enum compiles without errors
- [ ] Only 4 states remain: Pending, Submitted, Completed, Failed

---

### Task 1.2: Update StockUpOperation Domain Entity

**File**: `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs`

**Action 1**: Remove `VerifiedAt` property
```csharp
// REMOVE this line:
public DateTime? VerifiedAt { get; private set; }
```

**Action 2**: Remove `MarkAsVerified()` method
```csharp
// REMOVE this entire method:
public void MarkAsVerified(DateTime timestamp)
{
    if (State != StockUpOperationState.Submitted)
        throw new InvalidOperationException($"Cannot mark as Verified from {State} state");

    State = StockUpOperationState.Verified;
    VerifiedAt = timestamp;
}
```

**Action 3**: Update `MarkAsCompleted()` method
```csharp
// OLD:
public void MarkAsCompleted(DateTime timestamp)
{
    if (State != StockUpOperationState.Verified && State != StockUpOperationState.Pending)
        throw new InvalidOperationException($"Cannot mark as Completed from {State} state");

    State = StockUpOperationState.Completed;
    CompletedAt = timestamp;
}

// NEW:
public void MarkAsCompleted(DateTime timestamp)
{
    // Can transition from Submitted (after verification) or Pending (if already in Shoptet)
    if (State != StockUpOperationState.Submitted && State != StockUpOperationState.Pending)
        throw new InvalidOperationException($"Cannot mark as Completed from {State} state");

    State = StockUpOperationState.Completed;
    CompletedAt = timestamp;
}
```

**Action 4**: Update `Reset()` method
```csharp
// OLD:
public void Reset()
{
    if (State != StockUpOperationState.Failed)
        throw new InvalidOperationException($"Can only reset Failed operations, current state: {State}");

    State = StockUpOperationState.Pending;
    SubmittedAt = null;
    VerifiedAt = null;  // <-- REMOVE this line
    CompletedAt = null;
    ErrorMessage = null;
}

// NEW:
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

**Action 5**: Update `ForceReset()` method
```csharp
// OLD:
public void ForceReset()
{
    if (State == StockUpOperationState.Completed)
        throw new InvalidOperationException("Cannot force reset Completed operations");

    State = StockUpOperationState.Pending;
    SubmittedAt = null;
    VerifiedAt = null;  // <-- REMOVE this line
    CompletedAt = null;
    ErrorMessage = null;
}

// NEW:
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

**Verification**:
- [ ] No references to `VerifiedAt` property remain
- [ ] No `MarkAsVerified()` method exists
- [ ] `MarkAsCompleted()` accepts transitions from `Submitted` or `Pending`
- [ ] `Reset()` and `ForceReset()` do not set `VerifiedAt = null`
- [ ] Code compiles without errors

---

### Task 1.3: Update StockUpOrchestrationService

**File**: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpOrchestrationService.cs`

**Action**: Remove `MarkAsVerified()` call from verification logic

**Location**: Lines 121-151 (Post-verify section)

**Changes**:
```csharp
// OLD:
try
{
    var verified = await _eshopService.VerifyStockUpExistsAsync(documentNumber);
    if (verified)
    {
        operation.MarkAsVerified(DateTime.UtcNow);  // <-- REMOVE this line
        operation.MarkAsCompleted(DateTime.UtcNow);
        await _repository.SaveChangesAsync(ct);
        _logger.LogInformation("Operation {DocumentNumber} verified and completed successfully", documentNumber);
        return StockUpOperationResult.Success(operation);
    }
    // ... rest of method
}

// NEW:
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
    // ... rest of method
}
```

**Verification**:
- [ ] `MarkAsVerified()` call removed
- [ ] `MarkAsCompleted()` is called directly after successful verification
- [ ] Code compiles without errors

---

### Task 1.4: Update StockUpOperationDto

**File**: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperations/GetStockUpOperationsResponse.cs`

**Action**: Remove `VerifiedAt` property from DTO

**Changes**:
```csharp
// OLD:
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
    public DateTime? VerifiedAt { get; set; }    // <-- REMOVE this line
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

// NEW:
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

**Verification**:
- [ ] `VerifiedAt` property removed
- [ ] DTO compiles without errors

---

### Task 1.5: Update Backend Tests

**File**: `backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockUpOperationTests.cs`

**Action 1**: Remove tests for `MarkAsVerified()` method
- Remove any test methods with "MarkAsVerified" in the name
- Remove any test methods specifically testing `VerifiedAt` timestamp

**Action 2**: Update `MarkAsCompleted()` tests to use `Submitted` instead of `Verified`

**Example**:
```csharp
// OLD:
[Fact]
public void MarkAsCompleted_FromVerified_ShouldSucceed()
{
    var operation = CreateOperation();
    operation.MarkAsSubmitted(DateTime.UtcNow);
    operation.MarkAsVerified(DateTime.UtcNow);  // <-- REMOVE

    var timestamp = DateTime.UtcNow;
    operation.MarkAsCompleted(timestamp);

    operation.State.Should().Be(StockUpOperationState.Completed);
    operation.CompletedAt.Should().Be(timestamp);
}

// NEW:
[Fact]
public void MarkAsCompleted_FromSubmitted_ShouldSucceed()
{
    var operation = CreateOperation();
    operation.MarkAsSubmitted(DateTime.UtcNow);

    var timestamp = DateTime.UtcNow;
    operation.MarkAsCompleted(timestamp);

    operation.State.Should().Be(StockUpOperationState.Completed);
    operation.CompletedAt.Should().Be(timestamp);
}
```

**Action 3**: Remove `VerifiedAt` assertions from all tests

**Verification**:
- [ ] No tests reference `MarkAsVerified()` method
- [ ] No tests reference `VerifiedAt` property
- [ ] All tests pass: `dotnet test`

---

### Task 1.6: Create Database Migration

**Command**:
```bash
cd backend/src/Anela.Heblo.Persistence
dotnet ef migrations add RemoveVerifiedStateFromStockUpOperations --startup-project ../Anela.Heblo.API
```

**Action**: Manually edit the generated migration file to include state value migration

**Migration Up** (add this to generated `Up` method):
```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Step 1: Update existing Verified records (state = 2) to old Completed value (state = 3)
    migrationBuilder.Sql(@"
        UPDATE ""StockUpOperations""
        SET ""State"" = 3
        WHERE ""State"" = 2;
    ");

    // Step 2: Update state values to new enum (Completed = 2, Failed = 3)
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

**Migration Down** (add this to generated `Down` method):
```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    // Step 1: Add VerifiedAt column back
    migrationBuilder.AddColumn<DateTime>(
        name: "VerifiedAt",
        table: "StockUpOperations",
        type: "timestamp with time zone",
        nullable: true);

    // Step 2: Restore old state values
    migrationBuilder.Sql(@"
        UPDATE ""StockUpOperations""
        SET ""State"" = 4
        WHERE ""State"" = 3;

        UPDATE ""StockUpOperations""
        SET ""State"" = 3
        WHERE ""State"" = 2;
    ");
}
```

**Run Migration**:
```bash
dotnet ef database update --startup-project ../Anela.Heblo.API
```

**Verification**:
- [ ] Migration file created successfully
- [ ] Migration SQL includes state value updates
- [ ] Migration runs without errors
- [ ] Database schema updated: `VerifiedAt` column removed
- [ ] Existing data migrated: all Verified records now Completed

---

### Task 1.7: Rebuild Backend and Regenerate API Client

**Commands**:
```bash
cd backend/src/Anela.Heblo.API
dotnet build
```

**This triggers**:
- Backend compilation
- PostBuild event that regenerates OpenAPI client
- Frontend TypeScript client generation

**Verification**:
- [ ] Backend builds without errors
- [ ] OpenAPI spec generated at `backend/src/Anela.Heblo.API/swagger.json`
- [ ] Frontend client generated at `frontend/src/api/generated/api-client.ts`
- [ ] TypeScript client no longer contains `Verified` in `StockUpOperationState` enum

---

## Phase 2: Frontend Changes

### Task 2.1: Install Dependencies

**Commands**:
```bash
cd frontend
npm install date-fns
```

**Verification**:
- [ ] `date-fns` appears in `package.json` dependencies
- [ ] `node_modules/date-fns` directory exists

---

### Task 2.2: Update Imports in StockOperationsPage

**File**: `frontend/src/pages/StockOperationsPage.tsx`

**Action**: Add new imports at the top of the file

**Find existing imports**:
```typescript
import React, { useState } from 'react';
import { AlertCircle, RefreshCw, CheckCircle, Clock, XCircle } from 'lucide-react';
```

**Update to**:
```typescript
import React, { useState } from 'react';
import { AlertCircle, RefreshCw, CheckCircle, Clock, XCircle, AlertTriangle, Play } from 'lucide-react';
import { differenceInMinutes } from 'date-fns';
```

**Verification**:
- [ ] `AlertTriangle` and `Play` imported from lucide-react
- [ ] `differenceInMinutes` imported from date-fns
- [ ] No import errors

---

### Task 2.3: Remove Verified State from getStateColor()

**File**: `frontend/src/pages/StockOperationsPage.tsx`

**Location**: Around lines 20-35

**Action**: Remove `Verified` case from switch statement

**Find**:
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
    case StockUpOperationState.Verified:  // <-- REMOVE this case
      return 'bg-indigo-100 text-indigo-800';
    default:
      return 'bg-gray-100 text-gray-800';
  }
};
```

**Replace with**:
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

**Verification**:
- [ ] `Verified` case removed
- [ ] No TypeScript errors

---

### Task 2.4: Remove Verified State from getStateIcon()

**File**: `frontend/src/pages/StockOperationsPage.tsx`

**Location**: Around lines 37-51

**Action**: Remove `Verified` case from switch statement

**Find**:
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
    case StockUpOperationState.Verified:  // <-- REMOVE this case
      return <RefreshCw className="h-4 w-4" />;
    default:
      return null;
  }
};
```

**Replace with**:
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

**Verification**:
- [ ] `Verified` case removed
- [ ] No TypeScript errors

---

### Task 2.5: Remove Verified from Filter Dropdown

**File**: `frontend/src/pages/StockOperationsPage.tsx`

**Location**: Around lines 130-136

**Action**: Remove Verified option from select dropdown

**Find**:
```tsx
<select>
  <option value="">Všechny</option>
  <option value={StockUpOperationState.Failed}>Failed</option>
  <option value={StockUpOperationState.Pending}>Pending</option>
  <option value={StockUpOperationState.Submitted}>Submitted</option>
  <option value={StockUpOperationState.Verified}>Verified</option>  {/* <-- REMOVE */}
  <option value={StockUpOperationState.Completed}>Completed</option>
</select>
```

**Replace with**:
```tsx
<select>
  <option value="">Všechny</option>
  <option value={StockUpOperationState.Failed}>Failed</option>
  <option value={StockUpOperationState.Pending}>Pending</option>
  <option value={StockUpOperationState.Submitted}>Submitted</option>
  <option value={StockUpOperationState.Completed}>Completed</option>
</select>
```

**Verification**:
- [ ] Verified option removed from dropdown
- [ ] Dropdown still renders correctly

---

### Task 2.6: Add Stuck Operation Detection Helpers

**File**: `frontend/src/pages/StockOperationsPage.tsx`

**Location**: After state declarations (after line ~10), before `useStockUpOperationsQuery` hook

**Action**: Insert new helper functions

**Add**:
```typescript
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

**Verification**:
- [ ] Functions added before any hooks
- [ ] No TypeScript errors
- [ ] `differenceInMinutes` import resolves correctly

---

### Task 2.7: Add Enhanced Retry Helper Functions

**File**: `frontend/src/pages/StockOperationsPage.tsx`

**Location**: After `handleRetry` function (around line ~61)

**Action**: Add new retry helper functions

**Add**:
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

**Verification**:
- [ ] All helper functions added
- [ ] No TypeScript errors
- [ ] Icons (`AlertTriangle`, `Play`) resolve correctly

---

### Task 2.8: Update Retry Button Rendering

**File**: `frontend/src/pages/StockOperationsPage.tsx`

**Location**: Around lines 208-219 (in table body, "Akce" column)

**Action**: Replace retry button logic to support all states

**Find**:
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

**Replace with**:
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

**Verification**:
- [ ] Retry button now uses `canRetry()` check
- [ ] Button calls `handleRetryWithConfirmation()` instead of `handleRetry()`
- [ ] Button color, label, and icon are dynamic based on state
- [ ] Tooltip added with state-specific message
- [ ] No TypeScript errors

---

### Task 2.9: Add Stuck Operation Visual Indicator

**File**: `frontend/src/pages/StockOperationsPage.tsx`

**Location**: Around lines 196-201 (in table body, "Stav" column)

**Action**: Add pulsing warning icon for stuck operations

**Find**:
```tsx
<td className="px-6 py-4 whitespace-nowrap">
  <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStateColor(operation.state ?? StockUpOperationState.Pending)}`}>
    {getStateIcon(operation.state ?? StockUpOperationState.Pending)}
    <span className="ml-1">{StockUpOperationState[operation.state ?? StockUpOperationState.Pending]}</span>
  </span>
</td>
```

**Replace with**:
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

**Verification**:
- [ ] State badge wrapped in flex container
- [ ] Stuck operations show pulsing warning triangle
- [ ] Tooltip shows stuck message on hover
- [ ] No TypeScript errors

---

### Task 2.10: Build and Test Frontend

**Commands**:
```bash
cd frontend
npm run build
npm test
```

**Verification**:
- [ ] Frontend builds without errors
- [ ] All existing tests pass
- [ ] No console errors when running dev server (`npm start`)

---

## Phase 3: Testing & Validation

### Task 3.1: Manual Testing - State Consolidation

**Start application**:
```bash
# Terminal 1 - Backend
cd backend/src/Anela.Heblo.API
dotnet run

# Terminal 2 - Frontend
cd frontend
npm start
```

**Test cases**:
- [ ] Navigate to Stock Operations page (`http://localhost:3000/stock-operations`)
- [ ] Verify state filter dropdown does NOT contain "Verified" option
- [ ] Filter by each state (Pending, Submitted, Completed, Failed)
- [ ] Verify operations display with correct state badges
- [ ] Verify no "Verified" state appears anywhere in UI

---

### Task 3.2: Manual Testing - Enhanced Retry

**Test Failed operations**:
- [ ] Find operation with state = Failed
- [ ] Verify retry button is RED with label "Opakovat"
- [ ] Click retry button
- [ ] Verify confirmation dialog says "Opravdu chcete znovu spustit tuto selhanou operaci?"
- [ ] Confirm and verify operation retries

**Test Submitted operations (create manually if needed)**:
- [ ] Find or create operation with state = Submitted
- [ ] Verify retry button is ORANGE with label "Znovu zkusit"
- [ ] Click retry button
- [ ] Verify confirmation dialog warns about potential duplicates
- [ ] Confirm and verify operation retries

**Test Pending operations (create manually if needed)**:
- [ ] Find or create operation with state = Pending
- [ ] Verify retry button is YELLOW with label "Spustit"
- [ ] Click retry button
- [ ] Verify confirmation dialog says "Tato operace nebyla nikdy zpracována. Chcete ji spustit?"
- [ ] Confirm and verify operation processes

**Test Completed operations**:
- [ ] Find operation with state = Completed
- [ ] Verify NO retry button appears

---

### Task 3.3: Manual Testing - Stuck Operation Detection

**Test Submitted stuck detection**:
- [ ] Manually create operation with `submittedAt` = 6 minutes ago (via DB)
- [ ] Refresh Stock Operations page
- [ ] Verify pulsing red warning triangle appears next to state badge
- [ ] Hover over warning icon
- [ ] Verify tooltip says "Operace je ve stavu Submitted X minut. Může být uvízlá."

**Test Pending stuck detection**:
- [ ] Manually create operation with `createdAt` = 11 minutes ago (via DB)
- [ ] Refresh Stock Operations page
- [ ] Verify pulsing red warning triangle appears
- [ ] Hover over warning icon
- [ ] Verify tooltip says "Operace je ve stavu Pending X minut. Nebyla zpracována."

**Test recent operations (not stuck)**:
- [ ] Create new operation (state = Pending or Submitted)
- [ ] Verify NO warning icon appears

---

### Task 3.4: Run All Tests

**Backend tests**:
```bash
cd backend
dotnet test
```

**Frontend tests**:
```bash
cd frontend
npm test
```

**Verification**:
- [ ] All backend tests pass (0 failures)
- [ ] All frontend tests pass (0 failures)
- [ ] No test timeouts or errors

---

### Task 3.5: Staging Deployment Smoke Test

**Prerequisites**:
- [ ] All code changes committed to Git
- [ ] PR created and approved
- [ ] Merged to main branch
- [ ] Deployed to staging environment

**Smoke tests on staging**:
- [ ] Navigate to staging Stock Operations page
- [ ] Verify page loads without errors
- [ ] Create new stock-up operation (via transport box receiving)
- [ ] Verify operation completes with state = Completed (NOT Verified)
- [ ] Manually fail an operation (e.g., disconnect network)
- [ ] Verify operation shows in Failed state with red retry button
- [ ] Click retry, confirm, verify operation completes
- [ ] Check database: verify no `VerifiedAt` column exists
- [ ] Check application logs: no errors related to stock-up operations

---

## Rollback Procedure

If issues are discovered in production:

### Option 1: Immediate Rollback (Before Database Migration)
```bash
git revert <commit-hash>
git push origin main
# Redeploy previous version
```

### Option 2: Post-Migration Rollback
```bash
# Revert code
git revert <commit-hash>
git push origin main

# Roll back database migration
cd backend/src/Anela.Heblo.Persistence
dotnet ef database update <previous-migration-name> --startup-project ../Anela.Heblo.API

# Redeploy
```

---

## Success Criteria

**Technical**:
- [ ] All backend tests pass
- [ ] All frontend tests pass
- [ ] No compilation errors
- [ ] Database migration runs successfully
- [ ] OpenAPI client regenerates without errors

**Functional**:
- [ ] Stock-up operations complete successfully
- [ ] Failed operations can be retried from UI
- [ ] Submitted operations can be retried from UI
- [ ] Pending operations can be retried from UI
- [ ] Stuck operations show visual warning
- [ ] All confirmation dialogs show correct messages
- [ ] No Verified state appears in UI
- [ ] No regressions in existing functionality

**Operational**:
- [ ] No errors in application logs
- [ ] Performance metrics unchanged
- [ ] No user complaints

---

## Post-Implementation Checklist

- [ ] Update CLAUDE.md if architecture changed
- [ ] Close related GitHub issues
- [ ] Notify team of changes (if applicable)
- [ ] Monitor error logs for 24 hours
- [ ] Archive change specification document
- [ ] Update runbook documentation for stuck operations

---

**Implementation Status**: [To be filled during implementation]

**Completed By**: [To be filled]

**Completion Date**: [To be filled]

**Notes**: [Any implementation notes or deviations from plan]
