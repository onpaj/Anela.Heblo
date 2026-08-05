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

