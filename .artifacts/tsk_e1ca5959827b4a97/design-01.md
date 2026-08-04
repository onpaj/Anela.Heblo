# Design — Replace raw `http.fetch` bypass in `useManufactureOutput` & `useSemiproductRecipePdf`

No UI section: this task changes no visual layout, interaction, or component
hierarchy — it swaps the data-fetching implementation behind two existing hooks
and repoints two import statements. `ManufactureOutput.tsx`,
`ManufactureOutputModal.tsx`, and `ManufactureBatchCalculator.tsx` render
identically before and after.

## Component design

### 1. `frontend/src/api/hooks/useManufactureOutput.ts`

**Responsibility (unchanged):** expose `useManufactureOutputQuery(monthsBack)` as a
React Query hook, plus two pure display-formatting helpers
(`formatMonthDisplay`, `getMonthShortName`).

**Change:** delete the four hand-declared interfaces (lines 4-31). Import the
equivalents from the generated client, matching the pattern already used in
`useManufactureOrders.ts` (`import { X } from "../generated/api-client"`):

```ts
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  GetManufactureOutputResponse,
  ManufactureOutputMonth,
  ProductContribution,
  ProductionDetail,
} from "../generated/api-client";

export type {
  GetManufactureOutputResponse,
  ManufactureOutputMonth,
  ProductContribution,
  ProductionDetail,
};

export const useManufactureOutputQuery = (monthsBack: number = 13) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.manufactureOutput, monthsBack],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.manufactureOutput_GetManufactureOutput(monthsBack);
    },
    retry: 1,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

// formatMonthDisplay / getMonthShortName unchanged
```

Notes:
- `getAuthenticatedApiClient()` is synchronous (`frontend/src/api/client.ts:276`);
  drop the stray `await` at this call site while touching the line.
- The generated method already throws `SwaggerException` on non-2xx and parses
  JSON via NSwag's `fromJS`, so the manual `response.ok` check and `response.json()`
  cast are deleted outright — no replacement needed, React Query's `error` state
  already surfaces thrown errors.
- Re-exporting the four generated types with `export type { ... }` from this
  module (rather than only from `generated/api-client`) means the two consumer
  files can keep importing from `../../api/hooks/useManufactureOutput` — a
  smaller, more surgical diff than repointing every import site. This matches
  the existing project convention of a hook module being the one import
  surface a page component depends on for its data shape (see
  `useManufacturedProductInventory.ts` re-exporting its response types the
  same way).

### 2. `frontend/src/api/hooks/useSemiproductRecipePdf.ts`

**Responsibility (unchanged):** expose `{ openRecipePdf, isLoading, error }`
to trigger a recipe PDF download/open-in-new-tab.

**Change:** replace the manual URL build + raw fetch with the generated
`manufactureBatch_GetRecipePdf`, and consume `FileResponse.data` directly:

```ts
import { useState } from "react";
import { getAuthenticatedApiClient } from "../client";

export const useSemiproductRecipePdf = () => {
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  const openRecipePdf = async (productCode: string, batchSize?: number) => {
    setIsLoading(true);
    setError(null);
    try {
      const apiClient = getAuthenticatedApiClient();
      const response = await apiClient.manufactureBatch_GetRecipePdf(
        productCode,
        batchSize,
      );
      const blobUrl = URL.createObjectURL(response.data);
      window.open(blobUrl, "_blank", "noopener,noreferrer");
      setTimeout(() => URL.revokeObjectURL(blobUrl), 10000);
    } catch (err) {
      const error = err instanceof Error ? err : new Error(String(err));
      setError(error);
    } finally {
      setIsLoading(false);
    }
  };

  return { openRecipePdf, isLoading, error };
};
```

Notes:
- `manufactureBatch_GetRecipePdf(productCode, batchSize)` accepts
  `number | null | undefined` for `batchSize`; the hook's own `batchSize?:
  number` param passes straight through, `undefined` included.
- The manual `response.ok`/status check is deleted — the generated method
  already throws on non-2xx/204, which lands in the existing `catch` block
  unchanged, so the public `{ isLoading, error }` contract is preserved
  exactly.
- `response.blob()` → `response.data` is a 1:1 swap (`FileResponse.data` is
  already a `Blob`, built by the generated method's own `response.blob()`
  call — see `api-client.ts:6706-6734`).

### 3. `frontend/src/components/pages/ManufactureOutput.tsx`

**Responsibility (unchanged):** render the stacked monthly output chart and
summary stats; open the drill-down modal on bar click.

**Change:** all fields on the generated types are optional
(`months?`, `products?`, `productionDetails?`, `month?`, `totalOutput?`,
`productCode?`, `productName?`, `quantity?`, `difficulty?`, `weightedValue?`),
where the deleted hand-declared interfaces had them required. Rather than
scatter `?? []`/`?? 0` at each of the ~15 access points, normalize once at the
top of the component and at the top of each `useMemo`/callback that walks
`month.products`, following the existing project convention of normalizing
optional collections once near their source
(`ManufacturedInventoryPage.tsx:295`: `const items = data?.items ?? [];`):

```ts
const months = data?.months ?? [];
```

then replace `data?.months` / `data.months` reads with `months` (still guard
`chartData`/`summaryStats` `useMemo`s with `if (months.length === 0) return null;`
instead of the current `if (!data?.months) return null;`), and inside each
callback that iterates a month's products, normalize locally:

```ts
month.products.forEach(...)         →  (month.products ?? []).forEach(...)
month.products.filter(...)          →  (month.products ?? []).filter(...)
month.products.find(...)            →  (month.products ?? []).find(...)
```

Numeric fields accessed unconditionally (`product.weightedValue`,
`product.quantity.toFixed(1)`, `product.difficulty.toFixed(1)`,
`month.totalOutput`) become `(product.weightedValue ?? 0)`,
`(product.quantity ?? 0).toFixed(1)`, etc. — same `?? 0` convention already
used at `ManufactureOutput.tsx:110` (`product?.weightedValue || 0`).
String fields interpolated directly (`product.productCode`,
`product.productName`) are template-string-safe with `undefined` (renders as
the literal text `"undefined"` only if the API omits them, which does not
happen for the current backend contract) — no change required there beyond
what TypeScript's strict-null-checks forces to compile.

Import fix:

```ts
import {
  useManufactureOutputQuery,
  formatMonthDisplay,
  getMonthShortName,
  ManufactureOutputMonth,
} from "../../api/hooks/useManufactureOutput";
```

stays exactly as-is (types re-exported from the hook module per the design
in section 1) — **no import path change needed** in this file.

### 4. `frontend/src/components/pages/ManufactureOutputModal.tsx`

**Responsibility (unchanged):** two-panel drill-down (products table +
selected product's production records table) for one month.

**Changes:**
- Same optional-field handling as `ManufactureOutput.tsx`:
  `monthData.products` → `(monthData.products ?? [])`,
  `monthData.productionDetails` → `(monthData.productionDetails ?? [])`,
  `monthData.totalOutput.toFixed(1)` → `(monthData.totalOutput ?? 0).toFixed(1)`,
  `monthData.month` passed into `formatMonthDisplay` stays as-is since that
  helper already takes a plain string and `month?: string` still
  structurally satisfies call sites after a `monthData.month ?? ""` guard —
  add that guard at the one call site (line 53).
- **`ProductionDetail.date` type change: `string` → `Date`.** The generated
  type's `init()` does `this.date = _data["date"] ? new Date(_data["date"]) :
  <any>undefined;` (NSwag's standard date handling), so by the time
  `record.date` reaches this component it is already a `Date` instance, not
  an ISO string. Current code:
  ```ts
  const formatDate = (dateStr: string) => {
    return new Date(dateStr).toLocaleDateString("cs-CZ");
  };
  ...
  {formatDate(record.date)}
  ```
  becomes:
  ```ts
  const formatDate = (date?: Date) => {
    return date ? date.toLocaleDateString("cs-CZ") : "";
  };
  ```
  (drop the redundant `new Date(...)` wrap — `record.date` is already a
  `Date`; `toLocaleDateString` is called directly). No other formatting
  helper touches `date` in this file.
- Numeric fields on `ProductionDetail`/`ProductContribution` used
  unconditionally (`record.amount.toFixed(1)`,
  `formatCurrency(record.pricePerPiece)`, `formatCurrency(record.priceTotal)`,
  `product.quantity.toFixed(1)`, `product.difficulty.toFixed(1)`) get the same
  `?? 0` guard as in `ManufactureOutput.tsx`.

Import fix: identical reasoning to section 3 — types come from
`../../api/hooks/useManufactureOutput` (re-exported), no path change:

```ts
import {
  ManufactureOutputMonth,
  ProductContribution,
  formatMonthDisplay,
} from "../../api/hooks/useManufactureOutput";
```

### 5. `frontend/src/components/pages/ManufactureBatchCalculator.tsx`

No change. It only consumes the hook's public `{ openRecipePdf, isLoading,
error }` surface (confirmed via `grep -n "useSemiproductRecipePdf\|openRecipePdf"`,
one call site at line 487 calling `openRecipePdf(productCode, batchSize)`),
which is unchanged by this design.

## Data schemas

No backend/contract change — this is a caller-side migration to already-generated
types. For reference, the shapes now driving both hook and components (all from
`frontend/src/api/generated/api-client.ts`):

```ts
class GetManufactureOutputResponse extends BaseResponse {
  months?: ManufactureOutputMonth[];
}

class ManufactureOutputMonth {
  month?: string;               // "YYYY-MM"
  totalOutput?: number;
  products?: ProductContribution[];
  productionDetails?: ProductionDetail[];
}

class ProductContribution {
  productCode?: string;
  productName?: string;
  quantity?: number;
  difficulty?: number;
  weightedValue?: number;
}

class ProductionDetail {
  productCode?: string;
  productName?: string;
  date?: Date;                  // NSwag-parsed from ISO string — was `string` in the hand-declared type
  amount?: number;
  pricePerPiece?: number;
  priceTotal?: number;
  documentNumber?: string;
}

interface FileResponse {
  data: Blob;
  status: number;
  fileName?: string;
  headers?: { [name: string]: any };
}
```

Endpoints (unchanged, now reached via typed methods instead of raw fetch):
- `GET /api/manufacture-output?monthsBack={n}` → `ApiClient.manufactureOutput_GetManufactureOutput(monthsBack)` → `Promise<GetManufactureOutputResponse>`
- `GET /api/manufacture-batch/recipe-pdf/{productCode}?batchSize={n}` → `ApiClient.manufactureBatch_GetRecipePdf(productCode, batchSize)` → `Promise<FileResponse>`

## Verification plan

1. `grep -rn "as any" frontend/src/api/hooks/useManufactureOutput.ts frontend/src/api/hooks/useSemiproductRecipePdf.ts` → no hits.
2. `cd frontend && npm run build` → zero TS errors (this is what actually
   surfaces every now-optional field access that still assumes non-null;
   fix each reported site with the `?? []` / `?? 0` conventions above rather
   than guessing all sites up front).
3. `cd frontend && npm run lint` → no new warnings in the five touched files.
4. Manual smoke check in a running dev instance: Manufacture Output page
   renders the chart, clicking a bar opens the modal with correct product
   list and drill-down records with correctly formatted dates; Batch
   Calculator's recipe PDF button still opens a PDF in a new tab.
