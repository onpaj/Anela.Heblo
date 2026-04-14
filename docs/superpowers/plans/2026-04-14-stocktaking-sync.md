# StockTaking Synchronous Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the async Hangfire-based StockTaking flow with a direct synchronous API call, eliminating job tracking, polling, and background processing.

**Architecture:** The synchronous `SubmitStockTakingHandler` (POST `/api/stock-taking/submit`) already exists and works correctly. The async flow (`EnqueueStockTaking` → Hangfire → `ProcessStockTaking`) is removed entirely. The frontend `InventoryModal` switches from `useEnqueueStockTaking` to the existing `useSubmitStockTaking` hook. `StockTakingJobStatusTracker` component and all job-polling infrastructure are deleted.

**Tech Stack:** .NET 8 / MediatR (backend), React + React Query (frontend). Branch: `feature/merge-stock-clients`.

---

## Files Changed

**Backend — delete entirely:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/EnqueueStockTaking/EnqueueStockTakingHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/EnqueueStockTaking/EnqueueStockTakingRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/EnqueueStockTaking/EnqueueStockTakingResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/ProcessStockTaking/ProcessStockTakingHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/ProcessStockTaking/ProcessStockTakingRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/ProcessStockTaking/ProcessStockTakingResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockTakingJobStatus/GetStockTakingJobStatusHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockTakingJobStatus/GetStockTakingJobStatusRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockTakingJobStatus/GetStockTakingJobStatusResponse.cs`

**Backend — modify:**
- `backend/src/Anela.Heblo.API/Controllers/CatalogController.cs` — remove `EnqueueStockTaking` and `GetStockTakingJobStatus` actions + their using imports

**Frontend — delete entirely:**
- `frontend/src/components/inventory/StockTakingJobStatusTracker.tsx`

**Frontend — modify:**
- `frontend/src/api/hooks/useStockTaking.ts` — remove `useEnqueueStockTaking`, `useStockTakingJobStatus`, `AsyncStockTakingRequest`, and the two private API functions that back them
- `frontend/src/components/inventory/InventoryModal.tsx` — use `useSubmitStockTaking` instead of `useEnqueueStockTaking`; remove `onJobEnqueued` prop; update loading/button/toast text
- `frontend/src/components/pages/InventoryList.tsx` — remove `activeStockTakingJobs` state, job tracker import/render, `handleStockTakingJobEnqueued`, `handleStockTakingJobCompleted`, `onJobEnqueued` prop on `InventoryModal`

**Frontend tests — modify:**
- `frontend/src/components/inventory/__tests__/InventoryModal.test.tsx` — remove `useEnqueueStockTaking` mock, ensure tests pass
- `frontend/src/components/pages/__tests__/InventoryList.test.tsx` — remove any async job-related assertions

---

## Task 1: Checkout branch and remove backend async use cases

**Files:**
- Delete: 9 files in `EnqueueStockTaking/`, `ProcessStockTaking/`, `GetStockTakingJobStatus/` folders

- [ ] **Step 1: Checkout the feature branch**
```bash
git checkout feature/merge-stock-clients
```

- [ ] **Step 2: Delete the three use case folders**
```bash
rm -rf backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/EnqueueStockTaking
rm -rf backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/ProcessStockTaking
rm -rf backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockTakingJobStatus
```

- [ ] **Step 3: Remove endpoints from CatalogController**

In `backend/src/Anela.Heblo.API/Controllers/CatalogController.cs`, remove:
- The `using` for `EnqueueStockTaking` (line 12): `using Anela.Heblo.Application.Features.Catalog.UseCases.EnqueueStockTaking;`
- Also remove the using for `GetStockTakingJobStatus` if present
- Remove these two action methods (lines ~177–197):

```csharp
    [HttpPost("stock-taking/enqueue")]
    [ProducesResponseType(typeof(EnqueueStockTakingResponse), 202)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<EnqueueStockTakingResponse>> EnqueueStockTaking(
        [FromBody] EnqueueStockTakingRequest request)
    {
        var response = await _mediator.Send(request);
        return Accepted(response);
    }

    [HttpGet("stock-taking/job-status/{jobId}")]
    [ProducesResponseType(typeof(GetStockTakingJobStatusResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<GetStockTakingJobStatusResponse>> GetStockTakingJobStatus(string jobId)
    {
        var request = new GetStockTakingJobStatusRequest { JobId = jobId };
        var response = await _mediator.Send(request);
        return Ok(response);
    }
```

- [ ] **Step 4: Verify backend builds**
```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -q
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Run backend tests**
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q
```
Expected: all tests pass (any test referencing removed handlers will need fixing — see Task 2 if failures occur)

- [ ] **Step 6: Commit**
```bash
git add -A
git commit -m "refactor(stock): remove async Hangfire StockTaking use cases and endpoints"
```

---

## Task 2: Update frontend hooks — remove async stock taking

**Files:**
- Modify: `frontend/src/api/hooks/useStockTaking.ts`

- [ ] **Step 1: Remove async-related exports and private functions**

Replace the file content from line 19 to end with only the synchronous hooks. The final file should be:

```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { SubmitStockTakingRequest, SubmitStockTakingResponse, GetStockTakingHistoryResponse } from "../generated/api-client";

export interface StockTakingSubmitRequest {
  productCode: string;
  targetAmount: number;
  softStockTaking?: boolean;
}

export interface StockTakingHistoryRequest {
  productCode?: string;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

// API function to submit stock taking
const submitStockTaking = async (request: StockTakingSubmitRequest): Promise<SubmitStockTakingResponse> => {
  const apiClient = getAuthenticatedApiClient();

  const submitRequest = new SubmitStockTakingRequest({
    productCode: request.productCode,
    targetAmount: request.targetAmount,
    softStockTaking: request.softStockTaking ?? false,
  });

  return await apiClient.stockTaking_SubmitStockTaking(submitRequest);
};

// React Query mutation hook for stock taking submission
export const useSubmitStockTaking = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: submitStockTaking,
    onSuccess: (data, variables) => {
      if (!variables.softStockTaking) {
        queryClient.setQueryData(
          [...QUERY_KEYS.catalog, "detail", variables.productCode, 1],
          (oldData: any) => {
            if (oldData?.item?.stock) {
              return {
                ...oldData,
                item: {
                  ...oldData.item,
                  stock: {
                    ...oldData.item.stock,
                    available: variables.targetAmount,
                    eshop: variables.targetAmount,
                  },
                },
              };
            }
            return oldData;
          }
        );

        queryClient.setQueriesData(
          { queryKey: [...QUERY_KEYS.catalog, "inventory"] },
          (oldData: any) => {
            if (oldData?.items) {
              return {
                ...oldData,
                items: oldData.items.map((item: any) => {
                  if (item.productCode === variables.productCode) {
                    return {
                      ...item,
                      stock: {
                        ...item.stock,
                        available: variables.targetAmount,
                        eshop: variables.targetAmount,
                      },
                    };
                  }
                  return item;
                }),
              };
            }
            return oldData;
          }
        );
      }

      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.catalog] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.catalog, "detail", variables.productCode] });
      queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.catalog, "inventory"] });
    },
    onError: (error, variables) => {
      console.error("Stock taking submission failed:", error, "for product:", variables.productCode);
    },
  });
};

// API function to get stock taking history
const getStockTakingHistory = async (request: StockTakingHistoryRequest): Promise<GetStockTakingHistoryResponse> => {
  const apiClient = getAuthenticatedApiClient();

  return await apiClient.stockTaking_GetStockTakingHistory(
    request.productCode || undefined,
    request.pageNumber || 1,
    request.pageSize || 20,
    request.sortBy || "date",
    request.sortDescending ?? true
  );
};

// React Query hook for stock taking history
export const useStockTakingHistory = (request: StockTakingHistoryRequest) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.stockTaking, "history", request],
    queryFn: () => getStockTakingHistory(request),
    enabled: !!request.productCode,
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};
```

Note: `softStockTaking` default changed from `true` to `false` — the modal passes the actual computed value anyway.

Note: `useSubmitStockTaking` now also updates the inventory list cache optimistically (ported from `useEnqueueStockTaking`).

- [ ] **Step 2: Run frontend lint**
```bash
cd frontend && npm run lint 2>&1 | grep -E "error|warning" | head -20
```
Expected: no errors referencing `useStockTaking.ts`

---

## Task 3: Update InventoryModal — switch to synchronous

**Files:**
- Modify: `frontend/src/components/inventory/InventoryModal.tsx`

- [ ] **Step 1: Update imports**

Change line 4 from:
```typescript
import { useEnqueueStockTaking, useStockTakingHistory } from "../../api/hooks/useStockTaking";
```
to:
```typescript
import { useSubmitStockTaking, useStockTakingHistory } from "../../api/hooks/useStockTaking";
```

- [ ] **Step 2: Remove `onJobEnqueued` from props interface and component signature**

Change:
```typescript
interface InventoryModalProps {
  item: CatalogItemDto | null;
  isOpen: boolean;
  onClose: () => void;
  onJobEnqueued?: (jobId: string, productCode: string) => void;
}

const InventoryModal: React.FC<InventoryModalProps> = ({
  item,
  isOpen,
  onClose,
  onJobEnqueued,
}) => {
```
to:
```typescript
interface InventoryModalProps {
  item: CatalogItemDto | null;
  isOpen: boolean;
  onClose: () => void;
}

const InventoryModal: React.FC<InventoryModalProps> = ({
  item,
  isOpen,
  onClose,
}) => {
```

- [ ] **Step 3: Replace `useEnqueueStockTaking` with `useSubmitStockTaking`**

Change:
```typescript
  const enqueueStockTaking = useEnqueueStockTaking();
```
to:
```typescript
  const submitStockTaking = useSubmitStockTaking();
```

- [ ] **Step 4: Update `useEffect` reset**

Change:
```typescript
  useEffect(() => {
    if (isOpen) {
      enqueueStockTaking.reset();
    }
  }, [isOpen, enqueueStockTaking]);
```
to:
```typescript
  useEffect(() => {
    if (isOpen) {
      submitStockTaking.reset();
    }
  }, [isOpen, submitStockTaking]);
```

- [ ] **Step 5: Replace `handleInventorize` implementation**

Replace the entire `handleInventorize` function with:
```typescript
  const handleInventorize = async () => {
    if (!effectiveItem?.productCode) return;

    const currentStock = Math.round((effectiveItem?.stock?.eshop || 0) * 100) / 100;
    const isSoftStockTaking = newQuantity === currentStock;

    try {
      await submitStockTaking.mutateAsync({
        productCode: effectiveItem.productCode,
        targetAmount: newQuantity,
        softStockTaking: isSoftStockTaking,
      });

      showInfo(
        "Inventarizace dokončena",
        `Inventarizace produktu ${effectiveItem.productCode} byla úspěšně provedena.`,
        { duration: 3000 }
      );

      onClose();
    } catch (error) {
      console.error("Stock taking failed:", error);
    }
  };
```

- [ ] **Step 6: Update button and error references in JSX**

Replace all occurrences of `enqueueStockTaking` in the JSX (disabled condition, isPending, error display) with `submitStockTaking`:

```tsx
// Button disabled condition (line ~317):
disabled={submitStockTaking.isPending || !effectiveItem?.productCode}

// Button loading state (line ~319):
{submitStockTaking.isPending ? (
  <>
    <Loader2 className="h-5 w-5 animate-spin" />
    <span>Ukládám...</span>
  </>
) : (
  <span>Zinventarizovat</span>
)}

// Error display (line ~331):
{submitStockTaking.error && (
  <div className="mt-4 p-3 bg-red-50 rounded-lg border border-red-200 flex items-start space-x-2">
    <AlertCircle className="h-5 w-5 text-red-600 mt-0.5 flex-shrink-0" />
    <div>
      <div className="text-sm font-medium text-red-800">
        Chyba při inventarizaci
      </div>
      <div className="text-sm text-red-700 mt-1">
        {submitStockTaking.error?.message || "Došlo k neočekávané chybě"}
      </div>
    </div>
  </div>
)}
```

- [ ] **Step 7: Run lint**
```bash
cd frontend && npm run lint 2>&1 | grep -E "error" | grep -i "inventorymodal\|useStockTaking" | head -10
```
Expected: no errors

---

## Task 4: Update InventoryList — remove job tracking

**Files:**
- Modify: `frontend/src/components/pages/InventoryList.tsx`
- Delete: `frontend/src/components/inventory/StockTakingJobStatusTracker.tsx`

- [ ] **Step 1: Delete StockTakingJobStatusTracker**
```bash
rm frontend/src/components/inventory/StockTakingJobStatusTracker.tsx
```

- [ ] **Step 2: Remove import from InventoryList.tsx**

Remove line 21:
```typescript
import StockTakingJobStatusTracker from "../inventory/StockTakingJobStatusTracker";
```

- [ ] **Step 3: Remove `activeStockTakingJobs` state (line 69)**

Remove:
```typescript
  const [activeStockTakingJobs, setActiveStockTakingJobs] = useState<Array<{jobId: string, productCode: string}>>([]);
```

- [ ] **Step 4: Remove job handler callbacks (lines ~213–226)**

Remove these two functions entirely:
```typescript
  const handleStockTakingJobEnqueued = useCallback((jobId: string, productCode: string) => {
    ...
  }, [...]);

  const handleStockTakingJobCompleted = useCallback((jobId: string) => {
    ...
  }, [...]);
```

- [ ] **Step 5: Remove StockTakingJobStatusTracker render block (lines ~319–332)**

Remove the entire block:
```tsx
      {activeStockTakingJobs.length > 0 && (
        ...
        {activeStockTakingJobs.map(job => (
          <StockTakingJobStatusTracker ... />
        ))}
        ...
      )}
```

- [ ] **Step 6: Remove `onJobEnqueued` prop from InventoryModal usage (line ~679)**

Change:
```tsx
        onJobEnqueued={handleStockTakingJobEnqueued}
```
Remove this line entirely.

- [ ] **Step 7: Remove unused `useCallback` import if nothing else uses it**

Check: `grep -n "useCallback" frontend/src/components/pages/InventoryList.tsx`

If `useCallback` is no longer used, remove it from the React import on line 1.

- [ ] **Step 8: Run lint**
```bash
cd frontend && npm run lint 2>&1 | grep -E "error" | head -10
```
Expected: no errors

---

## Task 5: Update frontend tests

**Files:**
- Modify: `frontend/src/components/inventory/__tests__/InventoryModal.test.tsx`
- Modify: `frontend/src/components/pages/__tests__/InventoryList.test.tsx`

- [ ] **Step 1: Update InventoryModal.test.tsx mock**

The mock at lines 8–27 mocks both `useSubmitStockTaking` and `useEnqueueStockTaking`. Remove `useEnqueueStockTaking` from the mock:

```typescript
jest.mock('../../../api/hooks/useStockTaking', () => ({
  useSubmitStockTaking: () => ({
    mutate: jest.fn(),
    mutateAsync: jest.fn().mockResolvedValue({}),
    isPending: false,
    isError: false,
    error: null,
    isSuccess: false,
    reset: jest.fn(),
  }),
  useStockTakingHistory: (request: any) => ({
    data: request.productCode === 'TEST-PRODUCT' ? {
      items: [
        {
          date: '2024-01-15T10:30:00Z',
          amountOld: 10,
          amountNew: 15,
          difference: 5,
          user: 'user1'
        },
        {
          date: '2024-01-10T14:20:00Z',
          amountOld: 5,
          amountNew: 10,
          difference: 5,
          user: 'user2'
        }
      ],
      totalCount: 2,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 1
    } : {
      items: [],
      totalCount: 0,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 0
    },
    isLoading: false,
    isError: false,
    error: null,
  }),
}));
```

Also update any test that checks for "Zařazuji do fronty..." to instead check for "Ukládám..." (the new loading text).

- [ ] **Step 2: Fix InventoryList.test.tsx if needed**

Run tests first to see if any fail:
```bash
cd frontend && npx jest --testPathPattern="InventoryList" --no-coverage 2>&1 | tail -20
```

If tests reference `onJobEnqueued`, `handleStockTakingJobEnqueued`, or `StockTakingJobStatusTracker`, remove those references. The test should not pass `onJobEnqueued` to `InventoryModal`.

- [ ] **Step 3: Run all frontend tests**
```bash
cd frontend && npm test -- --watchAll=false --passWithNoTests 2>&1 | tail -10
```
Expected: all tests pass

- [ ] **Step 4: Commit**
```bash
git add -A
git commit -m "feat(stock): make StockTaking synchronous — remove async Hangfire flow @claude"
```

---

## Verification

- [ ] `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -q` — 0 errors
- [ ] `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q` — all pass
- [ ] `cd frontend && npm test -- --watchAll=false` — all pass
- [ ] `cd frontend && npm run lint` — no errors
- [ ] Manual: Open InventoryList → click Inventarizovat → confirm spinner shows and modal closes on completion (no job tracker appears)
