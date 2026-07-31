# Development — replace `(apiClient as any).http.fetch` in `useManufacturedProductInventory` / `useMaterials` with the generated client

## Summary

Implemented plan-01.md / design-01.md exactly as specified, folding in the architecture review's one required correction (unwrap `.item` from the mutation envelope so the hooks keep returning the bare item type). Both hooks now call the generated NSwag client (`manufacturedProductInventory_GetInventory/CreateItem/UpdateItem/DeleteItem`, `catalog_GetMaterialsForPurchase`) with no casts, and the hand-coded DTOs/enum are gone in favour of type aliases to the generated `I*` interfaces. This flips several fields from `string` to `Date` and makes every DTO field optional (matching the generated types), which required fixing render/call sites across the manufactured-inventory page, the transport-box item flow, and the terminal box-fill flow — exactly the fallout the plan predicted, plus one file the plan hadn't independently discovered (`src/api/hooks/useBoxFill.ts`, which turned out to have its own separate hand-rolled `AddBoxItemInput.expirationDate?: string`, structurally identical to `useTransportBoxes.ts`'s `AddItemToBoxInput`).

## Files changed

**Core fix (FR-1/FR-2/FR-3):**
- `frontend/src/api/hooks/useManufacturedProductInventory.ts` — full rewrite. Deleted `getClientAndBaseUrl`, `apiFetch`, `buildFilterParams`, the hand-coded `InventoryChangeType` enum and `ManufacturedProductInventoryLog`/`Item`/`ManufacturedInventoryResponse` interfaces. All 4 hooks now call `getAuthenticatedApiClient()` directly with the matching generated method. Re-exports `InventoryChangeType` (value export, from generated) and `ManufacturedProductInventoryItem`/`Log` as aliases to the generated `I*` interfaces. Create/update mutations construct `CreateManufacturedInventoryItemRequest`/`UpdateManufacturedInventoryItemBody` explicitly (with `new Date(...)` conversion for `expirationDate`, matching `usePackingMaterials.ts` precedent) and unwrap `result.item!` so the mutation's return type stays the bare item, not the envelope (per architecture-review point 2).
- `frontend/src/api/hooks/useMaterials.ts` — full rewrite. Deleted the "Temporary types" comment and both hand-coded interfaces. Both hooks call `catalog_GetMaterialsForPurchase` directly. `MaterialForPurchaseDto` re-exported as an alias to `IMaterialForPurchaseDto`.

**FR-4 fallout — all optional-field/Date-type fixes, no behavior change beyond correct date formatting:**
- `frontend/src/components/pages/ManufacturedInventoryPage.tsx` — `formatDate`/`formatDateTime` now take `Date | undefined` directly (no re-wrap in `new Date(...)`). Added `?? 0`/`!`/optional-chaining guards everywhere a field that is now `T | undefined` was previously assumed required (`item.id!`, `item.amount ?? 0`, `item.log ?? []`, `entry.timestamp?.getTime() ?? 0`, `entry.amountDelta ?? 0`, and a guarded lookup into `changeTypeLabels` since `entry.changeType` is now optional).
- `frontend/src/components/transport/box-detail/TransportBoxItems.tsx` — `item.expirationDate` render site now formats via `.toISOString().slice(0, 10)` (matching the existing sibling-type precedent at the same file's line 409) instead of implicit `Date.toString()`. Added `?? 0`/`?? ""` guards for `item.amount`/`item.productName`/`item.productCode` at the now-optional call sites (search filter, overdraft math, default-amount calc).
- `frontend/src/components/pages/TransportBoxDetail.tsx` — `handleAddManufacturedItem`: `item.productCode!`/`item.productName!` (always populated at runtime for a fetched item, matching the `.id!` pattern already used elsewhere in this codebase), and `item.expirationDate?.toISOString()` when persisting to `saveLastManufacturedItem` (whose `LastManufacturedEntry.expirationDate` stays a `string` — it's `localStorage`-serialized, so a `Date` field would not survive `JSON.parse` on reload; converting explicitly at this one call site is correct, not a re-hand-rolling of the DTO).
- `frontend/src/api/hooks/useTransportBoxes.ts` — `AddItemToBoxInput.expirationDate` widened from `string` to `Date` (one line). Confirmed safe: the field flows straight into `JSON.stringify(body)`, and `JSON.stringify` calls `Date.prototype.toJSON()` automatically, so the wire format is unchanged.
- `frontend/src/api/hooks/useBoxFill.ts` — same one-line widening for `AddBoxItemInput.expirationDate` (`string` → `Date`). This is a **second, separate** hand-rolled `boxFillRequest`/`JSON.stringify(input)` path (the terminal box-fill flow) that the plan's prose pointed at `useTransportBoxes.ts` for, but the actual consumers (`BoxFillWorkflow.tsx`, `AddItemsStep.tsx`) call `useAddBoxItem` from *this* file, not `useTransportBoxes.ts`. Verified by `grep` that `useAddBoxItem` is defined here. Same "type-only, zero wire-format change" justification applies (`JSON.stringify(input)` serializes the whole object, including the Date field, via `toJSON()`).
- `frontend/src/components/terminal/box-fill/BoxFillWorkflow.tsx` and `AddItemsStep.tsx` — `item.productCode!`/`item.productName!` at the `addItem.mutateAsync`/`onAmountUsed` call sites, `?? 0` guards for `.amount` comparisons and `amountMemory` indexing (`selected.productCode!`), matching the same pattern as `ManufacturedInventoryPage.tsx`.
- `frontend/src/components/terminal/box-fill/OverdraftSheet.tsx` — `missing = requestedAmount - (item.amount ?? 0)` guard.
- Test fixtures — `OverdraftSheet.test.tsx`, `AmountEntrySheet.test.tsx`, `AddItemsStep.test.tsx`, `BoxFillWorkflow.test.tsx`: `createdAt: ""` → `createdAt: new Date()`, `expirationDate: "2027-01-01"` → `expirationDate: new Date("2027-01-01")` (and the matching `addMutateAsync` call-expectation in `BoxFillWorkflow.test.tsx`).

**Confirmed no change needed** (checked, not touched): `TransportBoxItems.test.tsx` (uses the unrelated `TransportBoxItemDto`, not `ManufacturedProductInventoryItem`), `PurchaseOrderForm.tsx`/`CatalogAutocompleteAdapters.ts`/`PurchaseOrderHelpers.tsx`/`PurchaseOrderTypes.tsx`/`MaterialResolver.tsx` (all consume `MaterialForPurchaseDto`, whose field shapes are unchanged — no date/enum drift on that DTO), `BoxFillBody.tsx` (only reads optional string/number fields for display, no required-field assumption).

## Verification

- `npx tsc --noEmit` doesn't work standalone in this environment (a `react-i18next` version pulled by `--legacy-peer-deps` uses TS5 const-type-parameter syntax the pinned TS 4.9.5 parser can't parse — pre-existing environment issue, unrelated to this change; `react-scripts build`'s fork-ts-checker doesn't hit it). Used `CI=true npm run build` instead, which is the project's actual validation command.
- `CI=true npm run build` → **Compiled successfully**, zero TS errors.
- `npm run lint` → 175 pre-existing errors/13 warnings, all in files untouched by this change (dashboard/marketing/financial-overview/etc. test files with `testing-library/no-node-access` and similar rule violations that predate this work). Zero lint issues in any file this change touched.
- Targeted Jest run (`react-scripts test --testPathPattern=...` covering every FR-4-listed component/hook and their tests): **8 test suites, 84 passed, 2 skipped, 0 failed.** (The 2 skipped are pre-existing `it.skip`s unrelated to this change.)
- `grep -n "as any\|\.http\.fetch\|\.baseUrl" frontend/src/api/hooks/useManufacturedProductInventory.ts frontend/src/api/hooks/useMaterials.ts` → no matches, confirming the rule violation is fully resolved in both target files.
- No backend files changed (`git status --short` shows only the 14 frontend files listed above), so `dotnet build`/`dotnet format` were not run — nothing in this change touches C#.

## How to verify locally
```bash
cd frontend
npm ci --legacy-peer-deps   # environment note: plain `npm ci`/`npm install` fails with an ERESOLVE
                             # conflict between the pinned typescript@4.9.5 and react-i18next@15.7.4's
                             # peer requirement on typescript ^5 — pre-existing, unrelated to this change
CI=true npm run build
npm run lint
CI=true npx react-scripts test --watchAll=false --testPathPattern="(ManufacturedInventoryPage|TransportBoxItems|TransportBoxDetail|BoxFillWorkflow|AddItemsStep|AmountEntrySheet|OverdraftSheet|PurchaseOrderValidation|PurchaseOrderHelpers)"
```
