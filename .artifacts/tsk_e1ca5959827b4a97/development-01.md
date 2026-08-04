# Development — Replace raw `http.fetch` bypass in `useManufactureOutput` & `useSemiproductRecipePdf`

## Summary

Implemented plan-01.md / design-01.md exactly as designed: both hooks now call the
generated, typed `ApiClient` methods instead of reaching into
`(apiClient as any).baseUrl` / `(apiClient as any).http.fetch`, and the hand-declared
response interfaces in `useManufactureOutput.ts` were deleted in favor of the generated
types.

## Files changed

- `frontend/src/api/hooks/useManufactureOutput.ts`
  - Deleted the hand-declared `ManufactureOutputResponse` / `ManufactureOutputMonth` /
    `ProductContribution` / `ProductionDetail` interfaces.
  - Imports and re-exports the generated `GetManufactureOutputResponse`,
    `ManufactureOutputMonth`, `ProductContribution`, `ProductionDetail` from
    `../generated/api-client` (re-exporting keeps both consumer components' existing
    import paths — `../../api/hooks/useManufactureOutput` — unchanged).
  - `queryFn` now calls `apiClient.manufactureOutput_GetManufactureOutput(monthsBack)`
    directly; dropped the stray `await` on the synchronous `getAuthenticatedApiClient()`
    call and the manual `response.ok` / `response.json()` handling (the generated method
    already throws `SwaggerException` on non-2xx).

- `frontend/src/api/hooks/useSemiproductRecipePdf.ts`
  - `openRecipePdf` now calls `apiClient.manufactureBatch_GetRecipePdf(productCode, batchSize)`
    and uses `response.data` (already a `Blob`) directly with `URL.createObjectURL`.
  - Dropped the manual URL construction, `response.ok` check, and `response.blob()` call —
    the generated method throws on non-2xx, which is caught by the existing `catch` block,
    so the public `{ openRecipePdf, isLoading, error }` contract is unchanged.

- `frontend/src/components/pages/ManufactureOutput.tsx`
  - No import changes needed (types still come from the hook module, now re-exported).
  - Generated types have optional fields where the old ones were required
    (`months?`, `products?`, `productionDetails?`, `month?`, `totalOutput?`,
    `productCode?`, `productName?`, `quantity?`, `difficulty?`, `weightedValue?`).
    Normalized `months` once per `useMemo`/callback via `data?.months ?? []`, and guarded
    each per-month `products` array access (`month.products ?? []`) and numeric field
    read (`?? 0`) at every call site the TS compiler flagged (chart data building,
    click handler, tooltip `afterLabel`, summary stats).

- `frontend/src/components/pages/ManufactureOutputModal.tsx`
  - Same `?? []` / `?? 0` null-safety treatment for `monthData.products`,
    `monthData.productionDetails`, `monthData.month`, `monthData.totalOutput`, and the
    per-product/per-record numeric fields.
  - `ProductionDetail.date` is now a generated `Date` (was hand-declared as `string`).
    `formatDate` was changed from `(dateStr: string) => new Date(dateStr).toLocaleDateString(...)`
    to `(date?: Date) => date ? date.toLocaleDateString("cs-CZ") : ""` — the redundant
    `new Date(...)` wrap is gone since `record.date` is already a `Date` instance.

- `frontend/src/components/pages/ManufactureBatchCalculator.tsx` — unchanged, as
  expected; it only consumes the hook's stable `{ openRecipePdf, isLoading, error }`
  surface.

## Tests added

- `frontend/src/api/hooks/__tests__/useManufactureOutput.test.tsx` (new) — mocks
  `getAuthenticatedApiClient()` to return an object exposing only
  `manufactureOutput_GetManufactureOutput` (no `http`/`baseUrl`), and verifies:
  - the query calls the generated method with the given `monthsBack` and returns its
    resolved value untouched;
  - the default `monthsBack` of 13 is used when omitted;
  - a rejection from the generated method surfaces as `isError`/`error` on the query;
  - `formatMonthDisplay` / `getMonthShortName` still format Czech month names correctly.

- `frontend/src/api/hooks/__tests__/useSemiproductRecipePdf.test.ts` (new) — same
  approach, modeled on the existing `useOpenManufactureProtocol.test.ts` convention
  (mocking `URL.createObjectURL`/`window.open`/fake timers), verifying:
  - `manufactureBatch_GetRecipePdf` is called with `(productCode, batchSize)`, including
    the `undefined` case when `batchSize` is omitted;
  - the returned blob is opened via `URL.createObjectURL` + `window.open` in a new tab;
  - the object URL is revoked after 10s;
  - `isLoading` returns to `false` after completion;
  - an error thrown by the generated client (non-2xx) is captured in `error` and
    `window.open` is not called.

Both new test files assert `mockApiClient.http`/`(mockApiClient as any).http` is
`undefined`, so they'd fail if the hook regressed back to reaching into private client
internals.

## Verification performed

- `grep -rn "as any" frontend/src/api/hooks/useManufactureOutput.ts frontend/src/api/hooks/useSemiproductRecipePdf.ts` → no hits.
- `npm ci --legacy-peer-deps` (repo has a pre-existing `react-i18next`/`typescript` peer
  conflict unrelated to this change; `node_modules` was not present in the worktree).
- `CI=true npm run build` → **compiled successfully**, zero TypeScript errors.
- `npm run lint` → 175 pre-existing errors / 13 warnings, all in unrelated test files
  (`ManufactureOrderWeeklyCalendar`, `AgendaDayGroup`, `MarketingActionModal`,
  `FinancialChart`, etc. — testing-library rule violations predating this change);
  **zero lint errors in any of the five files touched** by this task.
- `npx react-scripts test --testPathPattern="useManufactureOutput|useSemiproductRecipePdf"` →
  **2 suites / 11 tests passed**.
- `npx react-scripts test --testPathPattern="Manufacture"` → 10/11 suites passed; the one
  failure (`ManufactureOrderDetail.autoCalculation.test.tsx`) is **pre-existing and
  unrelated** — confirmed by `git stash`-ing this change and re-running the same test,
  which fails identically on the unmodified branch. That file doesn't reference either
  touched hook.

## How to verify

```bash
cd frontend
npm ci --legacy-peer-deps   # only needed if node_modules is missing
CI=true npm run build
npm run lint
npx react-scripts test --testPathPattern="useManufactureOutput|useSemiproductRecipePdf" --watchAll=false
```

No backend changes; no new endpoints; no change to the public API of either hook or to
component rendering behavior for a well-formed API response.
