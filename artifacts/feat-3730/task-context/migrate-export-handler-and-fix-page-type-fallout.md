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
