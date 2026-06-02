# Material Container Audit Page ("Šarže") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a read-only, filterable "Šarže" page under the Výroba menu that lists the material containers scanned in the Terminal lot-identification workflow.

**Architecture:** One small backend change — add an optional `code` filter to the existing `GET /api/material-containers` list endpoint (request → handler → repository), then rebuild to regenerate the TypeScript client. On the frontend, add a list query hook and a new page component that mirrors the established list-page convention (`PurchaseOrderList` / `InventoryList`), and wire it into the router and sidebar. No mutations, no new endpoints, no DTO changes.

**Tech Stack:** .NET 8 (MediatR, EF Core w/ PostgreSQL, xUnit + Moq), React 18 + TypeScript + Tailwind + React Query (`@tanstack/react-query`), Jest + React Testing Library.

**Spec reference:** `docs/superpowers/specs/2026-06-01-material-container-audit-page-design.md`

---

## File Structure

**Backend (modify):**
- `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/ListMaterialContainers/ListMaterialContainersRequest.cs` — add `Code` property
- `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/ListMaterialContainers/ListMaterialContainersHandler.cs` — pass `Code` through
- `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/IMaterialContainerRepository.cs` — add `code` param to `GetPaginatedAsync`
- `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerRepository.cs` — filter by `Code`
- `backend/src/Anela.Heblo.API/Controllers/MaterialContainersController.cs` — add `code` query param
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/ListMaterialContainersHandlerTests.cs` — update + add tests

**Frontend (create):**
- `frontend/src/components/pages/MaterialContainerList.tsx` — the page
- `frontend/src/components/pages/__tests__/MaterialContainerList.test.tsx` — page tests

**Frontend (modify):**
- `frontend/src/api/hooks/useMaterialContainers.ts` — add `useMaterialContainersList` query hook
- `frontend/src/api/hooks/__tests__/useMaterialContainers.test.ts` — add hook test
- `frontend/src/App.tsx` — register route + import
- `frontend/src/components/Layout/Sidebar.tsx` — add "Šarže" nav item to the Výroba section
- `frontend/src/api/generated/api-client.ts` — regenerated automatically by `dotnet build` (do not hand-edit)

---

## Phase 1 — Backend: add `code` filter

### Task 1: Thread a `code` filter through request → handler → repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/ListMaterialContainers/ListMaterialContainersRequest.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/IMaterialContainerRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/ListMaterialContainers/ListMaterialContainersHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/ListMaterialContainersHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

The existing test mocks `GetPaginatedAsync` with 5 positional args (`materialCode, lotCode, page, pageSize, ct`). We are adding a `code` arg between `lotCode` and `page`. Update the existing test's mock setup and add a new test. Replace the **entire** body of `ListMaterialContainersHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListMaterialContainers;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class ListMaterialContainersHandlerTests
{
    private readonly Mock<IMaterialContainerRepository> _containerRepo = new();
    private readonly ListMaterialContainersHandler _handler;

    public ListMaterialContainersHandlerTests()
    {
        _handler = new ListMaterialContainersHandler(NullLogger<ListMaterialContainersHandler>.Instance, _containerRepo.Object);
    }

    [Fact]
    public async Task Handle_FilterByMaterialCodeAndLotCode_DelegatesToRepository()
    {
        // Arrange
        var paged = new PagedResult<MaterialContainer>
        {
            Items = new List<MaterialContainer> { new MaterialContainer("INT-00000001", "MAT001", "L1", 25m, "kg", "user") },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 20
        };
        _containerRepo.Setup(r => r.GetPaginatedAsync("MAT001", "L1", null, 1, 20, default)).ReturnsAsync(paged);

        // Act
        var result = await _handler.Handle(new ListMaterialContainersRequest { MaterialCode = "MAT001", LotCode = "L1", Page = 1, PageSize = 20 }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Containers);
    }

    [Fact]
    public async Task Handle_FilterByCode_PassesCodeToRepository()
    {
        // Arrange
        var paged = new PagedResult<MaterialContainer>
        {
            Items = new List<MaterialContainer> { new MaterialContainer("M00001234", "MAT001", "L1", 25m, "kg", "user") },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 20
        };
        _containerRepo.Setup(r => r.GetPaginatedAsync(null, null, "M00001234", 1, 20, default)).ReturnsAsync(paged);

        // Act
        var result = await _handler.Handle(new ListMaterialContainersRequest { Code = "M00001234", Page = 1, PageSize = 20 }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Containers);
        Assert.Equal("M00001234", result.Containers[0].Code);
        _containerRepo.Verify(r => r.GetPaginatedAsync(null, null, "M00001234", 1, 20, default), Times.Once);
    }
}
```

- [ ] **Step 2: Run the test to confirm it fails**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~ListMaterialContainersHandlerTests" --no-build`
Expected: compile error — `ListMaterialContainersRequest` has no `Code` property, and `GetPaginatedAsync` does not accept a `code` arg.

- [ ] **Step 3: Add `Code` to the request**

Replace `ListMaterialContainersRequest.cs` with:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListMaterialContainers;

public class ListMaterialContainersRequest : IRequest<ListMaterialContainersResponse>
{
    public string? MaterialCode { get; set; }
    public string? LotCode { get; set; }
    public string? Code { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

- [ ] **Step 4: Add the `code` param to the repository interface**

Replace `IMaterialContainerRepository.cs` with:

```csharp
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface IMaterialContainerRepository : IRepository<MaterialContainer, int>
{
    Task<MaterialContainer?> GetByCodeAsync(string code, CancellationToken ct);
    Task<PagedResult<MaterialContainer>> GetPaginatedAsync(
        string? materialCode, string? lotCode, string? code, int page, int pageSize, CancellationToken ct);
    Task<string?> GetLastUsedLotCodeForMaterialAsync(string materialCode, CancellationToken ct);
}
```

- [ ] **Step 5: Implement the `code` filter in the repository**

In `MaterialContainerRepository.cs`, replace the `GetPaginatedAsync` method (keep `GetByCodeAsync` and `GetLastUsedLotCodeForMaterialAsync` unchanged):

```csharp
    public async Task<PagedResult<MaterialContainer>> GetPaginatedAsync(
        string? materialCode, string? lotCode, string? code, int page, int pageSize, CancellationToken ct)
    {
        var query = DbSet.AsQueryable();

        if (!string.IsNullOrWhiteSpace(materialCode))
            query = query.Where(x => x.MaterialCode == materialCode);

        if (!string.IsNullOrWhiteSpace(lotCode))
            query = query.Where(x => x.LotCode == lotCode);

        if (!string.IsNullOrWhiteSpace(code))
            query = query.Where(x => x.Code == code);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<MaterialContainer>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }
```

- [ ] **Step 6: Pass `Code` through the handler**

In `ListMaterialContainersHandler.cs`, replace the `GetPaginatedAsync` call inside `Handle` with:

```csharp
        var result = await _materialContainerRepository.GetPaginatedAsync(
            request.MaterialCode,
            request.LotCode,
            request.Code,
            request.Page,
            request.PageSize,
            cancellationToken);
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `cd backend && dotnet build && dotnet test --filter "FullyQualifiedName~ListMaterialContainersHandlerTests" --no-build`
Expected: both tests PASS.

- [ ] **Step 8: Format**

Run: `cd backend && dotnet format`
Expected: clean exit.

- [ ] **Step 9: Commit**

```bash
cd backend
git add -A
git commit -m "feat: add code filter to material container list query"
```

---

### Task 2: Expose the `code` query param on the controller and regenerate the TS client

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/MaterialContainersController.cs`
- Auto-regenerated: `frontend/src/api/generated/api-client.ts`

- [ ] **Step 1: Add the `code` query param to the GET action**

In `MaterialContainersController.cs`, replace the `GetMaterialContainers` action with (insert `code` after `lotCode`, before `page` — this ordering determines the generated client method's parameter order):

```csharp
    [HttpGet]
    public async Task<ActionResult<ListMaterialContainersResponse>> GetMaterialContainers(
        [FromQuery] string? materialCode,
        [FromQuery] string? lotCode,
        [FromQuery] string? code,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var request = new ListMaterialContainersRequest
        { MaterialCode = materialCode, LotCode = lotCode, Code = code, Page = page, PageSize = pageSize };
        return HandleResponse(await _mediator.Send(request, cancellationToken));
    }
```

- [ ] **Step 2: Build the backend (regenerates the TypeScript client)**

Run: `cd backend && dotnet build`
Expected: clean build. The build step regenerates `frontend/src/api/generated/api-client.ts`.

- [ ] **Step 3: Verify the generated client picked up the new param**

Run: `grep -n "materialContainers_GetMaterialContainers(materialCode" frontend/src/api/generated/api-client.ts`
Expected: the public method signature now lists `code` between `lotCode` and `page`, e.g.
`materialContainers_GetMaterialContainers(materialCode: string | null | undefined, lotCode: string | null | undefined, code: string | null | undefined, page: number | undefined, pageSize: number | undefined)`.

If the param is missing, the generation did not run — re-run `dotnet build` and confirm there were no build errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/MaterialContainersController.cs frontend/src/api/generated/api-client.ts
git commit -m "feat: expose code query param on material containers list endpoint"
```

---

## Phase 2 — Frontend

### Task 3: Add the `useMaterialContainersList` query hook

**Files:**
- Modify: `frontend/src/api/hooks/useMaterialContainers.ts`
- Test: `frontend/src/api/hooks/__tests__/useMaterialContainers.test.ts`

- [ ] **Step 1: Write the failing test**

Append this `describe` block to the END of `useMaterialContainers.test.ts` (after the `useLastUsedLotForMaterial` block, before EOF). Also add `useMaterialContainersList` to the import statement at the top of the file (the existing import from `'../useMaterialContainers'`):

```typescript
describe('useMaterialContainersList', () => {
  let mockList: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockList = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      materialContainers_GetMaterialContainers: mockList,
    } as any);
  });

  it('passes all filters and pagination to the generated client', async () => {
    mockList.mockResolvedValue({ success: true, containers: [], totalCount: 0, pageNumber: 1, pageSize: 20 });

    const { result } = renderHook(
      () =>
        useMaterialContainersList({
          materialCode: 'MAT001',
          lotCode: 'LOT001',
          code: 'M00001234',
          page: 2,
          pageSize: 50,
        }),
      { wrapper: createWrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockList).toHaveBeenCalledWith('MAT001', 'LOT001', 'M00001234', 2, 50);
  });

  it('sends undefined for empty filters and default pagination', async () => {
    mockList.mockResolvedValue({ success: true, containers: [], totalCount: 0, pageNumber: 1, pageSize: 20 });

    const { result } = renderHook(() => useMaterialContainersList({}), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockList).toHaveBeenCalledWith(undefined, undefined, undefined, 1, 20);
  });
});
```

Update the import line near the top of the test file from:

```typescript
import {
  useCreateMaterialContainers,
  useMaterialContainerByCode,
  useLastUsedLotForMaterial,
} from '../useMaterialContainers';
```

to:

```typescript
import {
  useCreateMaterialContainers,
  useMaterialContainerByCode,
  useLastUsedLotForMaterial,
  useMaterialContainersList,
} from '../useMaterialContainers';
```

- [ ] **Step 2: Run the test to confirm it fails**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useMaterialContainers.test.ts -t "useMaterialContainersList"`
Expected: FAIL — `useMaterialContainersList` is not exported (`is not a function`).

- [ ] **Step 3: Implement the hook**

In `useMaterialContainers.ts`:

1. Add `ListMaterialContainersResponse` to the import from `'../generated/api-client'` (the existing multi-line import block).
2. Add the request interface and hook below `useLastUsedLotForMaterial` (before the trailing `export type { ... }` re-export block):

```typescript
export interface MaterialContainersListRequest {
  materialCode?: string;
  lotCode?: string;
  code?: string;
  page?: number;
  pageSize?: number;
}

export const useMaterialContainersList = (request: MaterialContainersListRequest) =>
  useQuery({
    queryKey: ['materialContainers', 'list', request],
    queryFn: (): Promise<ListMaterialContainersResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.materialContainers_GetMaterialContainers(
        request.materialCode || undefined,
        request.lotCode || undefined,
        request.code || undefined,
        request.page ?? 1,
        request.pageSize ?? 20,
      );
    },
  });
```

3. Add `ListMaterialContainersResponse` to the trailing `export type { ... } from '../generated/api-client';` re-export block so the page can import the response type from this hook module.

The resulting import block at the top must include `ListMaterialContainersResponse`:

```typescript
import {
  CreateMaterialContainersRequest,
  CreateMaterialContainersResponse,
  GetMaterialContainerByCodeResponse,
  GetLastUsedLotForMaterialResponse,
  CreateMaterialContainerItem,
  ListMaterialContainersResponse,
} from '../generated/api-client';
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useMaterialContainers.test.ts`
Expected: all tests in the file PASS (existing + 2 new).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/useMaterialContainers.ts frontend/src/api/hooks/__tests__/useMaterialContainers.test.ts
git commit -m "feat: add useMaterialContainersList query hook"
```

---

### Task 4: Create the `MaterialContainerList` page

**Files:**
- Create: `frontend/src/components/pages/MaterialContainerList.tsx`
- Test: `frontend/src/components/pages/__tests__/MaterialContainerList.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/pages/__tests__/MaterialContainerList.test.tsx`:

```typescript
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import "@testing-library/jest-dom";
import MaterialContainerList from "../MaterialContainerList";
import * as useMaterialContainersHooks from "../../../api/hooks/useMaterialContainers";

jest.mock("../../../api/hooks/useMaterialContainers");
jest.mock("../../../telemetry/useScreenView", () => ({
  useScreenView: jest.fn(),
}));

const mockHooks = useMaterialContainersHooks as jest.Mocked<
  typeof useMaterialContainersHooks
>;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>{children}</BrowserRouter>
    </QueryClientProvider>
  );
};

const sampleResponse = {
  success: true,
  containers: [
    {
      id: 1,
      code: "M00001234",
      materialCode: "MAT001",
      lotCode: "LOT-2026-04",
      amount: 25,
      unit: "kg",
      createdAt: new Date("2026-04-01T10:00:00Z"),
      createdBy: "user@anela.cz",
    },
  ],
  totalCount: 1,
  pageNumber: 1,
  pageSize: 20,
} as any;

describe("MaterialContainerList", () => {
  const mockUseList = jest.fn();
  const mockRefetch = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
    (mockHooks.useMaterialContainersList as jest.Mock) = mockUseList;
    mockUseList.mockReturnValue({
      data: sampleResponse,
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    });
  });

  it("renders a row for each container", () => {
    render(<MaterialContainerList />, { wrapper: createWrapper });
    expect(screen.getByText("M00001234")).toBeInTheDocument();
    expect(screen.getByText("MAT001")).toBeInTheDocument();
    expect(screen.getByText("LOT-2026-04")).toBeInTheDocument();
    expect(screen.getByText("user@anela.cz")).toBeInTheDocument();
  });

  it("shows a loading indicator while fetching", () => {
    mockUseList.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
      refetch: mockRefetch,
    });
    render(<MaterialContainerList />, { wrapper: createWrapper });
    expect(screen.getByText(/Načítání/i)).toBeInTheDocument();
  });

  it("shows an error message when the query fails", () => {
    mockUseList.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error("boom"),
      refetch: mockRefetch,
    });
    render(<MaterialContainerList />, { wrapper: createWrapper });
    expect(screen.getByText(/Chyba/i)).toBeInTheDocument();
  });

  it("shows an empty state when there are no containers", () => {
    mockUseList.mockReturnValue({
      data: { success: true, containers: [], totalCount: 0, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    });
    render(<MaterialContainerList />, { wrapper: createWrapper });
    expect(screen.getByText(/Žádné kontejnery/i)).toBeInTheDocument();
  });

  it("applies the material filter and resets to page 1", async () => {
    render(<MaterialContainerList />, { wrapper: createWrapper });

    fireEvent.change(screen.getByPlaceholderText("Materiál"), {
      target: { value: "MAT001" },
    });
    fireEvent.click(screen.getByText("Filtrovat"));

    await waitFor(() =>
      expect(mockUseList).toHaveBeenCalledWith(
        expect.objectContaining({ materialCode: "MAT001", page: 1 }),
      ),
    );
  });
});
```

- [ ] **Step 2: Run the test to confirm it fails**

Run: `cd frontend && npx jest src/components/pages/__tests__/MaterialContainerList.test.tsx`
Expected: FAIL — cannot find module `../MaterialContainerList`.

- [ ] **Step 3: Implement the page**

Create `frontend/src/components/pages/MaterialContainerList.tsx`:

```typescript
import React, { useState } from "react";
import { Search, Filter, AlertCircle, Loader2, ChevronLeft, ChevronRight } from "lucide-react";
import {
  useMaterialContainersList,
  MaterialContainersListRequest,
} from "../../api/hooks/useMaterialContainers";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import { useScreenView } from "../../telemetry/useScreenView";

const formatDate = (date: Date | string | undefined): string => {
  if (!date) return "-";
  const dateObj = typeof date === "string" ? new Date(date) : date;
  return dateObj.toLocaleString("cs-CZ");
};

const formatAmount = (amount?: number, unit?: string): string => {
  if (amount === undefined || amount === null) return "-";
  return unit ? `${amount} ${unit}` : `${amount}`;
};

const MaterialContainerList: React.FC = () => {
  // Filter input state (what the user is typing)
  const [materialInput, setMaterialInput] = useState("");
  const [lotInput, setLotInput] = useState("");
  const [codeInput, setCodeInput] = useState("");

  // Applied filter state (what the query uses)
  const [materialFilter, setMaterialFilter] = useState("");
  const [lotFilter, setLotFilter] = useState("");
  const [codeFilter, setCodeFilter] = useState("");

  // Pagination state
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  useScreenView("Manufacturing", "MaterialContainers");

  const request: MaterialContainersListRequest = {
    materialCode: materialFilter || undefined,
    lotCode: lotFilter || undefined,
    code: codeFilter || undefined,
    page: pageNumber,
    pageSize,
  };

  const { data, isLoading: loading, error, refetch } = useMaterialContainersList(request);

  const containers = data?.containers || [];
  const totalCount = data?.totalCount || 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const isFiltered = Boolean(materialFilter || lotFilter || codeFilter);

  const handleApplyFilters = async () => {
    setMaterialFilter(materialInput);
    setLotFilter(lotInput);
    setCodeFilter(codeInput);
    setPageNumber(1);
    await refetch();
  };

  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "Enter") {
      handleApplyFilters();
    }
  };

  const handleClearFilters = async () => {
    setMaterialInput("");
    setLotInput("");
    setCodeInput("");
    setMaterialFilter("");
    setLotFilter("");
    setCodeFilter("");
    setPageNumber(1);
    await refetch();
  };

  const handlePageChange = (newPage: number) => {
    if (newPage >= 1 && newPage <= totalPages) {
      setPageNumber(newPage);
    }
  };

  const handlePageSizeChange = (newPageSize: number) => {
    setPageSize(newPageSize);
    setPageNumber(1);
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání šarží...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání šarží: {(error as Error).message}</div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      {/* Header */}
      <div className="flex-shrink-0 mb-3 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-gray-900">Šarže</h1>
      </div>

      {/* Filters */}
      <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 mr-2" />
              <span className="text-sm font-medium text-gray-900">Filtry:</span>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400" />
                </div>
                <input
                  type="text"
                  value={materialInput}
                  onChange={(e) => setMaterialInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                  placeholder="Materiál"
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <input
                type="text"
                value={lotInput}
                onChange={(e) => setLotInput(e.target.value)}
                onKeyDown={handleKeyDown}
                className="focus:ring-indigo-500 focus:border-indigo-500 block w-full px-3 py-2 sm:text-sm border-gray-300 rounded-md"
                placeholder="Šarže"
              />
            </div>

            <div className="flex-1 max-w-xs">
              <input
                type="text"
                value={codeInput}
                onChange={(e) => setCodeInput(e.target.value)}
                onKeyDown={handleKeyDown}
                className="focus:ring-indigo-500 focus:border-indigo-500 block w-full px-3 py-2 sm:text-sm border-gray-300 rounded-md"
                placeholder="Kód kontejneru"
              />
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={handleApplyFilters}
              className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm"
            >
              Filtrovat
            </button>
            <button
              onClick={handleClearFilters}
              className="bg-gray-500 hover:bg-gray-600 text-white font-medium py-2 px-3 rounded-md transition-colors duration-200 text-sm"
            >
              Vymazat
            </button>
          </div>
        </div>
      </div>

      {/* Table */}
      <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
        <div className="flex-1 overflow-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50 sticky top-0 z-10">
              <tr>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Kód kontejneru</th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Materiál</th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Šarže</th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Množství</th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Vytvořeno</th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Kdo</th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {containers.map((container) => (
                <tr key={container.id} className="hover:bg-gray-50 transition-colors duration-150">
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">{container.code}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{container.materialCode}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">{container.lotCode}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatAmount(container.amount, container.unit)}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatDate(container.createdAt)}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{container.createdBy}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {containers.length === 0 && (
            <div className="text-center py-8">
              <p className="text-gray-500">Žádné kontejnery nebyly nalezeny.</p>
            </div>
          )}
        </div>
      </div>

      {/* Pagination */}
      {totalCount > 0 && (
        <div className="flex-shrink-0 bg-white px-3 py-2 flex items-center justify-between border-t border-gray-200 text-xs">
          <div className="flex items-center space-x-3">
            <p className="text-xs text-gray-600">
              {Math.min((pageNumber - 1) * pageSize + 1, totalCount)}-
              {Math.min(pageNumber * pageSize, totalCount)} z {totalCount}
              {isFiltered ? <span className="text-gray-500"> (filtrováno)</span> : ""}
            </p>
            <div className="flex items-center space-x-1">
              <span className="text-xs text-gray-600">Zobrazit:</span>
              <select
                value={pageSize}
                onChange={(e) => handlePageSizeChange(Number(e.target.value))}
                className="border border-gray-300 rounded px-1 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
              >
                <option value={20}>20</option>
                <option value={50}>50</option>
                <option value={100}>100</option>
              </select>
            </div>
          </div>
          <nav className="relative z-0 inline-flex rounded shadow-sm -space-x-px" aria-label="Pagination">
            <button
              onClick={() => handlePageChange(pageNumber - 1)}
              disabled={pageNumber <= 1}
              className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <ChevronLeft className="h-3 w-3" />
            </button>
            <span className="relative inline-flex items-center px-2 py-1 border border-gray-300 bg-white text-xs font-medium text-gray-700">
              {pageNumber} / {totalPages}
            </span>
            <button
              onClick={() => handlePageChange(pageNumber + 1)}
              disabled={pageNumber >= totalPages}
              className="relative inline-flex items-center px-1 py-1 rounded-r border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <ChevronRight className="h-3 w-3" />
            </button>
          </nav>
        </div>
      )}
    </div>
  );
};

export default MaterialContainerList;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npx jest src/components/pages/__tests__/MaterialContainerList.test.tsx`
Expected: all 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/MaterialContainerList.tsx frontend/src/components/pages/__tests__/MaterialContainerList.test.tsx
git commit -m "feat: add read-only MaterialContainerList page"
```

---

### Task 5: Wire the route and the "Šarže" nav item

**Files:**
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/components/Layout/Sidebar.tsx`

- [ ] **Step 1: Import the page in `App.tsx`**

Add this import alongside the other page imports near the top of `App.tsx` (the file uses direct imports, not lazy loading — match that). Place it next to the existing `import PurchaseOrderList from "./components/pages/PurchaseOrderList";` line:

```typescript
import MaterialContainerList from "./components/pages/MaterialContainerList";
```

- [ ] **Step 2: Register the route**

In `App.tsx`, add this route next to the other `/manufacturing/*` routes (e.g. directly after the `<Route path="/manufacturing/product-inventory" ... />` line):

```tsx
<Route path="/manufacturing/material-containers" element={<MaterialContainerList />} />
```

- [ ] **Step 3: Add the "Šarže" nav item to the Výroba section**

In `Sidebar.tsx`, inside the `vyroba` section's `items` array, add a new entry after the `sklad-vyroby` item (the last item in that section, `href: "/manufacturing/product-inventory"`):

```typescript
        {
          id: "sarze",
          name: "Šarže",
          href: "/manufacturing/material-containers",
        },
```

- [ ] **Step 4: Build and lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: build succeeds, lint passes (no `console.log`, no unused imports).

- [ ] **Step 5: Run the full frontend test suite for touched files**

Run: `cd frontend && npx jest src/components/pages/__tests__/MaterialContainerList.test.tsx src/api/hooks/__tests__/useMaterialContainers.test.ts`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/App.tsx frontend/src/components/Layout/Sidebar.tsx
git commit -m "feat: add Šarže nav item and route for material container audit page"
```

---

## Final Verification

- [ ] **Backend:** `cd backend && dotnet build && dotnet test --no-build && dotnet format --verify-no-changes`
- [ ] **Frontend:** `cd frontend && npm run build && npm run lint && npx jest src/components/pages/__tests__/MaterialContainerList.test.tsx src/api/hooks/__tests__/useMaterialContainers.test.ts`
- [ ] **Manual smoke (optional):** run the app, open the Výroba → Šarže menu item, confirm the table loads, filters apply, and pagination works.

## Notes for the implementer

- **Immutability / read-only:** this page never mutates data. Do not add discard/create actions — they are explicitly out of scope.
- **No sortable headers:** ordering is fixed newest-first (the server orders by Id descending). Adding sortable columns would require a backend sort param and is out of scope.
- **`totalPages` is computed client-side** (`Math.ceil(totalCount / pageSize)`) — the list response only returns `totalCount`, `pageNumber`, `pageSize`.
- **Generated client is auto-generated** on `dotnet build`; never hand-edit `frontend/src/api/generated/api-client.ts`.
