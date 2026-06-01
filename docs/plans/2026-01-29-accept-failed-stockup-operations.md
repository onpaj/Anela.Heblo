# Accept Failed Stock-Up Operations Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add "Accept" functionality for Failed stock-up operations that changes state to Completed, removing them from active operations log.

**Architecture:** Follow existing retry pattern - add domain method `AcceptFailure()`, create MediatR handler `AcceptStockUpOperationHandler`, add controller endpoint, create frontend mutation hook, add UI button in operations table.

**Tech Stack:** .NET 8, MediatR, Entity Framework Core, React, TypeScript, React Query

---

## Task 1: Add Domain Method for Accepting Failed Operations

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs:88-102`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/StockUpOperationTests.cs` (create if not exists)

**Step 1: Create unit test file structure**

Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/StockUpOperationTests.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class StockUpOperationTests
{
    [Fact]
    public void AcceptFailure_WhenOperationIsFailed_TransitionsToCompleted()
    {
        // Arrange
        var operation = new StockUpOperation(
            documentNumber: "DOC-001",
            productCode: "PROD-001",
            amount: 10,
            sourceType: StockUpSourceType.TransportBox,
            sourceId: 1);

        operation.MarkAsFailed(DateTime.UtcNow, "Test error");
        var acceptedAt = DateTime.UtcNow;

        // Act
        operation.AcceptFailure(acceptedAt);

        // Assert
        Assert.Equal(StockUpOperationState.Completed, operation.State);
        Assert.Equal(acceptedAt, operation.CompletedAt);
        Assert.Contains("Test error", operation.ErrorMessage); // Original error preserved
        Assert.Contains("Manually accepted", operation.ErrorMessage); // Acceptance note added
    }

    [Fact]
    public void AcceptFailure_WhenOperationIsNotFailed_ThrowsInvalidOperationException()
    {
        // Arrange
        var operation = new StockUpOperation(
            documentNumber: "DOC-001",
            productCode: "PROD-001",
            amount: 10,
            sourceType: StockUpSourceType.TransportBox,
            sourceId: 1);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => operation.AcceptFailure(DateTime.UtcNow));

        Assert.Contains("Can only accept Failed operations", exception.Message);
    }
}
```

**Step 2: Run test to verify it fails**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~StockUpOperationTests" --verbosity normal
```

Expected: FAIL with "Method 'AcceptFailure' not found"

**Step 3: Implement AcceptFailure method**

Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs`

Add after the `ForceReset()` method (after line 102):

```csharp
    /// <summary>
    /// Accept a failed operation by marking it as completed.
    /// This allows hiding failed operations from active logs while preserving audit trail.
    /// Original error message is retained with acceptance note appended.
    /// </summary>
    public void AcceptFailure(DateTime timestamp)
    {
        if (State != StockUpOperationState.Failed)
            throw new InvalidOperationException($"Can only accept Failed operations, current state: {State}");

        State = StockUpOperationState.Completed;
        CompletedAt = timestamp;

        // Preserve audit trail by appending acceptance note to original error
        ErrorMessage = $"{ErrorMessage} | Manually accepted at {timestamp:yyyy-MM-dd HH:mm:ss} UTC";
    }
```

**Step 4: Run test to verify it passes**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~StockUpOperationTests" --verbosity normal
```

Expected: PASS (2 tests)

**Step 5: Format and commit**

```bash
cd backend
dotnet format
git add src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs
git add test/Anela.Heblo.Tests/Features/Catalog/StockUpOperationTests.cs
git commit -m "feat: add AcceptFailure method to StockUpOperation domain entity

- Add AcceptFailure() method that transitions Failed → Completed
- Preserve original error message with acceptance timestamp in audit trail
- Add unit tests for success and validation scenarios"
```

---

## Task 2: Create MediatR Request and Response DTOs

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/AcceptStockUpOperation/AcceptStockUpOperationRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/AcceptStockUpOperation/AcceptStockUpOperationResponse.cs`

**Step 1: Create request DTO**

Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/AcceptStockUpOperation/AcceptStockUpOperationRequest.cs`

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.AcceptStockUpOperation;

public class AcceptStockUpOperationRequest : IRequest<AcceptStockUpOperationResponse>
{
    public int OperationId { get; set; }
}
```

**Step 2: Create response DTO**

Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/AcceptStockUpOperation/AcceptStockUpOperationResponse.cs`

```csharp
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.AcceptStockUpOperation;

public class AcceptStockUpOperationResponse : BaseResponse
{
    public StockUpResultStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
}
```

**Step 3: Verify compilation**

Run:
```bash
cd backend
dotnet build
```

Expected: SUCCESS

**Step 4: Commit**

```bash
git add src/Anela.Heblo.Application/Features/Catalog/UseCases/AcceptStockUpOperation/
git commit -m "feat: add AcceptStockUpOperation request/response DTOs"
```

---

## Task 3: Implement MediatR Handler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/AcceptStockUpOperation/AcceptStockUpOperationHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/AcceptStockUpOperationHandlerTests.cs`

**Step 1: Create handler test**

Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/AcceptStockUpOperationHandlerTests.cs`

```csharp
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Catalog.UseCases.AcceptStockUpOperation;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class AcceptStockUpOperationHandlerTests
{
    [Fact]
    public async Task Handle_WhenOperationIsFailed_AcceptsAndReturnsSuccess()
    {
        // Arrange
        var mockRepo = new Mock<IStockUpOperationRepository>();
        var mockLogger = new Mock<ILogger<AcceptStockUpOperationHandler>>();

        var operation = new StockUpOperation(
            documentNumber: "DOC-001",
            productCode: "PROD-001",
            amount: 10,
            sourceType: StockUpSourceType.TransportBox,
            sourceId: 1);
        operation.MarkAsFailed(DateTime.UtcNow, "Original error");

        mockRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var handler = new AcceptStockUpOperationHandler(mockRepo.Object, mockLogger.Object);
        var request = new AcceptStockUpOperationRequest { OperationId = 1 };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(StockUpResultStatus.Success, response.Status);
        Assert.Null(response.ErrorMessage);
        Assert.Equal(StockUpOperationState.Completed, operation.State);
        mockRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenOperationNotFound_ReturnsFailure()
    {
        // Arrange
        var mockRepo = new Mock<IStockUpOperationRepository>();
        var mockLogger = new Mock<ILogger<AcceptStockUpOperationHandler>>();

        mockRepo.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockUpOperation?)null);

        var handler = new AcceptStockUpOperationHandler(mockRepo.Object, mockLogger.Object);
        var request = new AcceptStockUpOperationRequest { OperationId = 999 };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(StockUpResultStatus.Failed, response.Status);
        Assert.Contains("not found", response.ErrorMessage);
        mockRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOperationNotFailed_ReturnsFailure()
    {
        // Arrange
        var mockRepo = new Mock<IStockUpOperationRepository>();
        var mockLogger = new Mock<ILogger<AcceptStockUpOperationHandler>>();

        var operation = new StockUpOperation(
            documentNumber: "DOC-001",
            productCode: "PROD-001",
            amount: 10,
            sourceType: StockUpSourceType.TransportBox,
            sourceId: 1);
        // Operation is Pending, not Failed

        mockRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        var handler = new AcceptStockUpOperationHandler(mockRepo.Object, mockLogger.Object);
        var request = new AcceptStockUpOperationRequest { OperationId = 1 };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(StockUpResultStatus.Failed, response.Status);
        Assert.Contains("not in Failed state", response.ErrorMessage);
        mockRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

**Step 2: Run test to verify it fails**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~AcceptStockUpOperationHandlerTests" --verbosity normal
```

Expected: FAIL with "Type 'AcceptStockUpOperationHandler' not found"

**Step 3: Implement handler**

Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/AcceptStockUpOperation/AcceptStockUpOperationHandler.cs`

```csharp
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.AcceptStockUpOperation;

public class AcceptStockUpOperationHandler : IRequestHandler<AcceptStockUpOperationRequest, AcceptStockUpOperationResponse>
{
    private readonly IStockUpOperationRepository _repository;
    private readonly ILogger<AcceptStockUpOperationHandler> _logger;

    public AcceptStockUpOperationHandler(
        IStockUpOperationRepository repository,
        ILogger<AcceptStockUpOperationHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<AcceptStockUpOperationResponse> Handle(
        AcceptStockUpOperationRequest request,
        CancellationToken cancellationToken)
    {
        var operation = await _repository.GetByIdAsync(request.OperationId, cancellationToken);

        if (operation == null)
        {
            return new AcceptStockUpOperationResponse
            {
                Success = false,
                Status = StockUpResultStatus.Failed,
                ErrorMessage = $"Operation with ID {request.OperationId} not found"
            };
        }

        if (operation.State != StockUpOperationState.Failed)
        {
            return new AcceptStockUpOperationResponse
            {
                Success = false,
                Status = StockUpResultStatus.Failed,
                ErrorMessage = $"Operation {operation.DocumentNumber} is not in Failed state (current: {operation.State})"
            };
        }

        _logger.LogInformation(
            "Accepting failed operation {OperationId} - {DocumentNumber}. Original error: {ErrorMessage}",
            operation.Id,
            operation.DocumentNumber,
            operation.ErrorMessage);

        operation.AcceptFailure(DateTime.UtcNow);

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Failed operation {OperationId} accepted and marked as Completed",
            operation.Id);

        return new AcceptStockUpOperationResponse
        {
            Success = true,
            Status = StockUpResultStatus.Success,
            ErrorMessage = null
        };
    }
}
```

**Step 4: Run test to verify it passes**

Run:
```bash
cd backend
dotnet test --filter "FullyQualifiedName~AcceptStockUpOperationHandlerTests" --verbosity normal
```

Expected: PASS (3 tests)

**Step 5: Format and commit**

```bash
cd backend
dotnet format
git add src/Anela.Heblo.Application/Features/Catalog/UseCases/AcceptStockUpOperation/
git add test/Anela.Heblo.Tests/Features/Catalog/AcceptStockUpOperationHandlerTests.cs
git commit -m "feat: add AcceptStockUpOperation MediatR handler

- Implement handler with validation (operation exists, is Failed)
- Add logging for accept actions
- Add comprehensive unit tests covering success and failure scenarios"
```

---

## Task 4: Add Controller Endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs:86-110`

**Step 1: Add accept endpoint to controller**

Modify: `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs`

Add after the `RetryOperation` method (after line 86):

```csharp
    /// <summary>
    /// Accept a failed stock-up operation, marking it as completed to remove from active logs
    /// </summary>
    /// <param name="id">Operation ID to accept</param>
    [HttpPost("{id}/accept")]
    public async Task<ActionResult<AcceptStockUpOperationResponse>> AcceptOperation(int id)
    {
        var request = new AcceptStockUpOperationRequest { OperationId = id };
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
```

Add using statement at top:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.AcceptStockUpOperation;
```

**Step 2: Verify compilation and build**

Run:
```bash
cd backend
dotnet build
```

Expected: SUCCESS

**Step 3: Test endpoint manually (optional verification)**

Start backend:
```bash
cd backend
dotnet run --project src/Anela.Heblo.API
```

Expected: Server starts on port 5001

**Step 4: Commit**

```bash
git add src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs
git commit -m "feat: add POST /api/stockupoperations/{id}/accept endpoint

- Add controller action for accepting failed operations
- Returns AcceptStockUpOperationResponse with success/error info
- Follows same pattern as retry endpoint"
```

---

## Task 5: Generate OpenAPI Client

**Files:**
- Modify: `frontend/src/api/generated/api-client.ts` (auto-generated)

**Step 1: Build backend to regenerate OpenAPI spec**

Run:
```bash
cd backend
dotnet build
```

Expected: SUCCESS (generates swagger.json)

**Step 2: Regenerate frontend API client**

Run:
```bash
cd frontend
npm run generate-api-client
```

Expected: SUCCESS - "api-client.ts generated successfully"

**Step 3: Verify new method exists**

Run:
```bash
grep -n "stockUpOperations_AcceptOperation" frontend/src/api/generated/api-client.ts
```

Expected: Find method definition like:
```
19XXX:    async stockUpOperations_AcceptOperation(id: number): Promise<AcceptStockUpOperationResponse>
```

**Step 4: Commit generated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate API client with AcceptOperation endpoint"
```

---

## Task 6: Create Frontend Mutation Hook

**Files:**
- Modify: `frontend/src/api/hooks/useStockUpOperations.ts:80-100`

**Step 1: Write test for accept mutation hook**

Create: `frontend/src/api/hooks/__tests__/useAcceptStockUpOperation.test.ts`

```typescript
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useAcceptStockUpOperationMutation } from "../useStockUpOperations";
import { getAuthenticatedApiClient } from "../../client";

// Mock the API client
jest.mock("../../client");

describe("useAcceptStockUpOperationMutation", () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });
    jest.clearAllMocks();
  });

  it("should call accept endpoint and invalidate queries on success", async () => {
    const mockAcceptOperation = jest.fn().mockResolvedValue({
      success: true,
      status: "Success",
      errorMessage: null,
    });

    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      stockUpOperations_AcceptOperation: mockAcceptOperation,
    });

    const wrapper = ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );

    const { result } = renderHook(() => useAcceptStockUpOperationMutation(), {
      wrapper,
    });

    result.current.mutate(123);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockAcceptOperation).toHaveBeenCalledWith(123);
    expect(mockAcceptOperation).toHaveBeenCalledTimes(1);
  });
});
```

**Step 2: Run test to verify it fails**

Run:
```bash
cd frontend
npm test -- useAcceptStockUpOperation.test.ts
```

Expected: FAIL - "useAcceptStockUpOperationMutation is not a function"

**Step 3: Add accept mutation hook**

Modify: `frontend/src/api/hooks/useStockUpOperations.ts`

Add import at top:
```typescript
import {
  GetStockUpOperationsResponse,
  GetStockUpOperationsSummaryResponse,
  RetryStockUpOperationResponse,
  AcceptStockUpOperationResponse,  // ADD THIS
  StockUpSourceType,
} from "../generated/api-client";
```

Add after `useRetryStockUpOperationMutation` hook (after line 80):

```typescript
export const useAcceptStockUpOperationMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (operationId: number): Promise<AcceptStockUpOperationResponse> => {
      const client = getStockUpOperationsClient();
      return await client.stockUpOperations_AcceptOperation(operationId);
    },
    onSuccess: () => {
      // Invalidate all stock-up operations queries to refresh the list
      queryClient.invalidateQueries({
        queryKey: stockUpOperationsKeys.lists(),
      });
      // Also invalidate summaries since failed count will decrease
      queryClient.invalidateQueries({
        queryKey: stockUpOperationsKeys.summaries(),
      });
    },
  });
};
```

**Step 4: Run test to verify it passes**

Run:
```bash
cd frontend
npm test -- useAcceptStockUpOperation.test.ts
```

Expected: PASS

**Step 5: Commit**

```bash
git add frontend/src/api/hooks/useStockUpOperations.ts
git add frontend/src/api/hooks/__tests__/useAcceptStockUpOperation.test.ts
git commit -m "feat: add useAcceptStockUpOperationMutation hook

- Add React Query mutation for accepting failed operations
- Invalidate both operations list and summary queries on success
- Add unit test for mutation hook"
```

---

## Task 7: Add Accept Button to Stock Operations Page

**Files:**
- Modify: `frontend/src/pages/StockOperationsPage.tsx:139-345`

**Step 1: Import the new mutation hook**

Modify: `frontend/src/pages/StockOperationsPage.tsx`

Change import at line 20-23:
```typescript
import {
  useStockUpOperationsQuery,
  useRetryStockUpOperationMutation,
  useAcceptStockUpOperationMutation,  // ADD THIS
} from "../api/hooks/useStockUpOperations";
```

**Step 2: Add accept mutation to component**

Modify: `frontend/src/pages/StockOperationsPage.tsx`

Add after line 139 where `retryMutation` is defined:

```typescript
  const retryMutation = useRetryStockUpOperationMutation();
  const acceptMutation = useAcceptStockUpOperationMutation();  // ADD THIS
```

**Step 3: Add accept handler function**

Add after `handleRetryWithConfirmation` function (after line 345):

```typescript
  const handleAccept = async (operation: any) => {
    try {
      await acceptMutation.mutateAsync(operation.id!);
      refetch();
    } catch (error) {
      console.error("Chyba při akceptování operace:", error);
    }
  };
```

**Step 4: Add accept button to actions column**

Modify the actions column in the table body (around line 713-726).

Replace the current actions cell with:

```typescript
                    <td className="px-6 py-4 whitespace-nowrap text-sm">
                      <div className="flex items-center gap-2">
                        {canRetry(operation.state) && operation.id && (
                          <button
                            onClick={() => handleRetryWithConfirmation(operation)}
                            disabled={retryMutation.isPending}
                            className={`inline-flex items-center px-3 py-1 disabled:bg-gray-400 text-white text-xs font-medium rounded transition-colors duration-200 ${getRetryButtonColor(
                              operation.state
                            )}`}
                          >
                            {getRetryButtonIcon(operation.state)}
                            <span className="ml-1">{getRetryButtonLabel(operation.state)}</span>
                          </button>
                        )}

                        {operation.state === StockUpOperationState.Failed && operation.id && (
                          <button
                            onClick={() => handleAccept(operation)}
                            disabled={acceptMutation.isPending}
                            className="inline-flex items-center px-3 py-1 bg-green-600 hover:bg-green-700 disabled:bg-gray-400 text-white text-xs font-medium rounded transition-colors duration-200"
                            title="Akceptovat - označit jako vyřešené"
                          >
                            <CheckCircle className="h-3 w-3" />
                            <span className="ml-1">Akceptovat</span>
                          </button>
                        )}
                      </div>
                    </td>
```

**Step 5: Test UI locally**

Run:
```bash
cd frontend
npm start
```

Navigate to: http://localhost:3000/stock-up-operations

Expected:
- Failed operations show both "Opakovat" (red) and "Akceptovat" (green) buttons
- Pending/Submitted operations show only retry button
- Completed operations show no buttons

**Step 6: Verify accept functionality**

1. Click "Akceptovat" on a Failed operation
2. Operation should disappear from "Active" filter
3. Find it in "Completed" filter with preserved error message

**Step 7: Commit**

```bash
git add frontend/src/pages/StockOperationsPage.tsx
git commit -m "feat: add Accept button for failed stock-up operations

- Add green 'Akceptovat' button next to retry button for Failed operations
- Call accept mutation without confirmation dialog
- Refresh operations list after acceptance
- Failed operations transition to Completed and disappear from Active view"
```

---

## Task 8: Add E2E Test

**Files:**
- Create: `frontend/test/e2e/stock-operations-accept.spec.ts`

**Step 1: Create E2E test**

Create: `frontend/test/e2e/stock-operations-accept.spec.ts`

```typescript
import { test, expect } from '@playwright/test';
import { navigateToApp } from './helpers/e2e-auth-helper';

test.describe('Stock Operations - Accept Failed Operations', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page);
  });

  test('should show Accept button only for Failed operations', async ({ page }) => {
    await page.goto('/stock-up-operations');

    // Apply filter to show only Failed operations
    await page.selectOption('select', 'Failed');
    await page.click('text=Použít filtry');

    // Wait for results
    await page.waitForSelector('table tbody tr', { timeout: 5000 });

    // Get first Failed operation row
    const firstRow = page.locator('table tbody tr').first();

    // Verify Accept button exists
    const acceptButton = firstRow.locator('button:has-text("Akceptovat")');
    await expect(acceptButton).toBeVisible();

    // Verify button is green
    await expect(acceptButton).toHaveClass(/bg-green-600/);
  });

  test('should accept failed operation and move to Completed', async ({ page }) => {
    await page.goto('/stock-up-operations');

    // Filter for Failed operations
    await page.selectOption('select', 'Failed');
    await page.click('text=Použít filtry');

    // Wait for Failed operations
    await page.waitForSelector('table tbody tr', { timeout: 5000 });

    const initialRowCount = await page.locator('table tbody tr').count();

    if (initialRowCount === 0) {
      test.skip('No Failed operations available for testing');
      return;
    }

    // Get document number of first failed operation
    const firstRow = page.locator('table tbody tr').first();
    const documentNumber = await firstRow.locator('td:nth-child(2)').textContent();

    // Click Accept button
    await firstRow.locator('button:has-text("Akceptovat")').click();

    // Wait for operation to disappear from Failed list
    await page.waitForTimeout(1000);
    await page.click('text=Použít filtry'); // Refresh

    const newRowCount = await page.locator('table tbody tr').count();
    expect(newRowCount).toBe(initialRowCount - 1);

    // Switch to Completed filter
    await page.selectOption('select', 'Completed');
    await page.click('text=Použít filtry');

    // Verify operation appears in Completed with preserved error message
    await page.waitForSelector('table tbody tr', { timeout: 5000 });

    const completedRow = page.locator(`table tbody tr:has-text("${documentNumber}")`);
    await expect(completedRow).toBeVisible();

    // Verify error message column contains "Manually accepted"
    const errorCell = completedRow.locator('td:nth-child(7)');
    const errorText = await errorCell.textContent();
    expect(errorText).toContain('Manually accepted');
  });

  test('should not show Accept button for Completed operations', async ({ page }) => {
    await page.goto('/stock-up-operations');

    // Filter for Completed operations
    await page.selectOption('select', 'Completed');
    await page.click('text=Použít filtry');

    await page.waitForSelector('table tbody tr', { timeout: 5000 });

    // Verify no Accept buttons exist
    const acceptButtons = page.locator('button:has-text("Akceptovat")');
    await expect(acceptButtons).toHaveCount(0);
  });
});
```

**Step 2: Run E2E test against staging**

Run:
```bash
./scripts/run-playwright-tests.sh stock-operations-accept.spec.ts
```

Expected: PASS (3 tests) or SKIP if no Failed operations in staging

**Step 3: Commit**

```bash
git add frontend/test/e2e/stock-operations-accept.spec.ts
git commit -m "test: add E2E tests for accepting failed stock operations

- Verify Accept button only visible for Failed operations
- Test accept flow: Failed → Completed transition
- Verify accepted operations preserve error message with audit note
- Verify no Accept button for Completed operations"
```

---

## Task 9: Final Verification and Documentation

**Files:**
- Modify: `docs/📘 Architecture Documentation – MVP Work.md` (optional documentation update)

**Step 1: Run full backend test suite**

Run:
```bash
cd backend
dotnet test --verbosity normal
```

Expected: ALL PASS

**Step 2: Run full frontend test suite**

Run:
```bash
cd frontend
npm test -- --watchAll=false
```

Expected: ALL PASS

**Step 3: Verify formatting**

Run:
```bash
cd backend
dotnet format --verify-no-changes

cd ../frontend
npm run lint
```

Expected: No formatting issues

**Step 4: Build both projects**

Run:
```bash
cd backend
dotnet build --configuration Release

cd ../frontend
npm run build
```

Expected: Both build successfully

**Step 5: Manual smoke test**

1. Start backend: `cd backend && dotnet run --project src/Anela.Heblo.API`
2. Start frontend: `cd frontend && npm start`
3. Navigate to Stock Operations page
4. Create a failed operation (or use existing)
5. Click "Akceptovat" button
6. Verify operation moves to Completed
7. Check error message contains acceptance timestamp

**Step 6: Final commit**

```bash
git add -A
git commit -m "docs: update implementation verification

All tests passing:
- Backend unit tests (StockUpOperation, Handler)
- Frontend unit tests (mutation hook)
- E2E tests (accept flow)
- Manual smoke test completed"
```

---

## Completion Checklist

- [ ] Domain method `AcceptFailure()` added with unit tests
- [ ] MediatR handler `AcceptStockUpOperationHandler` implemented with tests
- [ ] Controller endpoint `POST /api/stockupoperations/{id}/accept` added
- [ ] OpenAPI client regenerated
- [ ] Frontend mutation hook `useAcceptStockUpOperationMutation` created with test
- [ ] UI Accept button added to StockOperationsPage
- [ ] E2E test coverage for accept flow
- [ ] All tests passing (BE + FE + E2E)
- [ ] Code formatting validated
- [ ] Builds successful (BE + FE)
- [ ] Manual smoke test completed

## Success Criteria

**Backend:**
- Failed operations can be accepted via API endpoint
- State transitions correctly: Failed → Completed
- Error message preserved with acceptance timestamp
- Repository saves changes correctly

**Frontend:**
- Green "Akceptovat" button visible only for Failed operations
- Clicking accept immediately transitions operation to Completed
- Accepted operations disappear from "Active" filter
- Operations list and summary refresh after acceptance
- No confirmation dialog (as per requirement)

**Audit Trail:**
- Original error message preserved
- Acceptance timestamp appended to ErrorMessage
- CompletedAt field set correctly

## Notes

- No confirmation dialog as per user requirement (Option A)
- Accept is a one-way operation (no "un-accept")
- Accepted operations are indistinguishable from genuinely completed operations except for ErrorMessage content
- Consider adding user tracking (who accepted) in future iteration if needed
