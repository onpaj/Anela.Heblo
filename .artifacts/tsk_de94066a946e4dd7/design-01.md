# Design — Remove dead DateFrom/DateTo from GetProductMarginsRequest

No UI section: this is a pure contract-cleanup deletion. There is no new screen,
control, or interaction — `ProductMarginsList.tsx` already never surfaces a
date-range picker and requires no visual change.

## Component design

Three components are touched, each losing the same two fields/parameters in the
same position (end of the parameter list) so the change is a mechanical, low-risk
deletion mirroring the `#3486`/`#3487` precedent shape.

### 1. `GetProductMarginsRequest` (Application layer, MediatR request DTO)

- **Responsibility**: describes the full set of query parameters
  `ProductMarginsController.GetProductMargins` binds via `[FromQuery]`.
- **Change**: delete the two unread properties.

  ```csharp
  public class GetProductMarginsRequest : IRequest<GetProductMarginsResponse>
  {
      public string? ProductCode { get; set; }
      public string? ProductName { get; set; }
      public ProductType? ProductType { get; set; }
      public int PageNumber { get; set; } = 1;
      public int PageSize { get; set; } = 20;
      public string? SortBy { get; set; }
      public bool SortDescending { get; set; } = false;
      // DateFrom / DateTo removed — never read by GetProductMarginsHandler
  }
  ```
- **Interface boundary**: `ProductMarginsController.cs:21` binds the whole class
  via `[FromQuery] GetProductMarginsRequest request` — no controller code change
  needed, since the binder simply stops looking for `DateFrom`/`DateTo` in the
  query string. Any client still sending those query params gets them silently
  ignored by model binding (same as today, just no longer advertised).
- **Out of scope, unchanged**: `GetProductMarginsHandler.Handle` and
  `MapToMarginDto`'s hardcoded `AddMonths(-13)` window — the handler already
  doesn't reference `request.DateFrom`/`DateTo`, so removing the properties is a
  zero-behavior-change deletion in this component.

### 2. Generated TypeScript client (`frontend/src/api/generated/api-client.ts`)

- **Responsibility**: typed HTTP wrapper generated from the OpenAPI spec; not
  hand-authored.
- **Change**: regenerating from the trimmed backend contract removes the trailing
  `dateFrom`, `dateTo` parameters from `productMargins_GetProductMargins(...)`
  and the two `if (dateFrom !== ...) url_ += "DateFrom=..."` / `DateTo=...`
  query-string blocks (`api-client.ts:10809, 10831-10834` today).
- **Interface after regeneration**:

  ```ts
  productMargins_GetProductMargins(
    productCode: string | null | undefined,
    productName: string | null | undefined,
    productType: ProductType | null | undefined,
    pageNumber: number | undefined,
    pageSize: number | undefined,
    sortBy: string | null | undefined,
    sortDescending: boolean | undefined,
  ): Promise<GetProductMarginsResponse>
  ```
- No manual edits to this file — generation is triggered by the standard build
  step per `docs/development/api-client-generation.md`, run after the backend
  change so the OpenAPI spec reflects the trimmed DTO.

### 3. `useProductMarginsQuery` hook (`frontend/src/api/hooks/useProductMargins.ts`)

- **Responsibility**: React Query wrapper around
  `productMargins_GetProductMargins`, owning the query key and cache policy for
  the product margins list.
- **Change**: drop the two trailing parameters and the two `dateFrom || null`,
  `dateTo || null` arguments passed to the generated client call; drop
  `dateFrom`/`dateTo` from the `queryKey` array (they no longer exist as
  distinguishing cache inputs).

  ```ts
  export const useProductMarginsQuery = (
    productCode?: string,
    productName?: string,
    productType?: string,
    pageNumber: number = 1,
    pageSize: number = 20,
    sortBy?: string,
    sortDescending: boolean = false,
  ) => {
    return useQuery<GetProductMarginsResponse, Error>({
      queryKey: [
        ...QUERY_KEYS.productMargins,
        productCode,
        productName,
        productType,
        pageNumber,
        pageSize,
        sortBy,
        sortDescending,
      ],
      queryFn: async () => {
        const apiClient = await getAuthenticatedApiClient();
        let productTypeEnum = null;
        if (productType === "Product") productTypeEnum = ProductType.Product;
        if (productType === "Goods") productTypeEnum = ProductType.Goods;
        if (productType === "Material") productTypeEnum = ProductType.Material;
        if (productType === "SemiProduct")
          productTypeEnum = ProductType.SemiProduct;
        if (productType === "Set") productTypeEnum = ProductType.Set;

        return apiClient.productMargins_GetProductMargins(
          productCode || null,
          productName || null,
          productTypeEnum,
          pageNumber,
          pageSize,
          sortBy || null,
          sortDescending,
        );
      },
      staleTime: 5 * 60 * 1000,
      gcTime: 10 * 60 * 1000,
    });
  };
  ```
- **Call site**: `ProductMarginsList.tsx:48-56` already calls the hook with
  exactly 7 positional arguments (`productCodeFilter` through
  `sortDescending`) and never passes `dateFrom`/`dateTo` — confirmed the only
  call site. No edit required there; it remains source-compatible with the
  trimmed signature.
- **Test mock**: `ProductMarginsList.test.tsx` mocks
  `useProductMarginsHook.useProductMarginsQuery` — verify at implementation time
  it asserts on argument count/shape; if it does, update the mock expectation
  to match the 7-parameter signature (this is a mechanical test-signature
  alignment, not new test-writing, so it's covered here rather than deferred).

## Data schemas

### Request (query string), before → after

| Param | Before | After |
|---|---|---|
| `ProductCode` | `string?` | unchanged |
| `ProductName` | `string?` | unchanged |
| `ProductType` | `ProductType?` | unchanged |
| `PageNumber` | `int` (default 1) | unchanged |
| `PageSize` | `int` (default 20) | unchanged |
| `SortBy` | `string?` | unchanged |
| `SortDescending` | `bool` (default false) | unchanged |
| `DateFrom` | `DateTime?` — bound, never read | **removed** |
| `DateTo` | `DateTime?` — bound, never read | **removed** |

### Response — unchanged

`GetProductMarginsResponse` and `ProductMarginDto` are untouched: the handler's
output shape, the hardcoded 13-month `MonthlyHistory` window, and the
pre-computed `Margins.Averages` (`M0`/`M1`/`M2`) all keep their current
behavior exactly as-is. This confirms the change is contract-surface-only —
no response payload or computed value changes for any existing caller.

### Event payloads

None — this feature has no event/message payloads; `RefreshMarginData`'s
background computation window is unaffected and out of scope.

## Validation plan (carried from plan, restated for this step's scope)

- `dotnet build` + `dotnet format` after removing the two properties.
- `dotnet test` on `GetProductMarginsHandlerTests.cs` — expect unmodified pass,
  since the hardcoded-window logic under test is untouched.
- `npm run build` + `npm run lint` after trimming the hook and regenerating the
  client.
- Repo-wide grep for `dateFrom`/`dateTo` scoped to `productMargins_` /
  `useProductMarginsQuery` to confirm no stray reference survives (already
  clean per plan step; re-verify post-edit).
