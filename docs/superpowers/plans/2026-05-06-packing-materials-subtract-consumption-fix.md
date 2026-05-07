# PackingMaterials "Odečíst spotřebu" Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix "Odečíst spotřebu" so it actually subtracts quantities when invoices exist, shows a truthful info banner when no invoices are found, and correctly blocks duplicate processing.

**Architecture:** The bug lives in `ConsumptionCalculationService.ProcessDailyConsumptionAsync` — it silently skips all materials when `orderCount == 0` (no invoices) but still returns success. The handler hard-codes `MaterialsProcessed = 0` so the FE has no way to detect the no-op. We change the service return type to carry the real count, write an idempotency marker log even on zero-consumption days, fix the handler to surface the count, and update the modal to show three distinct states.

**Tech Stack:** .NET 8 / C# (xUnit, FluentAssertions), React / TypeScript (React Testing Library, jest)

---

## File Map

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ProcessDailyConsumptionResult.cs` | **Create** — new record replacing `bool` return |
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs` | **Modify** — change `ProcessDailyConsumptionAsync` signature |
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs` | **Modify** — fix logic: count, write marker on zero-consumption |
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs` | **Modify** — use real count, distinguish zero-consumption message |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs` | **Modify** — add 3 new tests for `ProcessDailyConsumptionAsync` |
| `frontend/src/components/packing-materials/modals/ProcessDailyConsumptionModal.tsx` | **Modify** — 3-state banner (green / yellow / red) |
| `frontend/src/components/packing-materials/modals/__tests__/ProcessDailyConsumptionModal.test.tsx` | **Create** — 3 RTL tests for the modal banners |

---

## Task 1: Create result type and update interface (compile step)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ProcessDailyConsumptionResult.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs`

- [ ] **Step 1: Create `ProcessDailyConsumptionResult.cs`**

Create the file at:
`backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ProcessDailyConsumptionResult.cs`

```csharp
namespace Anela.Heblo.Application.Features.PackingMaterials.Services;

/// <param name="WasRun">False when the day was already processed and the run was skipped.</param>
/// <param name="MaterialsProcessed">Number of materials whose quantity was actually decremented.</param>
public sealed record ProcessDailyConsumptionResult(bool WasRun, int MaterialsProcessed);
```

- [ ] **Step 2: Update `IConsumptionCalculationService` signature**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs`, change line 12-16:

Replace:
```csharp
    Task<bool> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        int orderCount,
        int productCount,
        CancellationToken cancellationToken = default);
```

With:
```csharp
    Task<ProcessDailyConsumptionResult> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        int orderCount,
        int productCount,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 3: Update `ConsumptionCalculationService` return type (minimal — keeps existing logic)**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs`:

Change the method signature at line 39 from `Task<bool>` to `Task<ProcessDailyConsumptionResult>`.

Change `return false;` at line 49 to:
```csharp
        return new ProcessDailyConsumptionResult(false, 0);
```

Change `return true;` at line 94 to:
```csharp
        return new ProcessDailyConsumptionResult(true, processedCount);
```

Full method signature after this step (logic inside loop stays identical for now):

```csharp
    public async Task<ProcessDailyConsumptionResult> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        int orderCount,
        int productCount,
        CancellationToken cancellationToken = default)
```

- [ ] **Step 4: Update `ProcessDailyConsumptionHandler` to use new return type**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs`:

Change lines 65-90 so that:
1. `var processed = await _consumptionService...` → `var result = await _consumptionService...`
2. `if (!processed)` → `if (!result.WasRun)`
3. In the success response, replace `MaterialsProcessed = 0, // TODO...` with `MaterialsProcessed = result.MaterialsProcessed`
4. Keep the message as-is for now (we refine it in Task 3)

Result:
```csharp
            var result = await _consumptionService.ProcessDailyConsumptionAsync(
                request.ProcessingDate,
                orderCount,
                productCount,
                cancellationToken);

            if (!result.WasRun)
            {
                return new ProcessDailyConsumptionResponse
                {
                    Success = false,
                    ProcessedDate = request.ProcessingDate,
                    MaterialsProcessed = 0,
                    Message = $"Daily consumption for {request.ProcessingDate} was already processed"
                };
            }

            _logger.LogInformation("Successfully processed daily consumption for {Date}", request.ProcessingDate);

            return new ProcessDailyConsumptionResponse
            {
                Success = true,
                ProcessedDate = request.ProcessingDate,
                MaterialsProcessed = result.MaterialsProcessed,
                Message = $"Daily consumption successfully processed for {request.ProcessingDate}"
            };
```

- [ ] **Step 5: Build and confirm it compiles**

Run from repo root:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded. 0 Error(s)` (existing `ProcessDailyConsumptionAsync` tests still pass since the logic is identical).

- [ ] **Step 6: Run existing tests to confirm nothing broke**

```bash
dotnet test backend/Anela.Heblo.sln --filter "PackingMaterials"
```

Expected: all existing tests pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ProcessDailyConsumptionResult.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/IConsumptionCalculationService.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs
git commit -m "refactor: change ProcessDailyConsumptionAsync return type to carry processed count"
```

---

## Task 2: Write failing BE tests for `ProcessDailyConsumptionAsync`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs`

- [ ] **Step 1: Add three failing tests**

Append the following three `[Fact]` tests to `ConsumptionCalculationServiceTests` class in `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs`:

```csharp
    [Fact]
    public async Task ProcessDailyConsumptionAsync_DecrementsQuantityAndReturnsCount_WhenInvoicesExist()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 15);
        var material1 = new PackingMaterial("Cards", 1m, ConsumptionType.PerOrder, 100m);
        var material2 = new PackingMaterial("Stickers", 2m, ConsumptionType.PerOrder, 50m);
        var mockRepository = new MockPackingMaterialRepository();
        mockRepository.SetMaterials(new[] { material1, material2 });
        var service = new ConsumptionCalculationService(mockRepository, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date, orderCount: 5, productCount: 10);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(2, result.MaterialsProcessed);
        Assert.Equal(95m, mockRepository.UpdatedMaterials[0].CurrentQuantity); // 100 - 1*5
        Assert.Equal(40m, mockRepository.UpdatedMaterials[1].CurrentQuantity); // 50 - 2*5
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_WritesMarkerAndReturnsZero_WhenNoConsumptionCalculated()
    {
        // Arrange — PerOrder material but zero orders means consumption = 0
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Cards", 1m, ConsumptionType.PerOrder, 8000m);
        var mockRepository = new MockPackingMaterialRepository();
        mockRepository.SetMaterials(new[] { material });
        var service = new ConsumptionCalculationService(mockRepository, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date, orderCount: 0, productCount: 0);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(0, result.MaterialsProcessed);

        // Quantity must NOT have changed
        Assert.Single(mockRepository.UpdatedMaterials);
        Assert.Equal(8000m, mockRepository.UpdatedMaterials[0].CurrentQuantity);

        // A marker log must have been written so re-runs are blocked
        var markerLog = mockRepository.UpdatedMaterials[0].Logs.Single();
        Assert.Equal(LogEntryType.AutomaticConsumption, markerLog.LogType);
        Assert.Equal(date, markerLog.Date);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenDayAlreadyProcessed()
    {
        // Arrange — marker already present
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Cards", 1m, ConsumptionType.PerOrder, 8000m);
        var mockRepository = new MockPackingMaterialRepository();
        mockRepository.SetMaterials(new[] { material });
        mockRepository.SetHasDailyProcessingBeenRun(date, true);
        var service = new ConsumptionCalculationService(mockRepository, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date, orderCount: 5, productCount: 10);

        // Assert
        Assert.False(result.WasRun);
        Assert.Equal(0, result.MaterialsProcessed);
        Assert.Empty(mockRepository.UpdatedMaterials);
    }
```

- [ ] **Step 2: Run tests — verify the right ones fail**

```bash
dotnet test backend/Anela.Heblo.sln --filter "ProcessDailyConsumptionAsync"
```

Expected output:
- `ProcessDailyConsumptionAsync_DecrementsQuantityAndReturnsCount_WhenInvoicesExist` → **FAIL** (`MaterialsProcessed` is 0, not 2 — because handler doesn't return count yet from service)
- `ProcessDailyConsumptionAsync_WritesMarkerAndReturnsZero_WhenNoConsumptionCalculated` → **FAIL** (`UpdatedMaterials` is empty — no marker written)
- `ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenDayAlreadyProcessed` → **PASS** (this path already works)

- [ ] **Step 3: Commit failing tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs
git commit -m "test: add failing tests for ProcessDailyConsumptionAsync logic"
```

---

## Task 3: Fix `ConsumptionCalculationService` logic — make tests pass

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs`

- [ ] **Step 1: Implement the fixes**

Replace the entire `ProcessDailyConsumptionAsync` method (lines 39-95) with:

```csharp
    public async Task<ProcessDailyConsumptionResult> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        int orderCount,
        int productCount,
        CancellationToken cancellationToken = default)
    {
        if (await HasDayAlreadyBeenProcessedAsync(processingDate, cancellationToken))
        {
            _logger.LogInformation("Daily consumption processing for {Date} already completed, skipping", processingDate);
            return new ProcessDailyConsumptionResult(false, 0);
        }

        _logger.LogInformation("Starting daily consumption processing for {Date} with {OrderCount} orders and {ProductCount} products",
            processingDate, orderCount, productCount);

        var materials = await _repository.GetAllAsync(cancellationToken);
        var materialList = materials.ToList();
        var processedCount = 0;

        foreach (var material in materialList)
        {
            try
            {
                var consumptionAmount = await CalculateConsumptionAsync(material, orderCount, productCount);

                if (consumptionAmount > 0)
                {
                    var newQuantity = Math.Max(0, material.CurrentQuantity - consumptionAmount);
                    material.UpdateQuantity(newQuantity, processingDate, LogEntryType.AutomaticConsumption);
                    await _repository.UpdateAsync(material, cancellationToken);
                    processedCount++;

                    _logger.LogInformation("Processed material {MaterialName}: consumed {Consumption}, new quantity: {NewQuantity}",
                        material.Name, consumptionAmount, newQuantity);
                }
                else
                {
                    _logger.LogDebug("Skipping material {MaterialName}: no consumption calculated", material.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing material {MaterialId} ({MaterialName}) for date {Date}",
                    material.Id, material.Name, processingDate);
            }
        }

        // Write an idempotency marker even when no materials had consumption,
        // so re-runs on the same date are blocked regardless of invoice data.
        if (processedCount == 0 && materialList.Count > 0)
        {
            var marker = materialList[0];
            marker.UpdateQuantity(marker.CurrentQuantity, processingDate, LogEntryType.AutomaticConsumption);
            await _repository.UpdateAsync(marker, cancellationToken);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Completed daily consumption processing for {Date}. Processed {ProcessedCount} materials",
            processingDate, processedCount);

        return new ProcessDailyConsumptionResult(true, processedCount);
    }
```

- [ ] **Step 2: Run failing tests — verify they now pass**

```bash
dotnet test backend/Anela.Heblo.sln --filter "ProcessDailyConsumptionAsync"
```

Expected: all 3 new tests **PASS**.

- [ ] **Step 3: Run the full test suite to check for regressions**

```bash
dotnet test backend/Anela.Heblo.sln --filter "PackingMaterials"
```

Expected: all existing tests still pass.

- [ ] **Step 4: Also update the handler message for the zero-consumption case**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs`, change the success return block (after `if (!result.WasRun)`) to distinguish zero vs non-zero:

Replace:
```csharp
            return new ProcessDailyConsumptionResponse
            {
                Success = true,
                ProcessedDate = request.ProcessingDate,
                MaterialsProcessed = result.MaterialsProcessed,
                Message = $"Daily consumption successfully processed for {request.ProcessingDate}"
            };
```

With:
```csharp
            var message = result.MaterialsProcessed > 0
                ? $"Daily consumption successfully processed for {request.ProcessingDate}. {result.MaterialsProcessed} materials updated."
                : $"No invoices found for {request.ProcessingDate} — no materials were updated.";

            return new ProcessDailyConsumptionResponse
            {
                Success = true,
                ProcessedDate = request.ProcessingDate,
                MaterialsProcessed = result.MaterialsProcessed,
                Message = message
            };
```

- [ ] **Step 5: Build + full test run**

```bash
dotnet build backend/Anela.Heblo.sln && dotnet test backend/Anela.Heblo.sln --filter "PackingMaterials"
```

Expected: build succeeded, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs
git commit -m "fix: count processed materials and write idempotency marker on zero-consumption days"
```

---

## Task 4: Write failing FE modal tests

**Files:**
- Create: `frontend/src/components/packing-materials/modals/__tests__/ProcessDailyConsumptionModal.test.tsx`

- [ ] **Step 1: Create the test directory and file**

Create `frontend/src/components/packing-materials/modals/__tests__/ProcessDailyConsumptionModal.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import ProcessDailyConsumptionModal from '../ProcessDailyConsumptionModal';

jest.mock('../../../../api/hooks/usePackingMaterials', () => ({
  useProcessDailyConsumption: jest.fn(),
}));

jest.mock('../../../../api/generated/api-client', () => ({
  ProcessDailyConsumptionRequest: jest.fn().mockImplementation((data: unknown) => data),
}));

const { useProcessDailyConsumption } = require('../../../../api/hooks/usePackingMaterials');

function setupMutationMock(resolveValue: {
  success: boolean;
  materialsProcessed?: number;
  message?: string;
}) {
  useProcessDailyConsumption.mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue(resolveValue),
    isPending: false,
    error: null,
  });
}

const defaultProps = {
  isOpen: true,
  onClose: jest.fn(),
  onSuccess: jest.fn(),
};

describe('ProcessDailyConsumptionModal', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('shows green success banner when materials were updated', async () => {
    setupMutationMock({ success: true, materialsProcessed: 3 });

    render(<ProcessDailyConsumptionModal {...defaultProps} />);
    fireEvent.submit(screen.getByRole('button', { name: /odečíst spotřebu/i }));

    await waitFor(() => {
      expect(screen.getByText(/odečteno pro 3 materiálů/i)).toBeInTheDocument();
    });

    expect(screen.queryByText(/nebyly nalezeny žádné faktury/i)).not.toBeInTheDocument();
  });

  it('shows yellow info banner when no invoices were found (zero materials processed)', async () => {
    setupMutationMock({ success: true, materialsProcessed: 0 });

    render(<ProcessDailyConsumptionModal {...defaultProps} />);
    fireEvent.submit(screen.getByRole('button', { name: /odečíst spotřebu/i }));

    await waitFor(() => {
      expect(screen.getByText(/nebyly nalezeny žádné faktury/i)).toBeInTheDocument();
    });

    expect(screen.queryByText(/odečteno pro/i)).not.toBeInTheDocument();
  });

  it('shows red error banner when mutation throws', async () => {
    useProcessDailyConsumption.mockReturnValue({
      mutateAsync: jest.fn().mockRejectedValue(new Error('Server error')),
      isPending: false,
      error: new Error('Server error'),
    });

    render(<ProcessDailyConsumptionModal {...defaultProps} />);
    fireEvent.submit(screen.getByRole('button', { name: /odečíst spotřebu/i }));

    await waitFor(() => {
      expect(screen.getByText(/server error/i)).toBeInTheDocument();
    });
  });
});
```

- [ ] **Step 2: Run tests — verify they fail for the right reason**

```bash
cd frontend && npm test -- --testPathPattern="ProcessDailyConsumptionModal" --watchAll=false
```

Expected:
- `shows green success banner…` → **FAIL** (modal shows generic "Spotřeba byla úspěšně odečtena" not "Odečteno pro 3 materiálů")
- `shows yellow info banner…` → **FAIL** (modal still shows green banner for `success=true`)
- `shows red error banner…` → **PASS** (already works via `mutation.error`)

- [ ] **Step 3: Commit failing tests**

```bash
git add frontend/src/components/packing-materials/modals/__tests__/ProcessDailyConsumptionModal.test.tsx
git commit -m "test: add failing tests for ProcessDailyConsumptionModal banners"
```

---

## Task 5: Fix FE modal — make tests pass

**Files:**
- Modify: `frontend/src/components/packing-materials/modals/ProcessDailyConsumptionModal.tsx`

- [ ] **Step 1: Add `infoMessage` state and update banner logic**

In `ProcessDailyConsumptionModal.tsx`:

After line 24 (`const [successMessage, setSuccessMessage] = useState<string | null>(null);`), add:
```tsx
  const [infoMessage, setInfoMessage] = useState<string | null>(null);
```

In `handleSubmit` at line 29 (after `setSuccessMessage(null)`), also reset:
```tsx
    setInfoMessage(null);
```

Replace lines 37-43 (the `if (result.success)` block):
```tsx
      if (result.success) {
        if ((result.materialsProcessed ?? 0) > 0) {
          setSuccessMessage(`Odečteno pro ${result.materialsProcessed} materiálů`);
        } else {
          setInfoMessage('Pro tento den nebyly nalezeny žádné faktury — nic nebylo odečteno');
        }
        setTimeout(() => {
          onSuccess?.();
          onClose();
        }, 2000);
      }
```

In the JSX, after the existing error block and before the success block (or after it), add the yellow info banner. Insert after the success message block (around line 109):

```tsx
          {/* Info Message — zero consumption */}
          {infoMessage && (
            <div className="bg-yellow-50 border border-yellow-200 text-yellow-700 px-4 py-3 rounded">
              {infoMessage}
            </div>
          )}
```

- [ ] **Step 2: Run modal tests — verify they all pass**

```bash
cd frontend && npm test -- --testPathPattern="ProcessDailyConsumptionModal" --watchAll=false
```

Expected: all 3 tests **PASS**.

- [ ] **Step 3: Run the FE build and lint**

```bash
cd frontend && npm run build && npm run lint
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/packing-materials/modals/ProcessDailyConsumptionModal.tsx
git commit -m "fix: show truthful banner distinguishing updated vs no-invoice result"
```

---

## Verification

### Automated

```bash
# Backend — build, format check, all packing materials tests
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test backend/Anela.Heblo.sln --filter "PackingMaterials"

# Frontend — build, lint, modal tests
cd frontend && npm run build && npm run lint
npm test -- --testPathPattern="ProcessDailyConsumptionModal" --watchAll=false
```

### Manual smoke test (requires running app + DB)

1. **Day with invoices**: Pick a date where `IssuedInvoices` has rows.
   - Open Sledování materiálů → click "Odečíst spotřebu" → select that date → submit.
   - Expected: green banner "Odečteno pro N materiálů", list refetches, Děkovací kartičky quantity drops by `ConsumptionRate × invoiceCount`.

2. **Day without invoices**: Pick a date with no `IssuedInvoices` rows (or yesterday if syncing hasn't run).
   - Submit.
   - Expected: **yellow** banner "Pro tento den nebyly nalezeny žádné faktury — nic nebylo odečteno", quantity unchanged.

3. **Re-run same date**: Submit the same date again immediately.
   - Expected: red error "Daily consumption … was already processed".

4. **DB sanity check**:
```sql
SELECT "Date", "OldQuantity", "NewQuantity", "LogType"
FROM public."PackingMaterialLogs"
WHERE "Date" = '2025-06-15'
ORDER BY "CreatedAt";
```
   - For a date where subtraction happened: rows with `OldQuantity != NewQuantity`, `LogType = 1` (AutomaticConsumption).
   - For a zero-invoice date: one row where `OldQuantity == NewQuantity`, `LogType = 1` — the idempotency marker.
