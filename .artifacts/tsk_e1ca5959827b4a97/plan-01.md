# Plan — Replace raw `http.fetch` bypass in `useManufactureOutput` & `useSemiproductRecipePdf`

## Summary

Two Manufacture-module hooks reach into private internals of the NSwag-generated
`ApiClient` (`(apiClient as any).baseUrl`, `(apiClient as any).http.fetch`) instead of
calling the already-generated typed methods, and one of them hand-declares response
interfaces that duplicate generated types. Replace both call sites with the generated
methods and generated types, per `docs/development/api-client-generation.md`.

## Context

`docs/development/api-client-generation.md` explicitly forbids `(apiClient as any)` access
to private fields (lines 212-219, 274) because NSwag regenerates the client on every build;
a silent rename of `baseUrl`/`http` breaks these hooks at runtime with no compiler signal.
The generated client already exposes typed equivalents:

- `ApiClient.manufactureOutput_GetManufactureOutput(monthsBack): Promise<GetManufactureOutputResponse>`
  (`frontend/src/api/generated/api-client.ts:7418`), with `GetManufactureOutputResponse` /
  `ManufactureOutputMonth` / `ProductContribution` / `ProductionDetail` at api-client.ts:30239+.
- `ApiClient.manufactureBatch_GetRecipePdf(productCode, batchSize): Promise<FileResponse>`
  (`frontend/src/api/generated/api-client.ts:6706`), where `FileResponse` is
  `{ data: Blob; status: number; fileName?: string; headers?: {...} }` (api-client.ts:43171).

Both generated methods already throw a structured error (via `throwException` /
`SwaggerException`) on non-2xx responses, so the hooks' manual `response.ok` checks
become unnecessary.

This is a single self-contained fix touching 2 hook files + their 3 consumer components
(imports only). No backend change, no new endpoints — pure frontend plumbing cleanup.

## Functional requirements

**FR-1 — `useManufactureOutput.ts` calls the generated method, not raw fetch.**
- Replace the `(apiClient as any).baseUrl` / `(apiClient as any).http.fetch` block with
  `apiClient.manufactureOutput_GetManufactureOutput(monthsBack)`.
- Delete the hand-declared `ManufactureOutputResponse`, `ManufactureOutputMonth`,
  `ProductContribution`, `ProductionDetail` interfaces (lines 4-31); use the generated
  `GetManufactureOutputResponse`, `ManufactureOutputMonth`, `ProductContribution`,
  `ProductionDetail` from `../generated/api-client` instead.
- Acceptance: `grep -rn "as any" frontend/src/api/hooks/useManufactureOutput.ts` returns
  nothing; the query's return type is `GetManufactureOutputResponse` (or `undefined`).

**FR-2 — `useSemiproductRecipePdf.ts` calls the generated method, not raw fetch.**
- Replace the `(apiClient as any).baseUrl` / `(apiClient as any).http.fetch` block with
  `apiClient.manufactureBatch_GetRecipePdf(productCode, batchSize)`.
- Use the returned `FileResponse.data` (already a `Blob`) directly instead of
  `response.blob()`; drop the manual `response.ok` / `response.status` check since the
  generated method throws on non-2xx.
- Acceptance: `grep -rn "as any" frontend/src/api/hooks/useSemiproductRecipePdf.ts`
  returns nothing; opening a recipe PDF from the Manufacture Batch Calculator still opens
  a new tab with the PDF blob.

**FR-3 — Consumers compile against generated types, not the deleted local ones.**
- `ManufactureOutput.tsx` and `ManufactureOutputModal.tsx` currently import
  `ManufactureOutputMonth` / `ProductContribution` from `../../api/hooks/useManufactureOutput`
  (the hand-declared ones being deleted in FR-1). Repoint these imports to
  `../../api/generated/api-client`.
- Generated types have optional array fields (`products?: ProductContribution[]`,
  `productionDetails?: ProductionDetail[]`, `months?: ManufactureOutputMonth[]`) where the
  hand-declared ones were required. Update the ~8 call sites in `ManufactureOutput.tsx`
  and `ManufactureOutputModal.tsx` that assume non-null (`data.months.map(...)`,
  `month.products.forEach(...)`, `monthData.productionDetails.filter(...)`, etc.) to use
  the codebase's existing null-safe convention (`?? []`), matching the pattern already
  used in `ManufacturedInventoryPage.tsx:295` (`data?.items ?? []`) and
  `ManufacturingStockAnalysis.tsx:218`.
- Acceptance: `npm run build` (frontend) succeeds with no TS errors in these three files.

**FR-4 — No behavior change.**
- `ManufactureOutput.tsx` chart rendering and `ManufactureOutputModal.tsx` drill-down
  table render identical data for the same API response.
- `ManufactureBatchCalculator.tsx`'s "download recipe PDF" action still opens the PDF in
  a new tab, still respects the optional `batchSize` query param.

## Non-functional requirements

- No behavioral or API surface change — this is a refactor for type safety and
  resilience to NSwag regeneration, not a feature change.
- Preserve existing `staleTime`/`retry` query config on `useManufactureOutputQuery`.
- Preserve existing loading/error state shape (`isLoading`, `error`) on
  `useSemiproductRecipePdf` so `ManufactureBatchCalculator.tsx` needs no changes beyond
  the hook internals.

## Data model

No new entities. Reuses existing generated types:
- `GetManufactureOutputResponse { months?: ManufactureOutputMonth[] }`
- `ManufactureOutputMonth { month?, totalOutput?, products?: ProductContribution[], productionDetails?: ProductionDetail[] }`
- `ProductContribution { productCode?, productName?, quantity?, difficulty?, weightedValue? }`
- `ProductionDetail { productCode?, productName?, date?: Date, amount?, pricePerPiece?, priceTotal?, documentNumber? }`
- `FileResponse { data: Blob, status: number, fileName?, headers? }`

One field-shape note: generated `ProductionDetail.date` is typed `Date` (NSwag converts
ISO date strings to `Date` objects via `fromJS`), whereas the hand-declared interface
typed it `string`. Any formatting code in `ManufactureOutputModal.tsx` that treats
`productionDetail.date` as a string needs to go through `.toISOString()` /
`date-fns`/whatever formatter the codebase already uses for `Date` fields elsewhere —
check for a `.toLocaleDateString()` or existing date-formatting helper before assuming
plain string interpolation still works.

## Interfaces

No endpoint changes. Existing REST endpoints, now called through the typed client:
- `GET /api/manufacture-output?monthsBack={n}` → `manufactureOutput_GetManufactureOutput`
- `GET /api/manufacture-batch/recipe-pdf/{productCode}?batchSize={n}` → `manufactureBatch_GetRecipePdf`

## Dependencies and scope

**In scope:**
- `frontend/src/api/hooks/useManufactureOutput.ts`
- `frontend/src/api/hooks/useSemiproductRecipePdf.ts`
- `frontend/src/components/pages/ManufactureOutput.tsx` (import fix + null-safety)
- `frontend/src/components/pages/ManufactureOutputModal.tsx` (import fix + null-safety + `date` type)
- `frontend/src/components/pages/ManufactureBatchCalculator.tsx` — verify only; expected
  to need no changes since it consumes the hook's public `{ openRecipePdf, isLoading, error }`
  surface, which is unchanged.

**Out of scope:**
- Sibling Manufacture hooks already tracked separately: `useManufactureOrders` (#3797),
  `useManufacturedProductInventory` / `useMaterials` (#3802),
  `useManufacturingStockAnalysis` (#3730). Do not touch these files.
- Backend/API contract — no controller or DTO changes.
- Any regeneration of `api-client.ts` — the generated methods already exist; this task
  only changes callers.

## Rough plan

1. Rewrite `useManufactureOutput.ts`: drop hand-declared interfaces, import generated
   types, call `apiClient.manufactureOutput_GetManufactureOutput(monthsBack)` directly
   (drop the unnecessary `await` on the synchronous `getAuthenticatedApiClient()` call
   while touching this line — pre-existing minor bug, same call site).
2. Rewrite `useSemiproductRecipePdf.ts`: call
   `apiClient.manufactureBatch_GetRecipePdf(productCode, batchSize ?? undefined)`,
   use `.data` blob directly, drop the now-redundant `response.ok` check.
3. Update imports in `ManufactureOutput.tsx` and `ManufactureOutputModal.tsx` to pull
   `ManufactureOutputMonth` / `ProductContribution` / `ProductionDetail` from
   `../../api/generated/api-client`; add `?? []` guards at each now-optional array
   access; confirm how `ProductionDetail.date` (now `Date`, was `string`) is displayed
   and adjust formatting if it's interpolated as a string today.
4. `npm run build` and `npm run lint` in `frontend/` — zero TS errors, zero new lint
   warnings in touched files.
5. Manually sanity-check in a running dev instance (or via existing component tests, if
   any): Manufacture Output page renders the chart and monthly drill-down modal; Batch
   Calculator's recipe PDF button still opens a PDF.
6. Grep the whole repo for `(apiClient as any)` scoped to these two files to confirm zero
   remaining hits.

## Open questions

- **`ProductionDetail.date` type change (string → Date):** the hand-declared interface
  said `string`; the generated type is `Date`. Resolved by design step: inspect actual
  rendering code in `ManufactureOutputModal.tsx` and adapt formatting — no user-facing
  ambiguity, just needs verification during implementation.
- **No existing component/unit tests found for these two hooks or their consumers** (not
  verified beyond a grep in this step) — default to compile+lint+manual smoke check per
  `CLAUDE.md` validation rules; flag to the user if E2E coverage for Manufacture Output
  is desired, since E2E runs nightly and won't gate this PR.
