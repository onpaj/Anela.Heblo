## Module
Invoices

## Finding
Two frontend hooks manually declare TypeScript interfaces for types that already exist in the auto-generated `api-client.ts`:

**`frontend/src/api/hooks/useIssuedInvoices.ts`** (lines 4–74) declares:
- `IssuedInvoicesFilters`, `IssuedInvoiceDto`, `IssuedInvoicesListResponse`, `IssuedInvoiceDetailDto`, `IssuedInvoiceItemDto`, `IssuedInvoiceSyncHistoryDto`, `IssuedInvoiceDetailResponse`

**`frontend/src/api/hooks/useAsyncInvoiceImport.ts`** (lines 5–31) declares:
- `EnqueueImportInvoicesRequest`, `EnqueueImportInvoicesResponse`, `BackgroundJobInfo`, `ImportResultDto`

All of these types are generated in `frontend/src/api/generated/api-client.ts` (lines ~3994–4393 cover the entire Invoices API surface). Neither hook imports anything from the generated file.

For comparison, `usePurchaseOrders.ts` correctly imports all its types from the generated client:
```ts
import { ApiClient as GeneratedApiClient, GetPurchaseOrdersResponse, ... } from "../generated/api-client";
```

## Why it matters
- **Type drift**: when the backend API changes, NSwag regenerates `api-client.ts` but the manually-declared interfaces in the hooks are not updated. Breakage may be silent (the TypeScript types diverge from the actual JSON shape)
- **Duplication**: two sources of truth for the same types, with no mechanism to keep them in sync
- **Inconsistency**: the established pattern (`usePurchaseOrders.ts`) imports generated types; these hooks reinvent them

## Suggested fix
Replace the manual interface declarations with imports from the generated client, e.g.:
```ts
import type {
  GetIssuedInvoicesListResponse,
  GetIssuedInvoiceDetailResponse,
  IssuedInvoiceDto,
} from "../generated/api-client";
```
Delete the duplicated hand-written interfaces. No logic changes are needed — only the type declarations.

---
_Filed by daily arch-review routine on 2026-05-29._