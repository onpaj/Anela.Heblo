# StockUpOperation Status Indicator Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a status indicator to GiftPackageManufacturing UI showing pending/failed StockUpOperation counts with auto-refresh and navigation to management page.

**Architecture:** New backend summary endpoint returns aggregated counts (Pending, Submitted, Failed) by SourceType. Frontend polls every 15s and displays blue banner when operations exist, clicking navigates to filtered operations page.

**Tech Stack:** Backend: .NET 8, MediatR, EF Core, xUnit, Moq | Frontend: React, TanStack Query, React Router, Lucide icons, Tailwind CSS, Jest

---

## Task 1: Backend - Create Request DTO

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/GetStockUpOperationsSummaryRequest.cs`

**Step 1: Create directory structure**

```bash
mkdir -p backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary
```

**Step 2: Write the request DTO**

Create file with this content:

```csharp
using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;

public class GetStockUpOperationsSummaryRequest : IRequest<GetStockUpOperationsSummaryResponse>
{
    public StockUpSourceType? SourceType { get; set; }
}
```

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/GetStockUpOperationsSummaryRequest.cs
git commit -m "feat(backend): add GetStockUpOperationsSummaryRequest DTO"
```

---

## Task 2: Backend - Create Response DTO

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/GetStockUpOperationsSummaryResponse.cs`

**Step 1: Write the response DTO**

Create file with this content:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;

public class GetStockUpOperationsSummaryResponse : BaseResponse
{
    public int PendingCount { get; set; }
    public int SubmittedCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalInQueue => PendingCount + SubmittedCount;
}
```

**Step 2: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/GetStockUpOperationsSummaryResponse.cs
git commit -m "feat(backend): add GetStockUpOperationsSummaryResponse DTO"
```

---

## Task 3: Backend - Write Handler Test (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetStockUpOperationsSummaryHandlerTests.cs`

**Step 1: Write the failing test**

Create file with this content:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetStockUpOperationsSummaryHandlerTests
{
    private readonly Mock<IStockUpOperationRepository> _repositoryMock;
    private readonly GetStockUpOperationsSummaryHandler _handler;

    public GetStockUpOperationsSummaryHandlerTests()
    {
        _repositoryMock = new Mock<IStockUpOperationRepository>();
        _handler = new GetStockUpOperationsSummaryHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_NoOperations_ReturnsZeroCounts()
    {
        // Arrange
        var request = new GetStockUpOperationsSummaryRequest();
        var emptyOperations = new List<StockUpOperation>().AsQueryable();

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(emptyOperations);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.PendingCount);
        Assert.Equal(0, result.SubmittedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.TotalInQueue);
    }

    [Fact]
    public async Task Handle_WithOperations_ReturnsCorrectCounts()
    {
        // Arrange
        var request = new GetStockUpOperationsSummaryRequest
        {
            SourceType = StockUpSourceType.GiftPackageManufacture
        };

        var operations = new List<StockUpOperation>
        {
            new("GPM-000001-PROD1", "PROD1", 10, StockUpSourceType.GiftPackageManufacture, 1),
            new("GPM-000001-PROD2", "PROD2", 5, StockUpSourceType.GiftPackageManufacture, 1),
            new("GPM-000002-PROD3", "PROD3", 8, StockUpSourceType.GiftPackageManufacture, 2),
            new("BOX-000001-PROD4", "PROD4", 15, StockUpSourceType.TransportBox, 1),
        }.AsQueryable();

        // Set states via reflection or state transition methods
        operations.ElementAt(0).MarkAsSubmitted(System.DateTime.UtcNow);
        operations.ElementAt(1).MarkAsSubmitted(System.DateTime.UtcNow);
        operations.ElementAt(1).MarkAsFailed(System.DateTime.UtcNow, "Test error");
        // operations.ElementAt(2) stays Pending
        operations.ElementAt(3).MarkAsSubmitted(System.DateTime.UtcNow);

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.PendingCount); // GPM-000002-PROD3
        Assert.Equal(1, result.SubmittedCount); // GPM-000001-PROD1
        Assert.Equal(1, result.FailedCount); // GPM-000001-PROD2
        Assert.Equal(2, result.TotalInQueue); // Pending + Submitted
    }

    [Fact]
    public async Task Handle_NoSourceTypeFilter_ReturnsAllOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsSummaryRequest(); // No SourceType filter

        var operations = new List<StockUpOperation>
        {
            new("GPM-000001-PROD1", "PROD1", 10, StockUpSourceType.GiftPackageManufacture, 1),
            new("BOX-000001-PROD2", "PROD2", 5, StockUpSourceType.TransportBox, 1),
        }.AsQueryable();

        // Both pending
        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.PendingCount); // Both operations
        Assert.Equal(0, result.SubmittedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(2, result.TotalInQueue);
    }
}
```

**Step 2: Run test to verify it fails**

```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "GetStockUpOperationsSummaryHandlerTests" -v n
```

Expected: FAIL with "GetStockUpOperationsSummaryHandler does not exist"

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/GetStockUpOperationsSummaryHandlerTests.cs
git commit -m "test(backend): add failing tests for GetStockUpOperationsSummary handler"
```

---

## Task 4: Backend - Implement Handler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/GetStockUpOperationsSummaryHandler.cs`

**Step 1: Write minimal handler implementation**

Create file with this content:

```csharp
using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;

public class GetStockUpOperationsSummaryHandler : IRequestHandler<GetStockUpOperationsSummaryRequest, GetStockUpOperationsSummaryResponse>
{
    private readonly IStockUpOperationRepository _repository;

    public GetStockUpOperationsSummaryHandler(IStockUpOperationRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetStockUpOperationsSummaryResponse> Handle(GetStockUpOperationsSummaryRequest request, CancellationToken cancellationToken)
    {
        var query = _repository.GetAll()
            .Where(x => x.State == StockUpOperationState.Pending
                     || x.State == StockUpOperationState.Submitted
                     || x.State == StockUpOperationState.Failed);

        // Apply optional SourceType filter
        if (request.SourceType.HasValue)
        {
            query = query.Where(x => x.SourceType == request.SourceType.Value);
        }

        // Group by state and count efficiently
        var counts = await query
            .GroupBy(x => x.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // Map to response
        return new GetStockUpOperationsSummaryResponse
        {
            PendingCount = counts.FirstOrDefault(x => x.State == StockUpOperationState.Pending)?.Count ?? 0,
            SubmittedCount = counts.FirstOrDefault(x => x.State == StockUpOperationState.Submitted)?.Count ?? 0,
            FailedCount = counts.FirstOrDefault(x => x.State == StockUpOperationState.Failed)?.Count ?? 0,
            Success = true
        };
    }
}
```

**Step 2: Run tests to verify they pass**

```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "GetStockUpOperationsSummaryHandlerTests" -v n
```

Expected: PASS (3 tests)

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockUpOperationsSummary/GetStockUpOperationsSummaryHandler.cs
git commit -m "feat(backend): implement GetStockUpOperationsSummary handler"
```

---

## Task 5: Backend - Add Controller Endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs`

**Step 1: Add using statement**

Add this line at the top of the file after existing usings:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;
```

**Step 2: Add summary endpoint**

Add this method to the controller class (after the existing methods):

```csharp
/// <summary>
/// Get summary counts of stock-up operations by state
/// </summary>
/// <param name="sourceType">Optional filter by source type (GiftPackageManufacture or TransportBox)</param>
[HttpGet("summary")]
public async Task<ActionResult<GetStockUpOperationsSummaryResponse>> GetSummary(
    [FromQuery] StockUpSourceType? sourceType = null)
{
    var request = new GetStockUpOperationsSummaryRequest
    {
        SourceType = sourceType
    };

    var response = await _mediator.Send(request);
    return Ok(response);
}
```

**Step 3: Verify backend builds**

```bash
cd backend/src/Anela.Heblo.API
dotnet build
```

Expected: Build succeeded

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/StockUpOperationsController.cs
git commit -m "feat(backend): add summary endpoint to StockUpOperations controller"
```

---

## Task 6: Backend - Regenerate OpenAPI Client

**Files:**
- Modified: `frontend/src/api/generated/api-client.ts` (auto-generated)

**Step 1: Start backend API**

```bash
cd backend/src/Anela.Heblo.API
dotnet run
```

Wait for "Now listening on: http://localhost:5001"

**Step 2: Regenerate frontend client in a new terminal**

```bash
cd backend/src/Anela.Heblo.API
dotnet msbuild -t:GenerateFrontendClientManual
```

Expected: "Frontend client generated successfully"

**Step 3: Stop backend**

Press Ctrl+C in the terminal running the API

**Step 4: Verify generated client**

Check that `frontend/src/api/generated/api-client.ts` contains:
- `stockUpOperations_GetSummary` method
- `GetStockUpOperationsSummaryResponse` interface with `pendingCount`, `submittedCount`, `failedCount`, `totalInQueue`

**Step 5: Commit**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(frontend): regenerate API client with summary endpoint"
```

---

## Task 7: Frontend - Create useStockUpOperations Hook File

**Files:**
- Create: `frontend/src/api/hooks/useStockUpOperations.ts`

**Step 1: Create the hook file**

Create file with this content:

```typescript
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  GetStockUpOperationsSummaryResponse,
  StockUpSourceType,
} from "../generated/api-client";

/**
 * Hook to get StockUpOperations summary counts (Pending, Submitted, Failed)
 * Polls every 15 seconds for live updates
 */
export const useStockUpOperationsSummary = (sourceType?: StockUpSourceType) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.stockUpOperations, "summary", sourceType],
    queryFn: async (): Promise<GetStockUpOperationsSummaryResponse> => {
      const client = getAuthenticatedApiClient();
      return await (client as any).stockUpOperations_GetSummary(sourceType);
    },
    refetchInterval: 15000, // Poll every 15 seconds
    staleTime: 10000, // Consider stale after 10 seconds
    gcTime: 30000, // Keep in cache for 30 seconds
  });
};
```

**Step 2: Add QUERY_KEYS entry**

Modify `frontend/src/api/client.ts` to add `stockUpOperations` key to `QUERY_KEYS`:

Find the `QUERY_KEYS` object and add:

```typescript
stockUpOperations: ['stock-up-operations'] as const,
```

**Step 3: Commit**

```bash
git add frontend/src/api/hooks/useStockUpOperations.ts frontend/src/api/client.ts
git commit -m "feat(frontend): add useStockUpOperationsSummary hook with 15s polling"
```

---

## Task 8: Frontend - Write Hook Test (TDD)

**Files:**
- Create: `frontend/src/api/hooks/__tests__/useStockUpOperations.test.ts`

**Step 1: Write the failing test**

Create file with this content:

```typescript
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React, { ReactNode } from "react";
import { useStockUpOperationsSummary } from "../useStockUpOperations";
import { getAuthenticatedApiClient } from "../../client";
import { StockUpSourceType } from "../../generated/api-client";

// Mock the API client
jest.mock("../../client");
const mockGetAuthenticatedApiClient =
  getAuthenticatedApiClient as jest.MockedFunction<
    typeof getAuthenticatedApiClient
  >;

const mockSummaryResponse = {
  success: true,
  pendingCount: 2,
  submittedCount: 1,
  failedCount: 1,
  totalInQueue: 3,
};

describe("useStockUpOperationsSummary", () => {
  let queryClient: QueryClient;
  let mockApiClient: any;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    mockApiClient = {
      stockUpOperations_GetSummary: jest.fn().mockResolvedValue(mockSummaryResponse),
    };

    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );

  it("should fetch summary with no source type filter", async () => {
    const { result } = renderHook(() => useStockUpOperationsSummary(), {
      wrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockApiClient.stockUpOperations_GetSummary).toHaveBeenCalledWith(undefined);
    expect(result.current.data).toEqual(mockSummaryResponse);
  });

  it("should fetch summary filtered by GiftPackageManufacture", async () => {
    const { result } = renderHook(
      () => useStockUpOperationsSummary(StockUpSourceType.GiftPackageManufacture),
      { wrapper }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockApiClient.stockUpOperations_GetSummary).toHaveBeenCalledWith(
      StockUpSourceType.GiftPackageManufacture
    );
    expect(result.current.data).toEqual(mockSummaryResponse);
  });

  it("should handle API errors gracefully", async () => {
    mockApiClient.stockUpOperations_GetSummary.mockRejectedValue(
      new Error("API Error")
    );

    const { result } = renderHook(() => useStockUpOperationsSummary(), {
      wrapper,
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toEqual(new Error("API Error"));
  });
});
```

**Step 2: Run test to verify it passes**

```bash
cd frontend
npm test -- useStockUpOperations.test.ts
```

Expected: PASS (3 tests)

**Step 3: Commit**

```bash
git add frontend/src/api/hooks/__tests__/useStockUpOperations.test.ts
git commit -m "test(frontend): add tests for useStockUpOperationsSummary hook"
```

---

## Task 9: Frontend - Create StockUpOperationStatusIndicator Component

**Files:**
- Create: `frontend/src/components/pages/GiftPackageManufacturing/StockUpOperationStatusIndicator.tsx`

**Step 1: Write the component**

Create file with this content:

```typescript
import React from "react";
import { useNavigate } from "react-router-dom";
import { Loader2, AlertTriangle, ChevronRight } from "lucide-react";
import { GetStockUpOperationsSummaryResponse } from "../../../api/generated/api-client";

interface StockUpOperationStatusIndicatorProps {
  summary: GetStockUpOperationsSummaryResponse;
}

const StockUpOperationStatusIndicator: React.FC<
  StockUpOperationStatusIndicatorProps
> = ({ summary }) => {
  const navigate = useNavigate();

  const handleClick = () => {
    // Navigate to stock-up operations page with filters
    navigate(
      "/stock-up-operations?sourceType=GiftPackageManufacture&state=Pending,Submitted,Failed"
    );
  };

  return (
    <div
      className="mb-4 p-4 bg-blue-50 rounded-lg border border-blue-200 cursor-pointer hover:bg-blue-100 transition-colors"
      onClick={handleClick}
      data-testid="stockup-status-indicator"
    >
      <div className="flex items-center justify-between">
        <div className="flex items-center space-x-4">
          {summary.totalInQueue > 0 && (
            <div className="flex items-center space-x-2" data-testid="queue-indicator">
              <Loader2 className="h-5 w-5 text-blue-600 animate-spin" />
              <span className="text-sm font-medium text-blue-900">
                {summary.totalInQueue} operací ve frontě
              </span>
            </div>
          )}

          {summary.failedCount > 0 && (
            <div className="flex items-center space-x-2" data-testid="failed-indicator">
              <AlertTriangle className="h-5 w-5 text-red-600" />
              <span className="text-sm font-medium text-red-900">
                {summary.failedCount} selhalo
              </span>
            </div>
          )}
        </div>

        <ChevronRight className="h-5 w-5 text-gray-400" />
      </div>
    </div>
  );
};

export default StockUpOperationStatusIndicator;
```

**Step 2: Verify TypeScript compiles**

```bash
cd frontend
npm run type-check
```

Expected: No errors

**Step 3: Commit**

```bash
git add frontend/src/components/pages/GiftPackageManufacturing/StockUpOperationStatusIndicator.tsx
git commit -m "feat(frontend): add StockUpOperationStatusIndicator component"
```

---

## Task 10: Frontend - Write Component Test (TDD)

**Files:**
- Create: `frontend/src/components/pages/GiftPackageManufacturing/__tests__/StockUpOperationStatusIndicator.test.tsx`

**Step 1: Create __tests__ directory if it doesn't exist**

```bash
mkdir -p frontend/src/components/pages/GiftPackageManufacturing/__tests__
```

**Step 2: Write the component test**

Create file with this content:

```typescript
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { BrowserRouter } from "react-router-dom";
import StockUpOperationStatusIndicator from "../StockUpOperationStatusIndicator";
import { GetStockUpOperationsSummaryResponse } from "../../../../api/generated/api-client";

const mockNavigate = jest.fn();

jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

describe("StockUpOperationStatusIndicator", () => {
  beforeEach(() => {
    mockNavigate.mockClear();
  });

  const renderComponent = (summary: GetStockUpOperationsSummaryResponse) => {
    return render(
      <BrowserRouter>
        <StockUpOperationStatusIndicator summary={summary} />
      </BrowserRouter>
    );
  };

  it("should display in-queue count when totalInQueue > 0", () => {
    const summary: GetStockUpOperationsSummaryResponse = {
      success: true,
      pendingCount: 2,
      submittedCount: 1,
      failedCount: 0,
      totalInQueue: 3,
    };

    renderComponent(summary);

    expect(screen.getByTestId("queue-indicator")).toBeInTheDocument();
    expect(screen.getByText("3 operací ve frontě")).toBeInTheDocument();
  });

  it("should display failed count when failedCount > 0", () => {
    const summary: GetStockUpOperationsSummaryResponse = {
      success: true,
      pendingCount: 0,
      submittedCount: 0,
      failedCount: 2,
      totalInQueue: 0,
    };

    renderComponent(summary);

    expect(screen.getByTestId("failed-indicator")).toBeInTheDocument();
    expect(screen.getByText("2 selhalo")).toBeInTheDocument();
  });

  it("should display both indicators when both counts > 0", () => {
    const summary: GetStockUpOperationsSummaryResponse = {
      success: true,
      pendingCount: 1,
      submittedCount: 2,
      failedCount: 3,
      totalInQueue: 3,
    };

    renderComponent(summary);

    expect(screen.getByTestId("queue-indicator")).toBeInTheDocument();
    expect(screen.getByTestId("failed-indicator")).toBeInTheDocument();
    expect(screen.getByText("3 operací ve frontě")).toBeInTheDocument();
    expect(screen.getByText("3 selhalo")).toBeInTheDocument();
  });

  it("should navigate to stock-up operations page on click", () => {
    const summary: GetStockUpOperationsSummaryResponse = {
      success: true,
      pendingCount: 1,
      submittedCount: 0,
      failedCount: 0,
      totalInQueue: 1,
    };

    renderComponent(summary);

    const indicator = screen.getByTestId("stockup-status-indicator");
    fireEvent.click(indicator);

    expect(mockNavigate).toHaveBeenCalledWith(
      "/stock-up-operations?sourceType=GiftPackageManufacture&state=Pending,Submitted,Failed"
    );
  });
});
```

**Step 3: Run component test**

```bash
cd frontend
npm test -- StockUpOperationStatusIndicator.test.tsx
```

Expected: PASS (4 tests)

**Step 4: Commit**

```bash
git add frontend/src/components/pages/GiftPackageManufacturing/__tests__/StockUpOperationStatusIndicator.test.tsx
git commit -m "test(frontend): add tests for StockUpOperationStatusIndicator component"
```

---

## Task 11: Frontend - Integrate Component into GiftPackageManufacturing Page

**Files:**
- Modify: `frontend/src/components/pages/GiftPackageManufacturing/index.tsx`

**Step 1: Add imports**

Add these imports at the top of the file:

```typescript
import StockUpOperationStatusIndicator from './StockUpOperationStatusIndicator';
import { useStockUpOperationsSummary } from '../../../api/hooks/useStockUpOperations';
import { StockUpSourceType } from '../../../api/generated/api-client';
```

**Step 2: Add hook call inside the component**

Add this line after the existing hooks (after `const enqueueManufactureMutation = ...`):

```typescript
// Add summary hook for StockUpOperations status
const { data: stockUpSummary } = useStockUpOperationsSummary(
  StockUpSourceType.GiftPackageManufacture
);

// Conditionally show indicator
const showIndicator = stockUpSummary &&
  (stockUpSummary.totalInQueue > 0 || stockUpSummary.failedCount > 0);
```

**Step 3: Add component to JSX**

Add this at the beginning of the return statement (before `<GiftPackageManufacturingList>`):

```typescript
return (
  <>
    {showIndicator && (
      <StockUpOperationStatusIndicator summary={stockUpSummary} />
    )}

    <GiftPackageManufacturingList
      onPackageClick={handlePackageClick}
      // ... rest of props
    />

    {/* Existing modals... */}
  </>
);
```

**Step 4: Verify frontend builds**

```bash
cd frontend
npm run build
```

Expected: Build succeeded

**Step 5: Commit**

```bash
git add frontend/src/components/pages/GiftPackageManufacturing/index.tsx
git commit -m "feat(frontend): integrate StockUpOperation status indicator into GiftPackageManufacturing page"
```

---

## Task 12: Verification - Backend Build and Tests

**Step 1: Build backend**

```bash
cd backend/src/Anela.Heblo.API
dotnet build --no-incremental
```

Expected: Build succeeded, 0 errors

**Step 2: Run all backend tests**

```bash
cd backend/test/Anela.Heblo.Tests
dotnet test
```

Expected: All tests pass

**Step 3: Verify new handler tests specifically**

```bash
cd backend/test/Anela.Heblo.Tests
dotnet test --filter "GetStockUpOperationsSummaryHandlerTests" -v n
```

Expected: 3 tests passed

---

## Task 13: Verification - Frontend Build and Tests

**Step 1: Run frontend tests**

```bash
cd frontend
npm test -- --coverage --watchAll=false
```

Expected: All tests pass, including new hook and component tests

**Step 2: Build frontend**

```bash
cd frontend
npm run build
```

Expected: Build succeeded, no TypeScript errors

**Step 3: Type check**

```bash
cd frontend
npm run type-check
```

Expected: No type errors

---

## Task 14: Manual Testing - Start Application

**Step 1: Start backend**

```bash
cd backend/src/Anela.Heblo.API
dotnet run
```

Wait for "Now listening on: http://localhost:5001"

**Step 2: Start frontend in new terminal**

```bash
cd frontend
npm start
```

Wait for "webpack compiled successfully"

**Step 3: Test in browser**

1. Navigate to `http://localhost:3000/gift-package-manufacturing`
2. Verify indicator appears if there are pending/failed operations
3. Verify indicator does NOT appear if all operations are completed
4. Click indicator and verify navigation to `/stock-up-operations` with query params
5. Wait 15 seconds and verify counts refresh

**Step 4: Test summary endpoint directly**

Open `http://localhost:5001/swagger` and test:
- GET `/api/stock-up-operations/summary` (no filter)
- GET `/api/stock-up-operations/summary?sourceType=1` (GiftPackageManufacture)

Expected: Returns correct counts based on database state

---

## Task 15: Final Commit and Cleanup

**Step 1: Final verification**

```bash
# Backend
cd backend/src/Anela.Heblo.API
dotnet build
cd ../../test/Anela.Heblo.Tests
dotnet test

# Frontend
cd ../../../frontend
npm test -- --watchAll=false
npm run build
```

Expected: All builds and tests pass

**Step 2: Create final commit message**

```bash
git add -A
git commit -m "$(cat <<'EOF'
feat: add StockUpOperation status indicator to GiftPackageManufacturing UI

- Backend: New summary endpoint returns aggregated counts by SourceType
- Frontend: Status indicator with 15s polling, shows pending/failed operations
- Click to navigate to filtered operations management page
- Only visible when operations exist (totalInQueue > 0 or failedCount > 0)

Backend changes:
- GetStockUpOperationsSummaryRequest/Response DTOs
- GetStockUpOperationsSummaryHandler with efficient GROUP BY query
- StockUpOperationsController.GetSummary endpoint
- Handler tests with Moq

Frontend changes:
- useStockUpOperationsSummary hook with auto-refresh
- StockUpOperationStatusIndicator component
- Integration into GiftPackageManufacturing page
- Hook and component tests with React Testing Library

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>
EOF
)"
```

**Step 3: Verify git log**

```bash
git log --oneline -15
```

Expected: See all commits from the implementation

---

## Success Criteria Checklist

- ✅ Backend summary endpoint returns correct counts
- ✅ Handler tests pass (3 tests)
- ✅ Frontend hook tests pass (3 tests)
- ✅ Component tests pass (4 tests)
- ✅ Indicator appears only when operations exist
- ✅ Auto-refreshes every 15 seconds
- ✅ Clicking navigates to StockUpOperations page with filters
- ✅ Visual design matches specification (blue/red theme)
- ✅ Backend build passes
- ✅ Frontend build passes
- ✅ All existing tests still pass
- ✅ Manual testing confirms functionality

## Reference Documents

- Design: `docs/plans/2026-01-21-stockup-status-indicator-design.md`
- Architecture: Clean Architecture with MediatR pattern
- Testing: TDD approach where applicable (handler and component tests)
