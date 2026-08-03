# Plan — Manufacture: replace `(apiClient as any).http.fetch` in `useManufacturedProductInventory` / `useMaterials` with the generated client

## Summary
`frontend/src/api/hooks/useManufacturedProductInventory.ts` and `frontend/src/api/hooks/useMaterials.ts` hand-roll every HTTP call by casting `getAuthenticatedApiClient()` to `any` and reaching into its private `.baseUrl`/`.http.fetch` fields, and hand-declare DTOs/enums that duplicate types the NSwag-generated client already exports. The fix replaces all five hand-rolled calls (4 in the manufactured-inventory hook, 1 effectively duplicated in the materials hook's two exported functions) with the typed generated methods `manufacturedProductInventory_GetInventory/CreateItem/UpdateItem/DeleteItem` and `catalog_GetMaterialsForPurchase`, and removes the hand-coded type declarations in favour of the generated ones.

## Context
This is the same defect class already fixed per-module in #3494, #3442, #3333, #3395, #3221, #2628, #2500, #1659/#1826, and open elsewhere in #3797/#3730. `docs/development/api-client-generation.md` explicitly forbids `(apiClient as any)` (line 274) and the `.http.fetch`/`.baseUrl` private-field pattern (lines 212–215), instructing callers to use `getAuthenticatedApiClient()`'s typed methods or, as an escape hatch, `getApiBaseUrl()` + `getAuthenticatedFetch()` from `./client`.

Verified during investigation (not just asserted by the issue): the generated client (`frontend/src/api/generated/api-client.ts`) already has full 1:1 typed coverage for every call these two hooks make — `manufacturedProductInventory_GetInventory` (line 6749), `_CreateItem` (6799), `_UpdateItem` (6837), `_DeleteItem` (6878), and `catalog_GetMaterialsForPurchase` (line 1979). No backend/API changes are needed.

**A real type-drift bug was found while investigating, not just a style violation:** the hand-coded `InventoryChangeType` enum in `useManufacturedProductInventory.ts` uses **numeric** values (`InitialWriteDown = 1`, …), while the generated `InventoryChangeType` enum (api-client.ts:27957) uses **string** values (`InitialWriteDown = "InitialWriteDown"`, …) because the backend serializes it as a JSON string enum. Any code today comparing a log entry's `changeType` against the hand-coded numeric constants is silently comparing against the wrong value — this is exactly the kind of runtime drift the issue warns about. The only current usage site (`ManufacturedInventoryPage.tsx`'s `changeTypeLabels` object) happens to use the enum members as computed keys, which works under either numbering scheme, so this hasn't visibly broken anything yet — but it's a live landmine.

**A second, previously-unflagged mismatch was found:** the hand-coded `ManufacturedProductInventoryItem`/`...Log` interfaces type `createdAt`, `expirationDate`, `lastModifiedAt`, and `timestamp` as `string`, but the generated DTOs type them as `Date` (NSwag's Fetch template parses ISO date strings into `Date` objects — see `docs/development/api-client-generation.md` "Date handling: ISO 8601 strings converted to `Date` objects"). Several consumers already assume the generated (`Date`) shape in places (`TransportBoxItems.tsx:409` calls `.toISOString()` on a same-named field of a *different* DTO), while others assume the hand-coded (`string`) shape (`ManufacturedInventoryPage.tsx`'s `formatDate`/`formatDateTime` helpers take `string`, `TransportBoxItems.tsx:48` interpolates `item.expirationDate` directly as text). Adopting the generated types will flip these fields from `string` to `Date` everywhere `ManufacturedProductInventoryItem` is used, and every render/format site that touches a date field must be updated in lockstep or it will render `Date.toString()` output (`"Tue Jan 01 2027 00:00:00 GMT+0100…"`) instead of a formatted date.

## Functional requirements

**FR-1 — `useManufacturedProductInventory.ts`: replace all 4 hand-rolled fetches with generated methods.**
- `useManufacturedProductInventoryQuery`: call `apiClient.manufacturedProductInventory_GetInventory(filters.search, filters.onlyWithStock, filters.manufactureOrderId, filters.page, filters.pageSize)` instead of building a querystring and calling `apiFetch`.
- `useCreateManufacturedProductInventoryItem`: call `apiClient.manufacturedProductInventory_CreateItem(new CreateManufacturedInventoryItemRequest(input))`.
- `useUpdateManufacturedProductInventoryItem`: call `apiClient.manufacturedProductInventory_UpdateItem(input.id, new UpdateManufacturedInventoryItemBody({ newAmount: input.newAmount, note: input.note }))`.
- `useDeleteManufacturedProductInventoryItem`: call `apiClient.manufacturedProductInventory_DeleteItem(id, note)`.
- Acceptance: `getClientAndBaseUrl`, `apiFetch`, and `buildFilterParams` helpers are deleted; no `as any` remains in the file; `getAuthenticatedApiClient()` is called with no cast.

**FR-2 — `useMaterials.ts`: replace both hand-rolled fetches with `catalog_GetMaterialsForPurchase`.**
- `useMaterialsForPurchase`: call `apiClient.catalog_GetMaterialsForPurchase(searchTerm, limit)`.
- `useMaterialByProductCode`: call `apiClient.catalog_GetMaterialsForPurchase(productCode, 50)`, keep the existing client-side exact-match filter on the result.
- Acceptance: no `as any`, no manual `URLSearchParams`/`fullUrl` construction remains in the file.

**FR-3 — Delete hand-coded DTOs/enum, source types from the generated client.**
- Remove the local `InventoryChangeType` enum, `ManufacturedProductInventoryLog`, `ManufacturedProductInventoryItem`, `ManufacturedInventoryResponse` interfaces from `useManufacturedProductInventory.ts`; remove the local `MaterialForPurchaseDto`, `GetMaterialsForPurchaseResponse` interfaces from `useMaterials.ts`.
- `ManufacturedInventoryFilters`, `CreateManufacturedInventoryItemInput`, `UpdateManufacturedInventoryItemInput` (the hooks' own *input* shapes, not response DTOs) may stay as hand-written interfaces — they aren't wire types, they're this hook module's public call signature, and mapping them into the generated `Request`/`Body` classes at the call site is enough to close the gap named in the issue.
- Re-export the generated response/item types under the **existing exported names** so the 12+ downstream files that `import { ManufacturedProductInventoryItem, InventoryChangeType, ... } from ".../useManufacturedProductInventory"` and the 9+ files that `import { MaterialForPurchaseDto } from ".../useMaterials"` don't need their import statements touched:
  ```typescript
  export type { InventoryChangeType } from "../generated/api-client"; // now a string enum — re-export the value, not just the type, since consumers use it as `InventoryChangeType.X`
  export type ManufacturedProductInventoryItem = IManufacturedProductInventoryItemDto;
  export type ManufacturedProductInventoryLog = IManufacturedProductInventoryLogDto;
  ```
  ```typescript
  export type MaterialForPurchaseDto = IMaterialForPurchaseDto;
  ```
  Alias to the generated **`I*` interfaces**, not the generated classes — the classes carry `init()`/`toJSON()` instance methods, and several call sites build plain object literals typed as these names (`PurchaseOrderForm.tsx:368,423`, `CatalogAutocompleteAdapters.ts:10`, `OverdraftSheet.test.tsx:6`, `AmountEntrySheet.test.tsx:6`, `AddItemsStep.test.tsx:10`, `BoxFillWorkflow.test.tsx:11`) — a plain object literal does not structurally satisfy a class type that declares methods, so aliasing to the class would break these sites at compile time.
  - `InventoryChangeType` needs a real (value) export, not `export type`, since `ManufacturedInventoryPage.tsx` uses it as a value (`InventoryChangeType.InitialWriteDown`).
- Acceptance: `tsc`/`npm run build` passes with zero new `any`; grep for `MaterialForPurchaseDto`/`ManufacturedProductInventoryItem`/`InventoryChangeType` declarations shows them only in `generated/api-client.ts` and as re-exports in the two hook files, never hand-declared.

**FR-4 — Fix the date-field fallout at every render/format site touching the now-`Date`-typed fields.**
Files that must change because `createdAt`/`expirationDate`/`lastModifiedAt`/`timestamp` flip from `string` to `Date | undefined`:
- `ManufacturedInventoryPage.tsx`: `formatDate`/`formatDateTime` helpers currently take `(dateStr?: string)` and do `new Date(dateStr)`. Change signature to accept `Date | undefined` (passing a `Date` into `new Date(existingDate)` still works but is redundant/misleading — change to format the `Date` directly, or keep `new Date(...)` only if it's cheap to leave as-is; prefer changing the parameter type and dropping the redundant re-wrap).
- `TransportBoxItems.tsx:48`: `{item.expirationDate}` — currently renders the raw string; once `expirationDate` is a `Date`, this must format it (e.g. `item.expirationDate.toISOString().slice(0, 10)`, matching the pattern already used two functions away at line 409 for the sibling transport-box-item type) instead of relying on implicit `Date.toString()`.
- `BoxFillWorkflow.tsx:107` and `AddItemsStep.tsx:57`: both pass `item.expirationDate` straight into `addItem.mutateAsync({..., expirationDate: item.expirationDate, ...})`. Check `useAddBoxItem`'s mutation input type in `frontend/src/api/hooks/useTransportBoxes.ts` — the generated `AddItemToBoxRequest.expirationDate` is `Date | undefined` (api-client.ts:42713), so if `useAddBoxItem`'s own input type currently declares `expirationDate?: string` (matching today's hand-coded `ManufacturedProductInventoryItem`), that hook's input type has been silently accepting the wrong shape and needs its own one-line type fix as a consequence of this change, even though `useTransportBoxes.ts` itself is otherwise out of scope. Verify and fix only if the type-checker actually flags it — don't touch that file speculatively.
- `OverdraftSheet.test.tsx`, `AmountEntrySheet.test.tsx`, `AddItemsStep.test.tsx`, `BoxFillWorkflow.test.tsx`: fixture literals currently set `createdAt: "", expirationDate: "2027-01-01"` — change to `createdAt: new Date(), expirationDate: new Date("2027-01-01")` (or omit, since both are optional) to keep compiling against the new type.
- Acceptance: `npm run build` (which runs `tsc`) reports zero type errors in every file listed above; no file silently renders `[object Object]` or a raw `Date.toString()` where a formatted date is expected.

**FR-5 — Preserve external behavior.**
- Request/response wire shape, query params, and HTTP methods/paths must stay byte-identical to what the hand-rolled `fetch` calls sent today (`GET/POST/PUT/DELETE /api/manufactured-inventory[...]`, `GET /api/Catalog/materials-for-purchase`) — confirmed identical by comparing the hand-rolled URLs against the generated methods' `url_` construction.
- `useMaterialsForPurchase`'s `enabled: true` and both hooks' `staleTime`/`gcTime` values are unchanged.
- Mutation `onSuccess` cache invalidation (`QUERY_KEYS.manufacturedProductInventory`) is unchanged.
- Error handling: today's hand-rolled `apiFetch` throws a generic `Error("HTTP error! status: ...")` on non-2xx. The generated client's `process...` methods throw `ApiException`/`throwException(...)` with richer info (status, response body, headers) on any status other than 200/204 — this is a strictly more informative error, not a behavior regression, but any caller doing `catch (e) { if (e.message === "HTTP error! status: 404") ... }` string-matching would break. Grep confirmed no such string-matching exists today (see Dependencies/scope below).

## Non-functional requirements
- **Type safety**: zero `as any` remaining in either hook file; this is the whole point of the change.
- **No behavior change** in loading states, cache keys, retry/staleness config, or the shape of data rendered to the user (beyond the date-formatting fixes required by FR-4, which restore *correct* formatting, not new behavior).
- **No backend changes** — purely a frontend client-consumption fix; the generated client already covers every endpoint used.

## Data model
No new entities. Reused generated types:
- `GetManufacturedInventoryResponse { items?: ManufacturedProductInventoryItemDto[]; totalCount?: number } extends BaseResponse`
- `ManufacturedProductInventoryItemDto { id, productCode, productName, lotNumber?, expirationDate?: Date, amount, manufactureOrderId?, createdAt: Date, createdBy, lastModifiedAt?: Date, lastModifiedBy?, log: ManufacturedProductInventoryLogDto[] }`
- `ManufacturedProductInventoryLogDto { id, inventoryItemId, changeType: InventoryChangeType, amountDelta, amountAfter, referenceType?, referenceId?, note?, timestamp: Date, user }`
- `InventoryChangeType` (string enum): `InitialWriteDown | ConsumedByTransportBox | RestoredFromTransportBox | ManualAdjustment | ManualRemoval | ManualAddition`
- `CreateManufacturedInventoryItemRequest`, `UpdateManufacturedInventoryItemBody`, `CreateManufacturedInventoryItemResponse { item?: ManufacturedProductInventoryItemDto }`, `UpdateManufacturedInventoryItemResponse { item?: ... }`, `DeleteManufacturedInventoryItemResponse` (envelope only)
- `GetMaterialsForPurchaseResponse { materials?: MaterialForPurchaseDto[] } extends BaseResponse`
- `MaterialForPurchaseDto { productCode, productName, productType, lastPurchasePrice?, location?, currentStock, minimalOrderQuantity? }` — field-for-field identical to today's hand-coded interface, so this one carries no drift risk, only the class-vs-interface aliasing concern from FR-3.

## Interfaces
No new/changed backend endpoints — all five already exist and are already exercised by these hooks today:
- `GET /api/manufactured-inventory` (search, onlyWithStock, manufactureOrderId, page, pageSize)
- `POST /api/manufactured-inventory`
- `PUT /api/manufactured-inventory/{id}`
- `DELETE /api/manufactured-inventory/{id}?note=`
- `GET /api/Catalog/materials-for-purchase?searchTerm=&limit=`

UI flows are unchanged — `ManufacturedInventoryPage`, `TransportBoxItems`/`TransportBoxTypes`, the `box-fill` terminal flow (`BoxFillWorkflow`, `AddItemsStep`, `AmountEntrySheet`, `OverdraftSheet`, `BoxFillBody`), and the purchase-order material-picking flow (`PurchaseOrderForm`, `MaterialAutocomplete`, `CatalogAutocompleteAdapters`, `MaterialResolver`) all keep their current props/behavior; only the underlying type source changes.

## Dependencies and scope

**In scope:**
- `frontend/src/api/hooks/useManufacturedProductInventory.ts` (full rewrite of the fetch layer + type re-exports)
- `frontend/src/api/hooks/useMaterials.ts` (full rewrite of the fetch layer + type re-exports)
- Every file in the FR-4 list, strictly to fix compile errors/date-rendering fallout caused by the type change (not a broader refactor of those files)
- Verified downstream import sites (no changes expected beyond FR-4, since names are preserved via re-export): `TransportBoxTypes.tsx`, `TransportBoxItems.tsx`, `OverdraftSheet.tsx`, `BoxFillWorkflow.tsx`, `BoxFillBody.tsx`, `AmountEntrySheet.tsx`, `AddItemsStep.tsx`, `ManufacturedInventoryPage.tsx`, `PurchaseOrderTypes.tsx`, `PurchaseOrderHelpers.tsx`, `PurchaseOrderForm.tsx`, `AddItemToBoxModal.tsx`, `CatalogAutocompleteAdapters.ts`, `MaterialAutocomplete.tsx`, `MaterialResolver.tsx`, and their test files.

**Out of scope:**
- Any other `(apiClient as any)` occurrence in the codebase not in these two files (tracked separately per #3797/#3730 and prior module-specific issues).
- `frontend/src/api/hooks/useTransportBoxes.ts` and the transport-box item type it owns — touched only if `tsc` actually flags a mismatch from FR-4's date-type propagation into `useAddBoxItem`'s input, and then only the minimal type fix, not a rewrite.
- Backend `ManufacturedProductInventoryController` / `CatalogController` — no server-side change; the generated client is already correct.
- Fixing the `InventoryChangeType` numeric-vs-string drift is a **side effect** of switching to the generated enum, not separate scoped work — no code currently depends on the numeric values in a way that requires a migration step (confirmed: only usage is as object keys, which is numbering-scheme-agnostic).

## Rough plan
1. In `useManufacturedProductInventory.ts`: import `CreateManufacturedInventoryItemRequest`, `UpdateManufacturedInventoryItemBody`, `GetManufacturedInventoryResponse`, `ManufacturedProductInventoryItemDto`, `IManufacturedProductInventoryItemDto`, `ManufacturedProductInventoryLogDto`, `IManufacturedProductInventoryLogDto`, `InventoryChangeType` from `../generated/api-client`. Delete the hand-coded enum/interfaces, replace with the re-export aliases from FR-3.
2. Rewrite the four hook bodies to call `getAuthenticatedApiClient()` directly and invoke the matching generated method (no cast, no manual URL/query building). Delete `getClientAndBaseUrl`, `apiFetch`, `buildFilterParams`.
3. Repeat steps 1–2 for `useMaterials.ts` against `catalog_GetMaterialsForPurchase`, `GetMaterialsForPurchaseResponse`, `MaterialForPurchaseDto`/`IMaterialForPurchaseDto`.
4. Run `npm run build` (which also regenerates the client via `prebuild`, though no backend change is expected to alter it) and fix every resulting type error — expect them concentrated in the FR-4 file list (date fields) and confirm no others surface.
5. Manually re-verify formatted dates: `ManufacturedInventoryPage` list/detail dates, `TransportBoxItems`'s manufactured-item expiration display, and the box-fill add-item flow's expiration display all still show a short date, not a `Date.toString()` dump.
6. Update the four test fixture files (FR-4) to construct `Date` objects for date fields; run the frontend unit test suite for the touched components (`ManufacturedInventoryPage`, `TransportBoxItems`, `BoxFillWorkflow`, `AddItemsStep`, `AmountEntrySheet`, `OverdraftSheet`, `PurchaseOrderValidation`, `PurchaseOrderHelpers`) and fix any new failures.
7. `npm run lint` and `npm run build` clean; `dotnet build`/`dotnet format` are not expected to be touched (no backend change) but confirm nothing in the repo-wide validation step regresses.
8. Grep the two changed files and their re-exports for `as any` / `.http.fetch` / `.baseUrl` to confirm zero remain, matching the enforcement rule in `api-client-generation.md`.

## Open questions
1. **`InventoryChangeType` numeric→string drift**: is any *out-of-repo* consumer (e.g. a saved report, an external integration, a persisted numeric value in local storage) depending on the old numeric values? Nothing in this repo does (verified), so default is to proceed with the generated string enum and treat this as a bugfix side effect, not a breaking change requiring a migration. Flag if the reviewer knows of an external dependency.
2. **`useAddBoxItem` input type** (`useTransportBoxes.ts`): default is "touch only if `tsc` forces it, minimal fix." If the reviewer wants that hook's `expirationDate` typing audited proactively regardless of whether this change forces a compile error, that's separate scope — say so explicitly.
3. **Alias via `I*` interface vs. wrapping in `new ...Dto(...)`**: default is aliasing the exported type name to the generated `I*` interface (zero consumer changes beyond date fields). The alternative — constructing real class instances (`ManufacturedProductInventoryItemDto.fromJS(...)`) everywhere and updating every plain-object-literal construction site to match — is more "canonically generated-client" but touches strictly more files for no behavioral gain here (these DTOs have no client-side computed methods beyond `toJSON`/`init`, which nothing here needs). Flag if the reviewer prefers the stricter class-based approach for consistency with other already-fixed modules — worth a quick check of how #3494/#3442/#3333 handled this exact class-vs-interface tension before implementing, since precedent should win over a fresh judgment call.
