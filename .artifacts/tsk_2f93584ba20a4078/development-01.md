# Development: DataQuality hooks — migrate to generated API client

Implements design-01.md as corrected by architecture-01.md (re-export DTOs
through the hook module instead of repointing consumer imports to
`generated/api-client` directly).

## Files changed

### `frontend/src/api/hooks/useDataQuality.ts` (full rewrite)

- Deleted all seven hand-rolled interfaces (`DqtRunDto`, `InvoiceDqtResultDto`,
  `GetDqtRunsResponse`, `GetDqtRunDetailResponse`, `RunDqtRequest`,
  `DqtDriftResultDto`, `RunDqtResponse`) and the `(apiClient as any).http.fetch`
  calls in all three hooks.
- `useDqtRuns` now calls `apiClient.dataQuality_GetRuns(testType, status, pageNumber, pageSize)`.
- `useDqtRunDetail` now calls `apiClient.dataQuality_GetRunDetail(runId!, resultPage, resultPageSize)`.
- `useRunDqt` now calls `apiClient.dataQuality_RunDqt(request: RunDqtRequest)`.
- `GetDqtRunsParams` kept as a local interface (no generated equivalent for a
  GET-with-query-string request), retyped to `DqtTestType`/`DqtRunStatus` enums.
- Query key factory (`dataQualityKeys`) unchanged.
- Added `export type { DqtRunDto, InvoiceDqtResultDto, DqtDriftResultDto } from '../generated/api-client';`
  at the bottom, matching the re-export convention used by `useRecurringJobs.ts`,
  `useCatalog.ts`, `useManufactureOrders.ts`, etc. — per the architecture
  review's correction, consumer components keep importing these types from
  `'../../api/hooks/useDataQuality'` rather than switching to
  `'../../api/generated/api-client'`.

### `frontend/src/components/data-quality/DqtSummaryCards.tsx`

- `run.totalMismatches`/`run.totalChecked` are now optional (`number | undefined`):
  guarded comparisons with `?? 0`, rendered with `?? '—'`.
- `run.dateFrom`/`run.dateTo` are now `Date | undefined`: rendered via the
  shared `formatDate` from `utils/formatters.ts` instead of raw string
  interpolation.

### `frontend/src/components/data-quality/DqtRunsTable.tsx`

- Deleted the local `formatDateTime` (byte-identical to the shared one) and
  imported `formatDate`/`formatDateTime` from `../../utils/formatters` instead.
- `run.totalMismatches` comparisons guarded with `?? 0` (now optional).
- `run.testType` indexing into `TEST_TYPE_LABELS` guarded for `undefined`.
- `run.dateFrom`/`run.dateTo` rendered via `formatDate` (now `Date | undefined`).
- `run.id` is now `string | undefined` (generated DTO marks all fields
  optional); `onRunSelect(run.id)` → `onRunSelect(run.id ?? '')` since
  `onRunSelect` requires a `string`. This wasn't called out in design-01.md —
  found during the `npm run build` type-check gate.

### `frontend/src/components/data-quality/DqtRunDetail.tsx`

- `result.mismatchFlags` is now `string[] | undefined`: guarded with `?? []`.
- `row.mismatchCode` is now `number | undefined`: `decodeMismatchFlags(row.mismatchCode ?? 0, flagMap)`.
- `prettyPrint`'s parameter widened from `string | null` to
  `string | null | undefined` — `InvoiceDqtResultDto.shoptetValue`/`flexiValue`
  are `string | undefined` (not `string | null`) on the generated DTO. Also
  not called out in design-01.md; found via the `npm run build` type-check.

### `frontend/src/components/data-quality/RunDqtButton.tsx`

- Imports `DqtTestType`, `RunDqtRequest` from `../../api/generated/api-client`.
- `TEST_TYPE_OPTIONS`/`testType` state retyped from raw strings to `DqtTestType`.
- Added `toLocalDate(yyyyMmDd: string): Date` (constructs local midnight via
  `new Date(y, m-1, d)`, not `new Date(isoString)`, to avoid UTC-parsing
  ambiguity).
- `handleRun` now builds a real `new RunDqtRequest({ testType, dateFrom: toLocalDate(dateFrom), dateTo: toLocalDate(dateTo) })`
  instance instead of a plain object literal — required because
  `RunDqtRequest.toJSON()` (generated) is what makes `dateFrom`/`dateTo`
  serialize as date-only strings; a plain object has no `.toJSON()` and would
  fall back to `Date.prototype.toJSON()` (UTC instant).
- The `<select>` DOM `onChange` still receives a plain string; narrow cast
  `e.target.value as DqtTestType` at that single boundary.

### `frontend/src/api/hooks/__tests__/useDataQuality.test.ts` (new)

Hook-level tests using the existing `mockAuthenticatedApiClient`/
`createQueryClientWrapper` test utilities (same pattern as
`useBankStatements.test.ts`):

- `useDqtRuns`: calls `dataQuality_GetRuns` with the given params, passes
  `undefined` for omitted params, returns the typed response unmodified.
- `useDqtRunDetail`: calls `dataQuality_GetRunDetail` with `runId`/paging;
  stays `idle` (does not fetch) when `runId` is `null`.
- `useRunDqt`: calls `dataQuality_RunDqt` with the `RunDqtRequest` instance
  passed to `mutate`, and surfaces the mocked response.

No new tests were added for the four consumer components — their existing
behavior (rendering, optional-field fallback to `—`) is exercised indirectly
by `DataQualityTile.test.tsx`/`DqtYesterdayStatusTile.test.tsx`, which already
pass unchanged, and `DqtRunsTable`/`DqtRunDetail`/`DqtSummaryCards`/`RunDqtButton`
had no pre-existing dedicated test files to extend (surgical-change principle:
not introducing new test scaffolding beyond what the task requires).

## Verification performed

- `npm install --legacy-peer-deps` (repo's `node_modules` wasn't present in
  this environment; `npm ci` fails on a pre-existing `react-i18next`/
  `typescript` peer-dependency conflict unrelated to this change).
- `npx react-scripts test --testPathPattern="useDataQuality|DataQuality|Dqt" --watchAll=false`
  → all 23 tests across 3 suites pass (new hook tests + the two dashboard tile
  tests that read DQT data).
- `npm run build` → **compiles successfully** after two additional fixes
  (`DqtRunsTable.tsx`'s `run.id` optionality, `DqtRunDetail.tsx`'s `prettyPrint`
  parameter type) that the type-check gate caught beyond what design-01.md
  specified.
- `npm run lint` → 0 errors/warnings in any file touched by this change (the
  175 pre-existing errors/13 warnings reported are all in unrelated files
  across the repo, not introduced by this change).
- Full suite: `npx react-scripts test --watchAll=false` → 2515 passed / 11
  failed / 5 skipped across 303 suites. The 11 failures are in 5 suites
  entirely unrelated to this change (`utils/timePeriod/resolve.test.ts`,
  `chartDataMapping.test.ts`, `fullcalendarAdapters.test.ts` — timezone-
  sensitive date assertions off by one day in this sandbox's TZ — and
  `useManufacturingStockAnalysis.test.tsx`, `ManufactureOrderDetail.autoCalculation.test.tsx`).
  None touch `data-quality`/`useDataQuality`.

## How to verify

```
cd frontend
npm run build
npx react-scripts test --testPathPattern="useDataQuality|DataQuality|Dqt" --watchAll=false
```

Manual check: open the Kvalita dat (Data Quality) page, confirm the runs
table/detail/summary cards render dates and mismatch counts correctly, and
that triggering a manual DQT run via `RunDqtButton` still posts successfully
(no backend/wire-format change — same three routes, same JSON shapes).
