# Task Plan: Migrate `useManufacturingStockAnalysis` to the generated OpenAPI client (#3730)

## Goal

`frontend/src/api/hooks/useManufacturingStockAnalysis.ts` hand-declares six types that duplicate
generated ones field-for-field and calls `(apiClient as any).http.fetch(...)` directly with manual
`URLSearchParams` building. `frontend/src/components/pages/ManufacturingStockAnalysis.tsx`'s
`handleExport` duplicates the identical anti-pattern against the same endpoint. The generated
client already has everything needed: `apiClient.manufacturingStockAnalysis_GetStockAnalysis(...)`
(a 15-positional-argument method) plus generated classes/enums that structurally match the
hand-coded types. This plan rewires both call sites onto the generated client and deletes the
hand-coded types. **No backend changes.**

## Architecture summary

No new components. Two existing call sites move from hand-rolled fetch to the generated-client
method already used by every other Manufacture hook (`useManufactureBatch.ts`,
`useManufactureOrders.ts`, `useManufactureSettings.ts`) and by the already-migrated sibling
`usePurchaseStockAnalysis.ts` / `useKnowledgeBase.ts`:

```
ManufacturingStockAnalysis.tsx
        ├── useManufacturingStockAnalysisQuery(filters) ──▶ useManufacturingStockAnalysis.ts
        │         (TanStack Query hook, unchanged shape)        │
        │                                                       ▼
        │                                    apiClient.manufacturingStockAnalysis_GetStockAnalysis(...)
        │                                                       │
        └── handleExport() ─────────────────────────────────────┘
                  (same generated method, isExport=true)
                                                                  ▼
                                                   GET /api/manufacturing-stock-analysis
                                                   (ManufacturingStockAnalysisController — unchanged)
```

## Tech stack

React 18 + TypeScript (strict mode) + TanStack Query v4/v5 + Jest/RTL, `react-scripts` (CRA) for
build/test. Generated client at `frontend/src/api/generated/api-client.ts` (NSwag).

## Global constraints (apply to every task below)

- No backend changes. `git diff` on `backend/` must stay empty.
- No changes to `ManufactureBatchPlanning.tsx`'s import of `calculateTimePeriodRange`, or to
  `ManufacturingStockAnalysis.tsx`'s two existing import *statements* from the hook module (new
  named imports may be *added* to the existing statement, but the import path doesn't change).
- `strict: true` is set in `frontend/tsconfig.json`. The generated `ManufacturingStockItemDto` /
  `ManufacturingStockSummaryDto` classes mark **every** field optional (NSwag's default), unlike
  the hand-coded interfaces they replace, which had most fields required. This is a real
  compile-error source discovered while reading the actual files (not called out explicitly in
  arch-review.r1.md) — Task 1 and Task 2 both contain concrete steps to fix every call site this
  affects. Do not skip these steps or `npm run build` will fail.
- A third call site with the identical `(apiClient as any).http.fetch(...)` anti-pattern exists —
  `ManufacturingStockAnalysis.tsx`'s `handleRowExpand` (product-family subgrid fetch, lines
  ~596–625) — but it is **not** mentioned anywhere in spec.r1.md's Background/FR list, and
  spec.r1.md's Status is COMPLETE. Per this project's "surgical changes" rule, this plan does
  **not** touch `handleRowExpand`. Do not fix it as part of this work — flag it as a candidate for
  a separate follow-up issue instead.

---

### task: rewire-stock-analysis-hook-to-generated-client

**Scope:** `frontend/src/api/hooks/useManufacturingStockAnalysis.ts` and its test file. Implements
FR-1, FR-2, FR-3, FR-4 (re-export decision) from spec.r1.md.

**Files touched:**
- `frontend/src/api/hooks/useManufacturingStockAnalysis.ts` (rewrite)
- `frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx` (rewrite the
  `useManufacturingStockAnalysisQuery` describe block; `calculateTimePeriodRange` and
  `formatWarehouseStock` describe blocks are untouched)

**Design note (reconciling spec.r1.md FR-1 with FR-3):** FR-1's acceptance criteria say every
field of the local request type must reference "the generated `TimePeriod` enum per FR-3" — but
FR-3's own body and its AC3 ("no behavior change to any existing consumer of `TimePeriodFilter`")
make clear the local request's `timePeriod` field must **stay** typed as the app-level
`TimePeriod`/`TimePeriodFilter`, converted only at the call boundary — otherwise
`ManufacturingStockAnalysis.tsx`'s `useState<GetManufacturingStockAnalysisRequest>({ timePeriod:
TimePeriodFilter.Q9M, ... })` initializer would stop compiling, which FR-4's AC1 explicitly
forbids. design.r1.md's Component Design section resolves this unambiguously ("its `timePeriod`
field keeps typing against the app-level `TimePeriodFilter`"). This plan follows design.r1.md:
only `sortBy` types against the generated enum; `timePeriod` stays app-level and is converted at
the boundary.

**Design note (deviation from design.r1.md, in spec's favor):** design.r1.md describes the
`TimePeriod` boundary conversion as living inline inside the hook's `queryFn` only. But
`handleExport` (Task 2) independently calls the same generated method with the same
`filters.timePeriod` value and needs the identical conversion + Q9M-omission logic. Spec FR-3's
AC2 requires "**a single**, clearly-commented conversion point" — with two call sites, the only
way to have one conversion point (not two near-duplicate casts) is to extract it as one shared,
exported function used by both. This also directly protects against the Q9M-omission risk
arch-review.r1.md flags in its risk table ("easy to lose... buried in URL-building code being
deleted wholesale") for **both** call sites, not just the hook's.

#### Step 1: Replace the hook's test file to target the generated client method

Replace the `useManufacturingStockAnalysisQuery` describe block in
`frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx`. This repo already has
an established pattern for this exact migration in `frontend/src/api/hooks/__tests__/useKnowledgeBase.test.ts`
(`mockAuthenticatedApiClient` + `createQueryClientWrapper` from `../../testUtils`) — follow it.

Replace the entire file content with:

```tsx
import { renderHook, waitFor } from "@testing-library/react";
import {
  useManufacturingStockAnalysisQuery,
  TimePeriodFilter,
  ManufacturingStockSortBy,
  calculateTimePeriodRange,
  formatWarehouseStock,
} from "../useManufacturingStockAnalysis";
import {
  mockAuthenticatedApiClient,
  createQueryClientWrapper,
} from "../../testUtils";

jest.mock("../../client");

describe("useManufacturingStockAnalysisQuery", () => {
  let mockClient: { manufacturingStockAnalysis_GetStockAnalysis: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = { manufacturingStockAnalysis_GetStockAnalysis: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  const mockResponse = {
    items: [
      {
        code: "TEST001",
        name: "Test Product",
        currentStock: 100,
        salesInPeriod: 50,
        dailySalesRate: 2.5,
        optimalDaysSetup: 20,
        stockDaysAvailable: 40,
        minimumStock: 10,
        overstockPercentage: 200,
        batchSize: "25",
        productFamily: "TestFamily",
        severity: "Adequate",
        isConfigured: true,
      },
    ],
    summary: {
      totalProducts: 1,
      criticalCount: 0,
      majorCount: 0,
      minorCount: 0,
      adequateCount: 1,
      unconfiguredCount: 0,
      analysisPeriodStart: "2023-01-01T00:00:00Z",
      analysisPeriodEnd: "2023-03-31T23:59:59Z",
      productFamilies: ["TestFamily"],
    },
    totalCount: 1,
    pageNumber: 1,
    pageSize: 20,
  };

  it("calls manufacturingStockAnalysis_GetStockAnalysis with params in exact declared positional order", async () => {
    mockClient.manufacturingStockAnalysis_GetStockAnalysis.mockResolvedValue(
      mockResponse,
    );

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () =>
        useManufacturingStockAnalysisQuery({
          timePeriod: TimePeriodFilter.PreviousQuarter,
          pageNumber: 2,
          pageSize: 10,
          searchTerm: "test",
          criticalItemsOnly: true,
          productFamily: "TestFamily",
          sortBy: ManufacturingStockSortBy.CurrentStock,
          sortDescending: true,
          salesMultiplier: 1.5,
        }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Argument order guards against the positional-transposition risk flagged in
    // arch-review.r1.md's risk table (e.g. swapping pageNumber/pageSize, or the four
    // *ItemsOnly booleans).
    expect(
      mockClient.manufacturingStockAnalysis_GetStockAnalysis,
    ).toHaveBeenCalledWith(
      "PreviousQuarter", // timePeriod
      undefined, // customFromDate
      undefined, // customToDate
      "TestFamily", // productFamily
      true, // criticalItemsOnly
      undefined, // majorItemsOnly
      undefined, // adequateItemsOnly
      undefined, // unconfiguredOnly
      "test", // searchTerm
      2, // pageNumber
      10, // pageSize
      "CurrentStock", // sortBy
      true, // sortDescending
      1.5, // salesMultiplier
      false, // isExport
    );
    expect(result.current.data).toEqual(mockResponse);
  });

  it("handles API errors correctly", async () => {
    mockClient.manufacturingStockAnalysis_GetStockAnalysis.mockRejectedValue(
      new Error("An unexpected server error occurred."),
    );

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () =>
        useManufacturingStockAnalysisQuery({
          timePeriod: TimePeriodFilter.PreviousQuarter,
          pageNumber: 1,
          pageSize: 20,
        }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.error).toBeTruthy();
    expect(result.current.data).toBeUndefined();
  });

  it("omits timePeriod param when it equals Q9M (default)", async () => {
    mockClient.manufacturingStockAnalysis_GetStockAnalysis.mockResolvedValue(
      mockResponse,
    );

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () =>
        useManufacturingStockAnalysisQuery({
          timePeriod: TimePeriodFilter.Q9M,
          pageNumber: 1,
          pageSize: 20,
        }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const timePeriodArg =
      mockClient.manufacturingStockAnalysis_GetStockAnalysis.mock.calls[0][0];
    expect(timePeriodArg).toBeUndefined();
  });

  it("includes timePeriod param for non-default periods", async () => {
    mockClient.manufacturingStockAnalysis_GetStockAnalysis.mockResolvedValue(
      mockResponse,
    );

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () =>
        useManufacturingStockAnalysisQuery({
          timePeriod: TimePeriodFilter.PreviousQuarter,
          pageNumber: 1,
          pageSize: 20,
        }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const timePeriodArg =
      mockClient.manufacturingStockAnalysis_GetStockAnalysis.mock.calls[0][0];
    expect(timePeriodArg).toBe("PreviousQuarter");
  });

  it("passes customFromDate/customToDate through as Date objects for CustomPeriod", async () => {
    const customFromDate = new Date("2023-01-01");
    const customToDate = new Date("2023-03-31");
    mockClient.manufacturingStockAnalysis_GetStockAnalysis.mockResolvedValue(
      mockResponse,
    );

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () =>
        useManufacturingStockAnalysisQuery({
          timePeriod: TimePeriodFilter.CustomPeriod,
          customFromDate,
          customToDate,
          pageNumber: 1,
          pageSize: 20,
        }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(
      mockClient.manufacturingStockAnalysis_GetStockAnalysis,
    ).toHaveBeenCalledWith(
      "CustomPeriod",
      customFromDate,
      customToDate,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      1,
      20,
      undefined,
      undefined,
      undefined,
      false,
    );
  });
});

describe("calculateTimePeriodRange", () => {
  const now = new Date("2023-04-15"); // Mid April 2023

  beforeAll(() => {
    jest.useFakeTimers();
    jest.setSystemTime(now);
  });

  afterAll(() => {
    jest.useRealTimers();
  });

  it("calculates previous quarter correctly", () => {
    const result = calculateTimePeriodRange(TimePeriodFilter.PreviousQuarter);

    expect(result).not.toBeNull();
    expect(result!.fromDate.getMonth()).toBe(0); // January (0-indexed)
    expect(result!.fromDate.getFullYear()).toBe(2023);
    expect(result!.toDate.getMonth()).toBe(2); // March (0-indexed)
    expect(result!.toDate.getFullYear()).toBe(2023);
  });

  it("calculates future quarter correctly", () => {
    const result = calculateTimePeriodRange(TimePeriodFilter.FutureQuarter);

    expect(result).not.toBeNull();
    expect(result!.fromDate.getMonth()).toBe(3); // April (0-indexed)
    expect(result!.fromDate.getFullYear()).toBe(2022); // Previous year
    expect(result!.toDate.getMonth()).toBe(5); // June (0-indexed)
    expect(result!.toDate.getFullYear()).toBe(2022); // Previous year
  });

  it("calculates previous season correctly", () => {
    const result = calculateTimePeriodRange(TimePeriodFilter.PreviousSeason);

    expect(result).not.toBeNull();
    expect(result!.fromDate.getMonth()).toBe(9); // October (0-indexed)
    expect(result!.fromDate.getFullYear()).toBe(2022); // Previous year for season
    expect(result!.toDate.getMonth()).toBe(0); // January (0-indexed)
    expect(result!.toDate.getFullYear()).toBe(2023); // Next year from season start
  });

  it("returns null for custom period", () => {
    const result = calculateTimePeriodRange(TimePeriodFilter.CustomPeriod);

    expect(result).toBeNull();
  });

  it("calculates Q9M with two ranges", () => {
    const result = calculateTimePeriodRange(TimePeriodFilter.Q9M);

    expect(result).not.toBeNull();
    expect(result!.ranges).toHaveLength(2);

    // Range A: last 6 months → now
    const rangeA = result!.ranges![0];
    expect(rangeA.from.getFullYear()).toBe(2022);
    expect(rangeA.from.getMonth()).toBe(9); // October (0-indexed)
    expect(rangeA.from.getDate()).toBe(15);
    expect(rangeA.to).toEqual(now);

    // Range B: 1 year ago → 1 year ago + 3 months
    const rangeB = result!.ranges![1];
    expect(rangeB.from.getFullYear()).toBe(2022);
    expect(rangeB.from.getMonth()).toBe(3); // April (0-indexed)
    expect(rangeB.from.getDate()).toBe(15);
    expect(rangeB.to.getFullYear()).toBe(2022);
    expect(rangeB.to.getMonth()).toBe(6); // July (0-indexed)
    expect(rangeB.to.getDate()).toBe(15);

    // Outer bounds via primary (range A: sixMonthsAgo → now)
    expect(result!.fromDate).toEqual(rangeA.from);
    expect(result!.toDate).toEqual(now);
  });
});

describe("formatWarehouseStock", () => {
  const baseItem = {
    code: "P1",
    name: "Product 1",
    currentStock: 0,
    erpStock: 0,
    eshopStock: 0,
    transportStock: 0,
    manufacturedStock: 0,
    primaryStockSource: "Erp",
    reserve: 0,
    quarantine: 0,
    planned: 0,
    salesInPeriod: 0,
    dailySalesRate: 0,
    optimalDaysSetup: 0,
    stockDaysAvailable: 0,
    minimumStock: 0,
    overstockPercentage: 0,
    batchSize: "1",
    severity: "Adequate",
    isConfigured: true,
  } as any;

  it("shows only the total when transport and manufactured are both zero", () => {
    const item = { ...baseItem, currentStock: 5, erpStock: 5 };
    expect(formatWarehouseStock(item)).toBe("5");
  });

  it("shows primary+transport breakdown when only transport is non-zero", () => {
    const item = { ...baseItem, currentStock: 12, erpStock: 5, transportStock: 7 };
    expect(formatWarehouseStock(item)).toBe("12 (5+7)");
  });

  it("shows primary+manufactured breakdown when only manufactured is non-zero", () => {
    const item = { ...baseItem, currentStock: 8, erpStock: 5, manufacturedStock: 3 };
    expect(formatWarehouseStock(item)).toBe("8 (5+3)");
  });

  it("shows primary+transport+manufactured breakdown when both are non-zero", () => {
    const item = {
      ...baseItem,
      currentStock: 15,
      erpStock: 5,
      transportStock: 7,
      manufacturedStock: 3,
    };
    expect(formatWarehouseStock(item)).toBe("15 (5+7+3)");
  });
});
```

#### Step 2: Run the test file and confirm it fails

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx
```

Expected: failures in the `useManufacturingStockAnalysisQuery` block — the hook still calls
`(apiClient as any).http.fetch`, not `mockClient.manufacturingStockAnalysis_GetStockAnalysis`, so
the mock is never invoked and assertions on it fail. (`calculateTimePeriodRange` and
`formatWarehouseStock` tests should still pass — they don't touch the query hook.)

#### Step 3: Rewrite `useManufacturingStockAnalysis.ts`

Replace the entire file content with:

```ts
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import {
  TimePeriod,
  resolveTimePeriod,
  getTimePeriodDisplayText,
  type DateRange,
} from "../../utils/timePeriod";
import {
  TimePeriod as GeneratedTimePeriod,
  GetManufacturingStockAnalysisResponse,
  ManufacturingStockItemDto,
  ManufacturingStockSummaryDto,
  ManufacturingStockSeverity,
  ManufacturingStockSortBy,
} from "../generated/api-client";

export { TimePeriod as TimePeriodFilter };
export { getTimePeriodDisplayText };

// Re-exported so existing consumers (ManufacturingStockAnalysis.tsx, ManufactureBatchPlanning.tsx)
// keep importing types/enums from this hook module unchanged, per spec FR-4 / arch-review Decision 1.
export {
  GetManufacturingStockAnalysisResponse,
  ManufacturingStockItemDto,
  ManufacturingStockSummaryDto,
  ManufacturingStockSeverity,
  ManufacturingStockSortBy,
};

export function calculateTimePeriodRange(
  period: TimePeriod,
  customFrom?: Date,
  customTo?: Date,
): { fromDate: Date; toDate: Date; ranges?: DateRange[] } | null {
  const result = resolveTimePeriod(period, customFrom, customTo);
  if (!result.primary) return null;
  return {
    fromDate: result.primary.from,
    toDate: result.primary.to,
    ranges: result.ranges.length > 1 ? result.ranges : undefined,
  };
}

// Request shape accepted by the query hook. The generated client method takes positional scalar
// arguments (not a request object), so this local interface remains the hook's public parameter
// contract. `sortBy` types against the generated enum; `timePeriod` stays typed against the
// app-level TimePeriodFilter (unchanged for all other consumers, e.g. calculateTimePeriodRange) —
// see FR-3 in spec.r1.md and design.r1.md's Component Design section.
export interface GetManufacturingStockAnalysisRequest {
  timePeriod?: TimePeriod;
  customFromDate?: Date;
  customToDate?: Date;
  productFamily?: string;
  criticalItemsOnly?: boolean;
  majorItemsOnly?: boolean;
  adequateItemsOnly?: boolean;
  unconfiguredOnly?: boolean;
  searchTerm?: string;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: ManufacturingStockSortBy;
  sortDescending?: boolean;
  salesMultiplier?: number;
}

/**
 * Converts the app-level TimePeriodFilter to the generated client's own TimePeriod enum at the
 * single API boundary (spec FR-3). Both are string enums with identical members
 * (PreviousQuarter/FutureQuarter/Y2Y/PreviousSeason/Q9M/CustomPeriod) — TypeScript enums are
 * nominal, so this is a same-string-value cast, not a data mapping; do not turn this into a
 * value-by-value mapping table unless the two enums genuinely diverge in membership.
 *
 * Q9M is the backend's implicit default: the pre-refactor query-string builder never appended
 * `timePeriod` when it was Q9M, so this returns `undefined` in that case to preserve that
 * omission. Used by both useManufacturingStockAnalysisQuery's queryFn and
 * ManufacturingStockAnalysis.tsx's handleExport, so there is exactly one conversion point.
 */
export const toGeneratedTimePeriod = (
  timePeriod: TimePeriod | undefined,
): GeneratedTimePeriod | undefined =>
  timePeriod && timePeriod !== TimePeriod.Q9M
    ? (timePeriod as unknown as GeneratedTimePeriod)
    : undefined;

// Query keys
const manufacturingStockAnalysisKeys = {
  all: ["manufacturing-stock-analysis"] as const,
  lists: () => [...manufacturingStockAnalysisKeys.all, "list"] as const,
  list: (filters: GetManufacturingStockAnalysisRequest) =>
    [...manufacturingStockAnalysisKeys.lists(), filters] as const,
};

// Main hook for manufacturing stock analysis
export const useManufacturingStockAnalysisQuery = (
  request: GetManufacturingStockAnalysisRequest,
) => {
  return useQuery({
    queryKey: manufacturingStockAnalysisKeys.list(request),
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();

      return apiClient.manufacturingStockAnalysis_GetStockAnalysis(
        toGeneratedTimePeriod(request.timePeriod),
        request.customFromDate,
        request.customToDate,
        request.productFamily,
        request.criticalItemsOnly,
        request.majorItemsOnly,
        request.adequateItemsOnly,
        request.unconfiguredOnly,
        request.searchTerm,
        request.pageNumber,
        request.pageSize,
        request.sortBy,
        request.sortDescending,
        request.salesMultiplier,
        false, // isExport — this hook is for interactive display, not export
      );
    },
    staleTime: 1000 * 60 * 2, // 2 minutes (stock data changes less frequently than purchase orders)
  });
};

// Helper function to get severity color class
export const getManufacturingSeverityColorClass = (
  severity: ManufacturingStockSeverity,
): string => {
  switch (severity) {
    case ManufacturingStockSeverity.Critical:
      return "text-red-600 bg-red-50";
    case ManufacturingStockSeverity.Major:
      return "text-orange-600 bg-orange-50";
    case ManufacturingStockSeverity.Minor:
      return "text-yellow-600 bg-yellow-50";
    case ManufacturingStockSeverity.Adequate:
      return "text-green-600 bg-green-50";
    case ManufacturingStockSeverity.Unconfigured:
      return "text-gray-600 bg-gray-50";
    default:
      return "text-gray-600 bg-gray-50";
  }
};

// Helper function to get severity display text
export const getManufacturingSeverityDisplayText = (
  severity: ManufacturingStockSeverity,
): string => {
  switch (severity) {
    case ManufacturingStockSeverity.Critical:
      return "Kritické";
    case ManufacturingStockSeverity.Major:
      return "Důležité";
    case ManufacturingStockSeverity.Minor:
      return "Menší";
    case ManufacturingStockSeverity.Adequate:
      return "Dostatečné";
    case ManufacturingStockSeverity.Unconfigured:
      return "Nezkonfigurováno";
    default:
      return "Neznámé";
  }
};

// Helper function to format Czech number
// Widened to accept `number | undefined` because the generated ManufacturingStockItemDto marks
// every numeric field optional (NSwag's default for all response DTOs), unlike the hand-coded
// interface it replaces. Mirrors the identical convention already shipped in the sibling
// frontend/src/api/hooks/usePurchaseStockAnalysis.ts.
export const formatNumber = (
  value: number | undefined,
  decimals: number = 2,
): string => {
  if (value === undefined || value === null) return "—";
  return value.toLocaleString("cs-CZ", {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });
};

// Helper function to format percentage
export const formatPercentage = (value: number | undefined): string => {
  if (value === undefined || value === null) return "—";
  return `${formatNumber(value, 1)}%`;
};

// Helper function to format warehouse stock with transport + manufactured breakdown
export const formatWarehouseStock = (item: ManufacturingStockItemDto): string => {
  const totalStock = formatNumber(item.currentStock, 0);
  const transport = item.transportStock ?? 0;
  const manufactured = item.manufacturedStock ?? 0;

  // If there are no secondary parts, show just the total
  if (transport === 0 && manufactured === 0) {
    return totalStock;
  }

  // Otherwise show breakdown: "15 (5+7+3)" = total (primary+transport+manufactured)
  const primaryStock =
    item.primaryStockSource === "Erp"
      ? formatNumber(item.erpStock, 0)
      : formatNumber(item.eshopStock, 0);

  const parts = [primaryStock];
  if (transport !== 0) {
    parts.push(formatNumber(transport, 0));
  }
  if (manufactured !== 0) {
    parts.push(formatNumber(manufactured, 0));
  }

  return `${totalStock} (${parts.join("+")})`;
};
```

Notes on what changed vs. the original file:
- The six hand-coded types (`GetManufacturingStockAnalysisRequest`'s old sibling declarations for
  `ManufacturingStockSortBy`, `ManufacturingStockSeverity`, `ManufacturingStockItemDto`,
  `ManufacturingStockSummaryDto`, `GetManufacturingStockAnalysisResponse`) are gone, replaced by
  imports + re-exports from `../generated/api-client` (FR-1).
- `formatDateForApi` is deleted — the generated method takes `Date | null | undefined` directly
  and serializes via `.toISOString()` internally (FR-2 AC2).
- `queryFn` now calls `apiClient.manufacturingStockAnalysis_GetStockAnalysis(...)` positionally
  and returns its result directly — no `response.ok` check, `response.json()`, or `as Promise<...>`
  cast (FR-2).
- New exported `toGeneratedTimePeriod` helper (FR-3) — see the design notes above the step block
  for why it's a shared function rather than two inline casts.
- `formatNumber`/`formatPercentage` widened to accept `number | undefined` (see inline comment) —
  required because `ManufacturingStockItemDto`'s fields are now all optional; without this,
  `npm run build` fails wherever a table cell calls e.g. `formatNumber(item.reserve, 0)`.

#### Step 4: Run the hook's test file again and confirm it passes

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx
```

Expected: all tests pass (`useManufacturingStockAnalysisQuery`, `calculateTimePeriodRange`,
`formatWarehouseStock` describe blocks all green).

#### Step 5: Type-check / build

```bash
cd frontend && npm run build
```

Expected: no TypeScript errors referencing `useManufacturingStockAnalysis.ts`. (Errors may still
appear from `ManufacturingStockAnalysis.tsx` at this point — that's expected and fixed in Task 2.)

#### Step 6: Commit

```bash
git add frontend/src/api/hooks/useManufacturingStockAnalysis.ts frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx
git commit -m "Manufacture: rewire useManufacturingStockAnalysis hook to the generated OpenAPI client (#3730)"
```

---

### task: migrate-export-handler-and-fix-page-type-fallout

**Scope:** `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` and its test file.
Implements FR-5 from spec.r1.md, plus the compile-error fallout from Task 1's type changes that
lives in this file (documented in the Global Constraints section above). Depends on Task 1 being
merged first (`toGeneratedTimePeriod` must exist).

**Files touched:**
- `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` (targeted edits)
- `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` (targeted edits +
  new test block)

#### Step 1: Add the new import

In `frontend/src/components/pages/ManufacturingStockAnalysis.tsx`, find the existing import block
(lines 18–29):

```tsx
import {
  useManufacturingStockAnalysisQuery,
  GetManufacturingStockAnalysisRequest,
  TimePeriodFilter,
  ManufacturingStockSortBy,
  ManufacturingStockSeverity,
  formatNumber,
  formatPercentage,
  formatWarehouseStock,
  calculateTimePeriodRange,
  getTimePeriodDisplayText,
} from "../../api/hooks/useManufacturingStockAnalysis";
```

Replace with (adds `toGeneratedTimePeriod`, no other change — the import *path* stays identical
per FR-4):

```tsx
import {
  useManufacturingStockAnalysisQuery,
  GetManufacturingStockAnalysisRequest,
  TimePeriodFilter,
  ManufacturingStockSortBy,
  ManufacturingStockSeverity,
  formatNumber,
  formatPercentage,
  formatWarehouseStock,
  calculateTimePeriodRange,
  getTimePeriodDisplayText,
  toGeneratedTimePeriod,
} from "../../api/hooks/useManufacturingStockAnalysis";
```

(The separate `import { ManufacturingStockItemDto } from '../../api/hooks/useManufacturingStockAnalysis';`
a few lines below, and the `getAuthenticatedApiClient` import, are untouched.)

#### Step 2: Rewrite `handleExport`

Find the existing `handleExport` (current lines ~174–247):

```tsx
  // Export functionality
  const handleExport = useCallback(async () => {
    setIsExporting(true);
    try {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/manufacturing-stock-analysis`;
      const params = new URLSearchParams();

      if (filters.timePeriod && filters.timePeriod !== TimePeriodFilter.Q9M) {
        params.append("timePeriod", filters.timePeriod);
      }
      if (filters.customFromDate)
        params.append("customFromDate", filters.customFromDate.toISOString().split("T")[0]);
      if (filters.customToDate)
        params.append("customToDate", filters.customToDate.toISOString().split("T")[0]);
      if (filters.productFamily)
        params.append("productFamily", filters.productFamily);
      if (filters.criticalItemsOnly) params.append("criticalItemsOnly", "true");
      if (filters.majorItemsOnly) params.append("majorItemsOnly", "true");
      if (filters.adequateItemsOnly) params.append("adequateItemsOnly", "true");
      if (filters.unconfiguredOnly) params.append("unconfiguredOnly", "true");
      if (filters.searchTerm) params.append("searchTerm", filters.searchTerm);
      if (filters.sortBy) params.append("sortBy", filters.sortBy);
      if (filters.sortDescending !== undefined)
        params.append("sortDescending", filters.sortDescending.toString());
      if (filters.salesMultiplier !== undefined && filters.salesMultiplier !== 1.0)
        params.append("salesMultiplier", filters.salesMultiplier.toString());
      params.append("isExport", "true");

      const queryString = params.toString();
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}${queryString ? `?${queryString}` : ""}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: { Accept: "application/json" },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      const result = await response.json();
      const today = new Date().toISOString().split("T")[0];
      await exportToXlsx(
        result.items ?? [],
        [
          { header: "Kód", value: (row: any) => row.code },
          { header: "Název", value: (row: any) => row.name },
          { header: "Sklad aktuální", value: (row: any) => row.currentStock },
          { header: "Sklad ERP", value: (row: any) => row.erpStock },
          { header: "Sklad E-shop", value: (row: any) => row.eshopStock },
          { header: "Sklad transport", value: (row: any) => row.transportStock },
          { header: "Primární zdroj skladu", value: (row: any) => row.primaryStockSource },
          { header: "Rezervace", value: (row: any) => row.reserve },
          { header: "Plánováno", value: (row: any) => row.planned },
          { header: "Prodeje v období", value: (row: any) => row.salesInPeriod },
          { header: "Denní prodeje", value: (row: any) => row.dailySalesRate },
          { header: "Optimální dny (nastavení)", value: (row: any) => row.optimalDaysSetup },
          { header: "Dní skladu", value: (row: any) => row.stockDaysAvailable },
          { header: "Minimální sklad", value: (row: any) => row.minimumStock },
          { header: "Přebytečné (%)", value: (row: any) => row.overstockPercentage },
          { header: "Velikost dávky", value: (row: any) => row.batchSize },
          { header: "Produktová rodina", value: (row: any) => row.productFamily },
          { header: "Závažnost", value: (row: any) => row.severity },
          { header: "Nakonfigurováno", value: (row: any) => row.isConfigured },
        ],
        `manufacturing-stock-analysis-${today}.xlsx`,
      );
    } catch {
      showError("Export selhal", "Nepodařilo se stáhnout data pro export.");
    } finally {
      setIsExporting(false);
    }
  }, [filters, showError]);
```

Replace with:

```tsx
  // Export functionality
  const handleExport = useCallback(async () => {
    setIsExporting(true);
    try {
      const apiClient = await getAuthenticatedApiClient();

      const result = await apiClient.manufacturingStockAnalysis_GetStockAnalysis(
        toGeneratedTimePeriod(filters.timePeriod),
        filters.customFromDate,
        filters.customToDate,
        filters.productFamily,
        filters.criticalItemsOnly,
        filters.majorItemsOnly,
        filters.adequateItemsOnly,
        filters.unconfiguredOnly,
        filters.searchTerm,
        undefined, // pageNumber — export returns all matching rows; the pre-refactor
        undefined, // pageSize  — query string builder never sent pageNumber/pageSize either
        filters.sortBy,
        filters.sortDescending,
        filters.salesMultiplier,
        true, // isExport
      );

      const today = new Date().toISOString().split("T")[0];
      await exportToXlsx(
        result.items ?? [],
        [
          { header: "Kód", value: (row: ManufacturingStockItemDto) => row.code },
          { header: "Název", value: (row: ManufacturingStockItemDto) => row.name },
          { header: "Sklad aktuální", value: (row: ManufacturingStockItemDto) => row.currentStock },
          { header: "Sklad ERP", value: (row: ManufacturingStockItemDto) => row.erpStock },
          { header: "Sklad E-shop", value: (row: ManufacturingStockItemDto) => row.eshopStock },
          { header: "Sklad transport", value: (row: ManufacturingStockItemDto) => row.transportStock },
          { header: "Primární zdroj skladu", value: (row: ManufacturingStockItemDto) => row.primaryStockSource },
          { header: "Rezervace", value: (row: ManufacturingStockItemDto) => row.reserve },
          { header: "Plánováno", value: (row: ManufacturingStockItemDto) => row.planned },
          { header: "Prodeje v období", value: (row: ManufacturingStockItemDto) => row.salesInPeriod },
          { header: "Denní prodeje", value: (row: ManufacturingStockItemDto) => row.dailySalesRate },
          { header: "Optimální dny (nastavení)", value: (row: ManufacturingStockItemDto) => row.optimalDaysSetup },
          { header: "Dní skladu", value: (row: ManufacturingStockItemDto) => row.stockDaysAvailable },
          { header: "Minimální sklad", value: (row: ManufacturingStockItemDto) => row.minimumStock },
          { header: "Přebytečné (%)", value: (row: ManufacturingStockItemDto) => row.overstockPercentage },
          { header: "Velikost dávky", value: (row: ManufacturingStockItemDto) => row.batchSize },
          { header: "Produktová rodina", value: (row: ManufacturingStockItemDto) => row.productFamily },
          { header: "Závažnost", value: (row: ManufacturingStockItemDto) => row.severity },
          { header: "Nakonfigurováno", value: (row: ManufacturingStockItemDto) => row.isConfigured },
        ],
        `manufacturing-stock-analysis-${today}.xlsx`,
      );
    } catch {
      showError("Export selhal", "Nepodařilo se stáhnout data pro export.");
    } finally {
      setIsExporting(false);
    }
  }, [filters, showError]);
```

**Important — preserving an easy-to-miss behavior:** the pre-refactor `handleExport` never
appended `pageNumber`/`pageSize` to its query string (unlike the main query hook, which does).
Passing `undefined` explicitly for both (not `filters.pageNumber`/`filters.pageSize`) is required
to keep this — otherwise the export would silently start returning only the current page's rows
instead of the full matching set, changing user-visible export behavior. This mirrors — and is as
easy to lose during the rewrite as — the Q9M-omission risk arch-review.r1.md explicitly calls out
for the *hook*; it applies symmetrically here.

#### Step 3: Fix the `ManufacturingStockSeverity`-typed helper function signatures

Every consumer already compares severity by enum member (`=== ManufacturingStockSeverity.Critical`
etc., confirmed by arch-review.r1.md's audit and re-confirmed while reading this file), so the
Critical/Major/Minor/Adequate/Unconfigured → `"Critical"`/`"Major"`/... representation change is
safe by construction and needs **no** production logic changes. But three local helper functions
declare their `severity` parameter as the required (non-optional) `ManufacturingStockSeverity`,
while `item.severity` is now `ManufacturingStockSeverity | undefined` (generated class — all
fields optional). Widen all three parameter types; behavior at every `case`/`default` branch is
unchanged since a `switch` on `undefined` simply falls to `default`.

In `getRowColorClass` (current lines ~486–489):

```tsx
  const getRowColorClass = (
    severity: ManufacturingStockSeverity,
    isSubgridRow: boolean = false,
  ) => {
```

Replace with:

```tsx
  const getRowColorClass = (
    severity: ManufacturingStockSeverity | undefined,
    isSubgridRow: boolean = false,
  ) => {
```

In `getSeverityStripColor` (current line ~852):

```tsx
  const getSeverityStripColor = (severity: ManufacturingStockSeverity) => {
```

Replace with:

```tsx
  const getSeverityStripColor = (severity: ManufacturingStockSeverity | undefined) => {
```

In `getStockValueColorClass` (current line ~879):

```tsx
  const getStockValueColorClass = (severity: ManufacturingStockSeverity) => {
```

Replace with:

```tsx
  const getStockValueColorClass = (severity: ManufacturingStockSeverity | undefined) => {
```

(`getManufacturingSeverityColorClass`/`getManufacturingSeverityDisplayText` in the hook file and
`handleSeverityFilterClick` in this file are untouched — the former are unused outside the hook
module, the latter is only ever called with hardcoded enum literals like
`ManufacturingStockSeverity.Critical`, never with `item.severity`.)

#### Step 4: Fix `isInPlanningList`'s parameter type

Current (lines ~250–252):

```tsx
  const isInPlanningList = (productCode: string) => {
    return planningListItems.some(item => item.productCode === productCode);
  };
```

Replace with:

```tsx
  const isInPlanningList = (productCode: string | undefined) => {
    return planningListItems.some(item => item.productCode === productCode);
  };
```

(Needed because the "indicator" column's `renderCell` calls `isInPlanningList(item.code)`, and
`item.code` is now `string | undefined` on the generated `ManufacturingStockItemDto`.)

#### Step 5: Fix the two direct numeric comparisons on optional fields

`formatNumber`/`formatPercentage` (widened in Task 1) absorb most optional-field call sites
automatically. Two spots compare an optional field directly with a relational operator, which
`formatNumber` widening does not fix — TypeScript strict mode rejects `number | undefined > number`.

In the `stockDaysAvailable` column's `renderCell` (current lines ~433–437):

```tsx
      renderCell: (item) => (
        <div className="font-bold">
          {item.stockDaysAvailable > 999 ? '∞' : formatNumber(item.stockDaysAvailable, 0)}
        </div>
      ),
```

Replace with:

```tsx
      renderCell: (item) => (
        <div className="font-bold">
          {(item.stockDaysAvailable ?? 0) > 999 ? '∞' : formatNumber(item.stockDaysAvailable, 0)}
        </div>
      ),
```

In the `optimalDaysSetup` column's `renderCell` (current line ~467):

```tsx
      renderCell: (item) => <>{item.optimalDaysSetup > 0 ? `${item.optimalDaysSetup} dní` : '—'}</>,
```

Replace with:

```tsx
      renderCell: (item) => <>{(item.optimalDaysSetup ?? 0) > 0 ? `${item.optimalDaysSetup} dní` : '—'}</>,
```

(The four `*ItemsOnly`-style ternaries like `(item.manufacturedStock || 0) > 0 ? ... :
formatNumber(item.manufacturedStock, 0)` for `manufacturedStock`/`reserve`/`quarantine`/`planned`
already coerce with `|| 0` inside the comparison itself, so they type-check as-is — no change
needed there.)

#### Step 6: Fix the one two-level optional-chain gap

Current (in the Product Family filter `<select>`, ~line 1368):

```tsx
                    {summary?.productFamilies.map((family) => (
```

Replace with:

```tsx
                    {summary?.productFamilies?.map((family) => (
```

(`ManufacturingStockSummaryDto.productFamilies` used to be a required `string[]` on the hand-coded
interface; the generated class marks it optional too, so `summary?.` alone no longer guarantees
`.productFamilies` is defined.)

#### Step 7: Update the page's test file — fix the stale numeric severity mock, add export coverage

`frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` entirely mocks the
hook module via a `jest.mock` factory, so it is decoupled from Task 1's real re-exports — but its
own hand-written `ManufacturingStockSeverity` mock still uses the old numeric values (`Critical:
0`, `Adequate: 3`, ...), which no longer reflects reality now that the real enum is string-valued.
Update it for consistency, add the new `toGeneratedTimePeriod` mock the page now imports, and add
a new test verifying `handleExport`'s generated-client call + typed row accessors.

7a. In the `jest.mock("../../../api/hooks/useManufacturingStockAnalysis", ...)` factory (lines
14–68), change:

```tsx
  ManufacturingStockSeverity: {
    Critical: 0,
    Major: 1,
    Minor: 2,
    Adequate: 3,
    Unconfigured: 4,
  },
```

to:

```tsx
  ManufacturingStockSeverity: {
    Critical: "Critical",
    Major: "Major",
    Minor: "Minor",
    Adequate: "Adequate",
    Unconfigured: "Unconfigured",
  },
```

and add `toGeneratedTimePeriod` to the same factory object (needed because `handleExport` now
calls it — without this the new export test in step 7d would throw
`toGeneratedTimePeriod is not a function`):

```tsx
  toGeneratedTimePeriod: (tp: any) => (tp && tp !== "Q9M" ? tp : undefined),
```

7b. In `mockData.items` (lines 137–194), change `severity: 3, // ManufacturingStockSeverity.Adequate`
to `severity: "Adequate",` and `severity: 0, // ManufacturingStockSeverity.Critical` to
`severity: "Critical",`.

7c. In the standalone mock object inside the `"renders the Vyrobeno column..."` test (line 567,
already `severity: "Adequate"`) — no change needed, it already uses the string form.

7d. Add `jest.mock` calls for `../../../api/client` and `../../../utils/exportToXlsx` near the top
of the file (after the existing `jest.mock("../../../api/hooks/useManufacturingStockAnalysis", ...)`
block), and a new `describe("handleExport", ...)` block at the end of the file, before the closing
`});` of the outer `describe("ManufacturingStockAnalysis", ...)`.

Add these two `jest.mock` calls near the top-level mocks (after the `CatalogDetail` mock, before
`createWrapper`):

```tsx
jest.mock("../../../utils/exportToXlsx", () => ({
  exportToXlsx: jest.fn().mockResolvedValue(undefined),
}));
```

(`../../../api/client` is mocked per-describe-block below, not globally, since only the export
tests need `getAuthenticatedApiClient` — the rest of the suite mocks the whole hook module and
never reaches real `api/client` code.)

Add this new `describe` block as the last item inside the outer `describe("ManufacturingStockAnalysis", ...)`,
right before its closing `});`:

```tsx
  describe("handleExport", () => {
    const mockGetStockAnalysis = jest.fn();

    beforeEach(() => {
      document.cookie = "manufacturing-stock-sales-multiplier=; max-age=0; path=/";
      jest.mock("../../../api/client");
      const { getAuthenticatedApiClient } = require("../../../api/client");
      (getAuthenticatedApiClient as jest.Mock).mockResolvedValue({
        manufacturingStockAnalysis_GetStockAnalysis: mockGetStockAnalysis,
      });
      mockGetStockAnalysis.mockReset();
    });

    it("calls the generated client with isExport=true, no pagination, and typed row accessors matching the default filters", async () => {
      mockUseManufacturingStockAnalysisQuery.mockReturnValue({
        data: mockData,
        isLoading: false,
        error: null,
        refetch: jest.fn(),
      });
      mockGetStockAnalysis.mockResolvedValue({
        items: [
          {
            code: "PROD001",
            name: "Test Product 1",
            currentStock: 100,
            severity: "Adequate",
          },
        ],
      });

      render(<ManufacturingStockAnalysis />, { wrapper: createWrapper() });

      fireEvent.click(screen.getByText("Export"));

      await waitFor(() => expect(mockGetStockAnalysis).toHaveBeenCalled());

      expect(mockGetStockAnalysis).toHaveBeenCalledWith(
        undefined, // timePeriod — default filter state is Q9M, omitted
        undefined, // customFromDate
        undefined, // customToDate
        undefined, // productFamily
        true, // criticalItemsOnly (default filter state)
        true, // majorItemsOnly (default filter state)
        false, // adequateItemsOnly
        false, // unconfiguredOnly
        "", // searchTerm
        undefined, // pageNumber — export is not paginated
        undefined, // pageSize
        "OverstockPercentage", // sortBy (default filter state)
        false, // sortDescending
        1, // salesMultiplier (default, cookie cleared above)
        true, // isExport
      );

      const { exportToXlsx: mockExportToXlsx } = require("../../../utils/exportToXlsx");
      await waitFor(() => expect(mockExportToXlsx).toHaveBeenCalled());

      const [rows, columns, filename] = mockExportToXlsx.mock.calls[0];
      expect(rows).toEqual([
        { code: "PROD001", name: "Test Product 1", currentStock: 100, severity: "Adequate" },
      ]);
      expect(columns.find((c: any) => c.header === "Kód").value(rows[0])).toBe("PROD001");
      expect(columns.find((c: any) => c.header === "Sklad aktuální").value(rows[0])).toBe(100);
      expect(columns.find((c: any) => c.header === "Závažnost").value(rows[0])).toBe("Adequate");
      expect(filename).toMatch(/^manufacturing-stock-analysis-\d{4}-\d{2}-\d{2}\.xlsx$/);
    });
  });
```

#### Step 8: Run the page's test file and confirm it passes

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx
```

Expected: all tests pass, including the new `handleExport` block.

#### Step 9: Full build + full test suite

```bash
cd frontend && npm run build
cd frontend && CI=true npx react-scripts test --watchAll=false
```

Expected: `npm run build` completes with no TypeScript errors; no other test file references the
six deleted types (confirm with the grep below — it should return nothing beyond the two files
already touched by this plan):

```bash
cd frontend && grep -rn "GetManufacturingStockAnalysisResponse\b" src --include="*.ts" --include="*.tsx" | grep -v "api/hooks/useManufacturingStockAnalysis.ts\|api/generated/api-client.ts\|components/pages/ManufacturingStockAnalysis.tsx"
```

Expected: no output (or only files unrelated to this feature that already imported the type from
the generated client directly).

#### Step 10: Lint

```bash
cd frontend && npm run lint
```

Expected: no new lint errors in the two changed source files.

#### Step 11: Commit

```bash
git add frontend/src/components/pages/ManufacturingStockAnalysis.tsx frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx
git commit -m "Manufacture: migrate handleExport to the generated OpenAPI client, fix optional-field fallout (#3730)"
```

---

## Self-Review

**Spec coverage** (spec.r1.md FR-1 through FR-6):
- FR-1 (remove hand-coded types, re-export generated ones): Task 1 Step 3 — six types deleted,
  five re-exported.
- FR-2 (replace manual fetch in the query hook): Task 1 Step 3's `queryFn` — no `(apiClient as
  any)`, no `.http.fetch`, no manual `URLSearchParams`, `formatDateForApi` removed, `Q9M` omission
  preserved via `toGeneratedTimePeriod` and covered by a dedicated test (Task 1 Step 1).
- FR-3 (`TimePeriod` enum boundary): Task 1 Step 3's `GeneratedTimePeriod` alias + `toGeneratedTimePeriod`
  helper; app-level `TimePeriodFilter` untouched everywhere else (confirmed: `calculateTimePeriodRange`,
  `getTimePeriodDisplayText`, and the request's own `timePeriod` field all stay app-level-typed).
- FR-4 (page's import strategy): Task 2 Step 1 only *adds* a named import to the existing
  statement; the import path and the two existing statements are untouched, satisfying FR-4's ACs
  verbatim.
- FR-5 (migrate `handleExport`): Task 2 Step 2 — no `(apiClient as any)`, no `.http.fetch`, no
  manual `URLSearchParams`; typed accessors against `ManufacturingStockItemDto`; column
  set/headers/order unchanged (only the accessor typing changed, values unchanged); new test
  coverage added in Task 2 Step 7 (none existed before, so FR-5 AC3's "if any" was previously
  vacuous — this plan goes beyond the letter of that AC because the arch-review's own risk table
  treats this call site as carrying the same transposition risk as the hook).
- FR-6 (backend unchanged): no backend files touched anywhere in this plan.

**Risks from arch-review.r1.md's risk table**, and how each is addressed:
1. Positional-argument transposition — Task 1 Step 1's test asserts the full 15-argument call with
   one inline comment per position; Task 2 Step 7's export test does the same.
2. Severity numeric→string — no production code changes needed (confirmed: every consumer compares
   symbolically); Task 2 Step 7a/7b updates the one place that had drifted from reality (the page
   test's own hand-rolled mock).
3. `ManufacturingStockItemDto` field-name mismatch surfacing in `handleExport` — Task 2 Step 2
   types every accessor against the real generated class, so any genuine field mismatch is now a
   compile error, per arch-review's own stated mitigation.
4. `Q9M` omission easy to lose — extracted into one shared, tested `toGeneratedTimePeriod` helper
   used by *both* call sites (Task 1 Step 3, Task 2 Step 2), rather than two independent inline
   casts that could drift.

**Risks found during file-reading that arch-review.r1.md did not flag** (documented inline at the
relevant step, not just here):
- Generated `ManufacturingStockItemDto`/`ManufacturingStockSummaryDto` mark all fields optional,
  unlike the hand-coded interfaces — this is a real, mechanical source of `strict: true` compile
  errors across `ManufacturingStockAnalysis.tsx`. Fixed via: widening `formatNumber`/`formatPercentage`
  (Task 1, mirroring the already-shipped `usePurchaseStockAnalysis.ts` convention), widening three
  severity-typed helper signatures and `isInPlanningList` (Task 2 Steps 3–4), adding `?? 0` to two
  direct relational comparisons (Task 2 Step 5), and one `?.` on `summary?.productFamilies?.map`
  (Task 2 Step 6).
- `handleExport` never sent `pageNumber`/`pageSize` pre-refactor (unlike the main query hook) —
  preserved explicitly by passing `undefined` for both rather than `filters.pageNumber`/`filters.pageSize`
  (Task 2 Step 2).
- A third, spec-uncatalogued call site with the same anti-pattern exists (`handleRowExpand`,
  ~lines 596–625) — explicitly left untouched per the Global Constraints section, not silently
  fixed or silently ignored.

**Placeholder scan:** no "TBD"/"handle appropriately"/"similar to above" phrasing anywhere in this
plan; every step that changes code shows the complete before/after code, not a description of it.

**Type consistency:** `toGeneratedTimePeriod(timePeriod: TimePeriod | undefined):
GeneratedTimePeriod | undefined` (Task 1) is called identically in Task 1's `queryFn` and Task 2's
`handleExport` with the same argument shape (`request.timePeriod` / `filters.timePeriod`, both
typed `TimePeriod | undefined` per `GetManufacturingStockAnalysisRequest`). `ManufacturingStockItemDto`
is imported the same way in both the hook file (defines it via re-export) and the page file
(consumes the re-export) — no divergent local re-declaration remains anywhere.
