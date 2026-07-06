### task: replace-http-fetch-with-generated-client-method

**Context**

`frontend/src/api/hooks/useFinancialOverview.ts` currently reads (full current contents, 45 lines):

```typescript
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  GetFinancialOverviewResponse,
  MonthlyFinancialDataDto,
  FinancialSummaryDto,
  StockChangeDto,
  StockSummaryDto,
} from "../generated/api-client";

// Re-export the generated types for convenience
export {
  GetFinancialOverviewResponse,
  MonthlyFinancialDataDto,
  FinancialSummaryDto,
  StockChangeDto,
  StockSummaryDto,
};

export const useFinancialOverviewQuery = (
  months: number = 6,
  includeStockData: boolean = true,
  excludedDepartments: string[] = [],
  includeCurrentMonth: boolean = false,
) => {
  return useQuery<GetFinancialOverviewResponse, Error>({
    queryKey: [...QUERY_KEYS.financialOverview, months, includeStockData, excludedDepartments, includeCurrentMonth],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const params = new URLSearchParams();
      params.set('months', String(months));
      params.set('includeStockData', String(includeStockData));
      params.set('includeCurrentMonth', String(includeCurrentMonth));
      excludedDepartments.forEach(d => params.append('excludedDepartments', d));
      const relativeUrl = `/api/FinancialOverview?${params.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
      if (!response.ok) throw new Error(`Failed to fetch financial overview: ${response.statusText}`);
      return await response.json() as GetFinancialOverviewResponse;
    },
    staleTime: 5 * 60 * 1000, // Consider data stale after 5 minutes
    gcTime: 10 * 60 * 1000, // Keep cache for 10 minutes
  });
};
```

The generated client already contains the exact method needed, unmodified by this task (`frontend/src/api/generated/api-client.ts:3809`):

```typescript
financialOverview_GetFinancialOverview(months: number | null | undefined, includeStockData: boolean | undefined, excludedDepartments: string[] | null | undefined, includeCurrentMonth: boolean | undefined): Promise<GetFinancialOverviewResponse>
```

It builds the identical query string, calls `this.http.fetch` (the same authenticated fetcher wired by `getAuthenticatedApiClient()`), and resolves a typed `GetFinancialOverviewResponse` via `.fromJS(...)` on 200/204, or throws a `SwaggerException` (which `extends Error`) via `throwException(...)` on other statuses.

**Step 1 — Edit the hook**

In `frontend/src/api/hooks/useFinancialOverview.ts`, replace only the `queryFn` body. Everything else in the file (imports, re-exports, hook signature, `queryKey`, `staleTime`, `gcTime`) stays exactly as-is. Apply this edit:

Old `queryFn` block to remove:
```typescript
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const params = new URLSearchParams();
      params.set('months', String(months));
      params.set('includeStockData', String(includeStockData));
      params.set('includeCurrentMonth', String(includeCurrentMonth));
      excludedDepartments.forEach(d => params.append('excludedDepartments', d));
      const relativeUrl = `/api/FinancialOverview?${params.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
      if (!response.ok) throw new Error(`Failed to fetch financial overview: ${response.statusText}`);
      return await response.json() as GetFinancialOverviewResponse;
    },
```

New `queryFn` block:
```typescript
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return await apiClient.financialOverview_GetFinancialOverview(
        months,
        includeStockData,
        excludedDepartments,
        includeCurrentMonth,
      );
    },
```

Do **not** add `await` before `getAuthenticatedApiClient()` — it is synchronous (`frontend/src/api/client.ts:276`, returns `ApiClient` not `Promise<ApiClient>`); the existing no-`await` call is already correct and must be preserved.

After this edit, the full resulting file must be:

```typescript
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  GetFinancialOverviewResponse,
  MonthlyFinancialDataDto,
  FinancialSummaryDto,
  StockChangeDto,
  StockSummaryDto,
} from "../generated/api-client";

// Re-export the generated types for convenience
export {
  GetFinancialOverviewResponse,
  MonthlyFinancialDataDto,
  FinancialSummaryDto,
  StockChangeDto,
  StockSummaryDto,
};

export const useFinancialOverviewQuery = (
  months: number = 6,
  includeStockData: boolean = true,
  excludedDepartments: string[] = [],
  includeCurrentMonth: boolean = false,
) => {
  return useQuery<GetFinancialOverviewResponse, Error>({
    queryKey: [...QUERY_KEYS.financialOverview, months, includeStockData, excludedDepartments, includeCurrentMonth],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return await apiClient.financialOverview_GetFinancialOverview(
        months,
        includeStockData,
        excludedDepartments,
        includeCurrentMonth,
      );
    },
    staleTime: 5 * 60 * 1000, // Consider data stale after 5 minutes
    gcTime: 10 * 60 * 1000, // Keep cache for 10 minutes
  });
};
```

**Step 2 — Add a unit test for the hook**

No test file currently exists for `useFinancialOverview.ts` (verified: `frontend/src/api/hooks/__tests__/` has no `useFinancialOverview*` entry). Create `frontend/src/api/hooks/__tests__/useFinancialOverview.test.ts`, following this repo's established convention for hook tests that mock `getAuthenticatedApiClient` (see sibling `frontend/src/api/hooks/__tests__/useJournal.simple.test.ts` for the pattern this mirrors — `renderHook` + `QueryClientProvider` wrapper + mocked client module). Note `getAuthenticatedApiClient()` is synchronous in the real implementation, so the mock must use `mockReturnValue` (not `mockResolvedValue`) to match actual call-site behavior (`const apiClient = getAuthenticatedApiClient();` with no `await`).

Write this exact file content:

```typescript
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useFinancialOverviewQuery } from "../useFinancialOverview";
import * as clientModule from "../../client";

// Mock the client module
jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    financialOverview: ["financialOverview"],
  },
}));

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        gcTime: 0,
      },
    },
  });

  return ({ children }: { children: React.ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe("useFinancialOverviewQuery", () => {
  const mockGetAuthenticatedApiClient =
    clientModule.getAuthenticatedApiClient as jest.MockedFunction<
      typeof clientModule.getAuthenticatedApiClient
    >;

  const mockFinancialOverviewGet = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();

    mockFinancialOverviewGet.mockResolvedValue({
      months: [],
      summary: undefined,
    });

    mockGetAuthenticatedApiClient.mockReturnValue({
      financialOverview_GetFinancialOverview: mockFinancialOverviewGet,
    } as any);
  });

  test("calls the generated client method with default parameters", async () => {
    const { result } = renderHook(() => useFinancialOverviewQuery(), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFinancialOverviewGet).toHaveBeenCalledTimes(1);
    expect(mockFinancialOverviewGet).toHaveBeenCalledWith(6, true, [], false);
  });

  test("calls the generated client method with explicit parameters, including excludedDepartments", async () => {
    const { result } = renderHook(
      () => useFinancialOverviewQuery(12, false, ["Sales", "Marketing"], true),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFinancialOverviewGet).toHaveBeenCalledTimes(1);
    expect(mockFinancialOverviewGet).toHaveBeenCalledWith(
      12,
      false,
      ["Sales", "Marketing"],
      true,
    );
  });

  test("does not use (apiClient as any).http.fetch or manual URLSearchParams", async () => {
    const { result } = renderHook(() => useFinancialOverviewQuery(), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Only the typed generated method should have been touched on the mocked client.
    const clientArg = mockGetAuthenticatedApiClient.mock.results[0].value;
    expect(clientArg.financialOverview_GetFinancialOverview).toBe(mockFinancialOverviewGet);
  });

  test("surfaces an Error from the generated client method as the query error", async () => {
    mockFinancialOverviewGet.mockReset();
    mockFinancialOverviewGet.mockRejectedValue(new Error("Request failed with status code 500"));

    const { result } = renderHook(() => useFinancialOverviewQuery(), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.error).toBeInstanceOf(Error);
    expect(result.current.error?.message).toBe("Request failed with status code 500");
  });
});
```

**Step 3 — Validate**

Run, from the `frontend/` directory:

```bash
npm run build
npm run lint
npx jest src/api/hooks/__tests__/useFinancialOverview.test.ts
```

Expected results:
- `npm run build` completes with no TypeScript errors (in particular, no error about `useQuery<GetFinancialOverviewResponse, Error>` not being satisfied, and no leftover reference to `URLSearchParams` or `as any` in `useFinancialOverview.ts`).
- `npm run lint` passes with no new warnings/errors introduced by this change.
- The new Jest test file passes (all 4 test cases green).

**Acceptance checks (map to spec FR-1/FR-2/FR-3)**

- `frontend/src/api/hooks/useFinancialOverview.ts` contains no `as any` anywhere (verify: `grep -n "as any" frontend/src/api/hooks/useFinancialOverview.ts` returns nothing).
- `frontend/src/api/hooks/useFinancialOverview.ts` contains no `URLSearchParams` and no `.http.fetch` (verify: `grep -nE "URLSearchParams|\.http\.fetch" frontend/src/api/hooks/useFinancialOverview.ts` returns nothing).
- `useFinancialOverviewQuery`'s exported signature, defaults, return type (`UseQueryResult<GetFinancialOverviewResponse, Error>`), `queryKey` shape, `staleTime` (5 min), and `gcTime` (10 min) are byte-identical to before this change — only the `queryFn` body differs.
- The re-exported types (`GetFinancialOverviewResponse`, `MonthlyFinancialDataDto`, `FinancialSummaryDto`, `StockChangeDto`, `StockSummaryDto`) are unchanged.
- Test asserts the generated method is called with positional args `(months, includeStockData, excludedDepartments, includeCurrentMonth)` for both default and non-default inputs, covering FR-2's default case (`6, true, [], false`) and populated-`excludedDepartments` case (`12, false, ["Sales", "Marketing"], true`).
- Test asserts an `Error` thrown by the generated method propagates as `result.current.error` (an `Error` instance with a populated `message`), covering FR-3.

**Do not** touch `ManufacturingStockAnalysis.tsx`, `TransportBoxDetail.tsx`, the backend `FinancialOverviewController`, `frontend/src/api/generated/api-client.ts`, or `frontend/src/api/client.ts` — all explicitly out of scope per the spec.

Once `npm run build`, `npm run lint`, and the new test file all pass, commit the change (hook file + new test file) with a message describing the refactor (e.g. "Replace raw http.fetch bypass in useFinancialOverviewQuery with generated client method").
