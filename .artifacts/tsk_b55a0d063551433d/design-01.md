# Design — Manufacture: replace `(apiClient as any).http.fetch` in `useManufacturedProductInventory` / `useMaterials` with the generated client

No UI section: this is a pure data-layer refactor. Every consuming component (`ManufacturedInventoryPage`, `TransportBoxItems`, `BoxFillWorkflow`, `AddItemsStep`, `PurchaseOrderForm`, etc.) keeps its current props, layout, and interaction behavior — only the hook implementation and the source of its types change. FR-4's date-formatting fixes restore correct rendering of values these components already display; they don't add or remove any UI element.

## Component design

### Module boundaries (unchanged)
Both files stay hooks-layer modules under `frontend/src/api/hooks/`, consumed the same way by the same call sites. Nothing moves across module boundaries; this design only changes what's *inside* the two files.

### `useManufacturedProductInventory.ts`

**Responsibility:** expose 4 React Query hooks (1 query, 3 mutations) for `/api/manufactured-inventory`, and be the single source from which downstream code imports the item/log/enum types.

**Before → after per hook:**

| Hook | Before | After |
|---|---|---|
| `useManufacturedProductInventoryQuery(filters)` | builds `URLSearchParams` manually, calls `apiFetch(apiClient, url, {method:"GET"})`, `response.json()` cast to hand-rolled `ManufacturedInventoryResponse` | `apiClient.manufacturedProductInventory_GetInventory(filters.search, filters.onlyWithStock, filters.manufactureOrderId, filters.page, filters.pageSize)` → returns typed `GetManufacturedInventoryResponse` directly |
| `useCreateManufacturedProductInventoryItem()` | `apiFetch(..., {method:"POST", body: JSON.stringify(input)})` | `apiClient.manufacturedProductInventory_CreateItem(new CreateManufacturedInventoryItemRequest(input))` |
| `useUpdateManufacturedProductInventoryItem()` | `apiFetch(..., {method:"PUT", body: JSON.stringify({newAmount, note})})` | `apiClient.manufacturedProductInventory_UpdateItem(input.id, new UpdateManufacturedInventoryItemBody({newAmount: input.newAmount, note: input.note}))` |
| `useDeleteManufacturedProductInventoryItem()` | builds `URLSearchParams`, `apiFetch(..., {method:"DELETE"})`, discards response | `apiClient.manufacturedProductInventory_DeleteItem(id, note)` |

Deleted entirely: `getClientAndBaseUrl()`, `apiFetch()`, `buildFilterParams()` — the generated method calls replace them 1:1, so there is no reduced helper, just a smaller file.

`getAuthenticatedApiClient()` is called directly inside each `queryFn`/`mutationFn`, with no cast, exactly as `usePackingMaterials.ts` already does (confirmed as the established pattern for already-fixed modules).

**Type surface exposed by this module (its public interface to the rest of the app):**
```typescript
import {
  CreateManufacturedInventoryItemRequest,
  UpdateManufacturedInventoryItemBody,
  GetManufacturedInventoryResponse,
  InventoryChangeType,
  ManufacturedProductInventoryItemDto,
  IManufacturedProductInventoryItemDto,
  ManufacturedProductInventoryLogDto,
  IManufacturedProductInventoryLogDto,
} from "../generated/api-client";

export { InventoryChangeType }; // value export — consumers use InventoryChangeType.InitialWriteDown
export type ManufacturedProductInventoryItem = IManufacturedProductInventoryItemDto;
export type ManufacturedProductInventoryLog = IManufacturedProductInventoryLogDto;

// unchanged, hand-written — these are the hook module's own call-signature types,
// not wire DTOs, so they don't come from the generated client:
export interface ManufacturedInventoryFilters { search?: string; onlyWithStock?: boolean; manufactureOrderId?: number; page?: number; pageSize?: number; }
export interface CreateManufacturedInventoryItemInput { productCode: string; productName: string; amount: number; lotNumber?: string; expirationDate?: string; manufactureOrderId?: number; }
export interface UpdateManufacturedInventoryItemInput { id: number; newAmount: number; note?: string; }
```
`ManufacturedInventoryResponse` (the old private `{items, totalCount}` interface) is deleted with no replacement export — nothing outside the file imported it (only `GetManufacturedInventoryResponse`'s shape, `{items?, totalCount?}`, is used, via the query's return type).

Note the input types (`CreateManufacturedInventoryItemInput` etc.) keep `expirationDate?: string` — they're caller-facing convenience shapes; the hook itself does the `string → Date` lift by passing them into `new CreateManufacturedInventoryItemRequest(input)`, whose `init()` (see Data schemas) already coerces `expirationDate` from a wire string. Since these are constructed from plain JS objects in-hook (not deserialized JSON), the constructor's `for...in` copy assigns the string as-is to a field typed `Date | undefined` — this is the one place the design accepts a pre-existing minor type looseness in the generated client's own constructor rather than adding a manual `new Date(...)` conversion, because it matches the pattern already used by `usePackingMaterials.ts`'s `UpdateQuantityRequest` (`date: new Date(date)`  is done *explicitly* there — see open point below).

**Design decision — explicit `Date` construction at the call site.** To avoid relying on the constructor's untyped passthrough, the create/update call sites convert `expirationDate` explicitly:
```typescript
new CreateManufacturedInventoryItemRequest({
  ...input,
  expirationDate: input.expirationDate ? new Date(input.expirationDate) : undefined,
})
```
This mirrors `usePackingMaterials.ts:100-103`'s `date: new Date(date)` precedent and keeps the input-to-wire conversion visible and type-correct rather than depending on an implicit `any`-cast inside the generated class.

### `useMaterials.ts`

**Responsibility:** expose 2 React Query hooks for `/api/Catalog/materials-for-purchase`, and re-export the material DTO type.

| Hook | Before | After |
|---|---|---|
| `useMaterialsForPurchase(searchTerm, limit)` | builds `URLSearchParams`, `(apiClient as any).http.fetch(fullUrl, {method:"GET"})`, casts JSON to hand-rolled `GetMaterialsForPurchaseResponse` | `apiClient.catalog_GetMaterialsForPurchase(searchTerm, limit)` |
| `useMaterialByProductCode(productCode)` | same hand-rolled fetch with `searchTerm=productCode, limit=50`, then client-side `.find(exact match)` | `apiClient.catalog_GetMaterialsForPurchase(productCode, 50)`, same client-side `.find(...)` unchanged (this filtering logic is legitimate hook logic, not a client-generation concern, and stays as-is) |

Deleted: the local `MaterialForPurchaseDto` / `GetMaterialsForPurchaseResponse` interfaces (comment `// Temporary types since API client is incomplete` is now false — the client covers this endpoint — so the comment goes too).

**Type surface exposed by this module:**
```typescript
import { IMaterialForPurchaseDto } from "../generated/api-client";
export type MaterialForPurchaseDto = IMaterialForPurchaseDto;
```
`GetMaterialsForPurchaseResponse` was never imported by any consumer (only `MaterialForPurchaseDto` is), so it is not re-exported — the hooks use the generated `GetMaterialsForPurchaseResponse` internally as the query's inferred return type only.

### Why alias to `I*` interfaces, not the generated classes

Verified call sites that build plain object literals typed as these names, which would fail structural typing against a class (classes declare `init`/`toJSON` methods a literal doesn't implement):
- `PurchaseOrderForm.tsx:368` — `const materialToUse: MaterialForPurchaseDto = material || { ...literal }`
- `PurchaseOrderForm.tsx:423` — `const material: MaterialForPurchaseDto = { ...literal }`
- `CatalogAutocompleteAdapters.ts:10` — arrow function returning `MaterialForPurchaseDto` as an object literal
- Test fixtures: `OverdraftSheet.test.tsx`, `AmountEntrySheet.test.tsx`, `AddItemsStep.test.tsx`, `BoxFillWorkflow.test.tsx`

This differs from `usePackingMaterials.ts`'s precedent (re-exports the generated *classes* and casts inputs `as CreatePackingMaterialRequest`), because that module's consumers don't construct plain-literal DTOs typed against the response shape — only request payloads, which are already being wrapped in `new ...Request(...)` at the call site here. The interface-alias choice is local to these two files' actual usage, not a deviation from house style for its own sake.

## Data schemas

All shapes below already exist in `frontend/src/api/generated/api-client.ts` — no backend or codegen change. Listed for the exact fields this design binds against.

### `GET /api/manufactured-inventory` (query params: `search?, onlyWithStock?, manufactureOrderId?, page?, pageSize?`)
```typescript
interface GetManufacturedInventoryResponse extends BaseResponse {
  items?: ManufacturedProductInventoryItemDto[];
  totalCount?: number;
}
interface IManufacturedProductInventoryItemDto {
  id?: number;
  productCode?: string;
  productName?: string;
  lotNumber?: string;
  expirationDate?: Date;      // was string in the hand-coded type
  amount?: number;
  manufactureOrderId?: number;
  createdAt?: Date;           // was string
  createdBy?: string;
  lastModifiedAt?: Date;      // was string
  lastModifiedBy?: string;
  log?: ManufacturedProductInventoryLogDto[];
}
interface IManufacturedProductInventoryLogDto {
  id?: number;
  inventoryItemId?: number;
  changeType?: InventoryChangeType;  // was numeric; generated enum is a STRING enum
  amountDelta?: number;
  amountAfter?: number;
  referenceType?: string;
  referenceId?: string;
  note?: string;
  timestamp?: Date;           // was string
  user?: string;
}
enum InventoryChangeType {
  InitialWriteDown = "InitialWriteDown",
  ConsumedByTransportBox = "ConsumedByTransportBox",
  RestoredFromTransportBox = "RestoredFromTransportBox",
  ManualAdjustment = "ManualAdjustment",
  ManualRemoval = "ManualRemoval",
  ManualAddition = "ManualAddition",
}
```

### `POST /api/manufactured-inventory`
```typescript
// request body
class CreateManufacturedInventoryItemRequest {
  productCode?: string;
  productName?: string;
  amount?: number;
  lotNumber?: string;
  expirationDate?: Date;
  manufactureOrderId?: number;
}
// response
interface ICreateManufacturedInventoryItemResponse extends IBaseResponse {
  item?: ManufacturedProductInventoryItemDto;
}
```

### `PUT /api/manufactured-inventory/{id}`
```typescript
// request body
class UpdateManufacturedInventoryItemBody {
  newAmount?: number;
  note?: string;
}
// response
interface IUpdateManufacturedInventoryItemResponse extends IBaseResponse {
  item?: ManufacturedProductInventoryItemDto;
}
```

### `DELETE /api/manufactured-inventory/{id}?note=`
```typescript
// no request body; response is envelope-only
interface IDeleteManufacturedInventoryItemResponse extends IBaseResponse {}
```

### `GET /api/Catalog/materials-for-purchase` (query params: `searchTerm?, limit?`)
```typescript
interface IGetMaterialsForPurchaseResponse extends IBaseResponse {
  materials?: MaterialForPurchaseDto[];
}
interface IMaterialForPurchaseDto {
  productCode?: string;
  productName?: string;
  productType?: string;
  lastPurchasePrice?: number;
  location?: string;
  currentStock?: number;
  minimalOrderQuantity?: string;
}
```
Field-for-field identical to today's hand-coded `MaterialForPurchaseDto` — no drift here, only the source-of-truth change.

### Re-exported public type surface (frozen names, new source)
```
useManufacturedProductInventory.ts:
  InventoryChangeType          (value export, was local enum → now generated string enum)
  ManufacturedProductInventoryItem   = IManufacturedProductInventoryItemDto
  ManufacturedProductInventoryLog    = IManufacturedProductInventoryLogDto
  ManufacturedInventoryFilters       (unchanged, hand-written)
  CreateManufacturedInventoryItemInput (unchanged, hand-written)
  UpdateManufacturedInventoryItemInput (unchanged, hand-written)

useMaterials.ts:
  MaterialForPurchaseDto       = IMaterialForPurchaseDto
```
No consumer's import statement changes — only the field types of `ManufacturedProductInventoryItem`/`Log` (4 date fields, 1 enum) change underneath the same name, which is what drives FR-4's render-site fixes.

## Error handling contract (design-level, not just a note)
`process...` methods on the generated client throw `ApiException` (status, response text, headers) instead of the hand-rolled `Error("HTTP error! status: N")`. No call site in the FR-3/FR-4 scope catches by matching that message string (verified by grep during planning), so no additional error-mapping layer is introduced — callers that today do `useMutation({ onError })` or `isError` checks keep working unchanged since they don't inspect `error.message` content.
