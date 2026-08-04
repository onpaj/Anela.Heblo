# Plan: DataQuality hooks — replace `(apiClient as any).http.fetch` with generated client calls

## Summary

`frontend/src/api/hooks/useDataQuality.ts` bypasses the generated NSwag client for all three DQT endpoints, using raw `(apiClient as any).http.fetch` calls plus hand-duplicated DTOs/response interfaces. This severs the compile-time link to the OpenAPI contract. The fix is to call the generated `dataQuality_GetRuns` / `dataQuality_RunDqt` / `dataQuality_GetRunDetail` methods directly and delete the duplicated local types — matching the pattern already used by `useConfiguration.ts` and other compliant hooks.

## Context

This is one of a recurring class of findings (Photobank #3815, Manufacture #3797/#3730) where hooks were written against a hand-rolled fetch wrapper instead of the generated client, likely predating those endpoints' addition to the OpenAPI spec, or copy-pasted from an older pattern. Fixing it is pure refactor with no behavior change intended — same URLs, same payloads — but it does surface real type mismatches between the ad-hoc local interfaces and the generated types that must be reconciled, not glossed over.

## Investigation findings (read before implementing)

Comparing the local hand-rolled types (`useDataQuality.ts:6-71`) against the generated client (`frontend/src/api/generated/api-client.ts`) turned up real shape differences — this is not a pure find/replace:

1. **Date fields are `string` locally but `Date` in generated types.** `DqtRunDto.dateFrom/dateTo/startedAt/completedAt` and `RunDqtRequest.dateFrom/dateTo` are typed `Date` in the generated client (parsed via `new Date(...)` in `fromJS`, serialized via `formatDate`/`.toISOString()` in `toJSON`). The local interfaces typed them as `string`.
2. **`testType`/`status` are enums, not strings, in the generated client.** `DqtTestType` (`IssuedInvoiceComparison`, `ProductPairing`, `StockWriteBackReconciliation`, `LotSumVsErpStock`) and `DqtRunStatus` (`Running`, `Completed`, `Failed`) are real enums (string-valued, so wire-compatible with today's string literals, but the TS types differ).
3. **`success` comes from `BaseResponse`**, not redeclared per-response — `GetDqtRunsResponse`, `GetDqtRunDetailResponse`, `RunDqtResponse` all `extends BaseResponse` which already has `success?: boolean`.
4. **`DqtDriftResultDto` in the generated client has an extra `testType` field** not present in the local interface — additive, harmless, no consumer currently reads it.
5. **Three components import types directly from the hook file, not from `generated/api-client`:**
   - `DqtSummaryCards.tsx:3` imports `DqtRunDto`
   - `DqtRunsTable.tsx:11` imports `DqtRunDto`
   - `DqtRunDetail.tsx:3` imports `InvoiceDqtResultDto`, `DqtDriftResultDto`
   These imports must be repointed to the generated client's types (re-exporting from the hook file is also acceptable but the generated types are the source of truth — prefer importing directly from `../generated/api-client` in each component, consistent with how other modules do it).
6. **Downstream code assumes string dates and will break once the hook returns generated types with `Date` fields**, unless adjusted:
   - `DqtSummaryCards.tsx:114`: `{run.dateFrom} — {run.dateTo}` renders raw values as JSX children — a `Date` object is not a valid React child and will throw. Needs explicit formatting (e.g., `.toLocaleDateString('cs-CZ')`).
   - `DqtRunsTable.tsx:136`: same `{run.dateFrom} — {run.dateTo}` issue.
   - `DqtRunsTable.tsx:27-37,160`: local `formatDateTime(iso: string)` calls `new Date(iso)` — needs its parameter type changed to accept `Date` (or be re-derived to just format a `Date` directly) since `run.startedAt` will already be a `Date`.
7. **`RunDqtButton.tsx` builds the mutation payload from plain `useState<string>` values** (`testType`, `dateFrom`, `dateTo` from a `<input type="date">`). `useRunDqt`'s `mutationFn` will now expect a `RunDqtRequest`-shaped object with `testType?: DqtTestType` and `dateFrom/dateTo: Date`. The call site (`RunDqtButton.tsx:49`: `mutate({ testType, dateFrom, dateTo })`) needs to convert: cast/assert `testType` to `DqtTestType` (values already match enum members) and convert the `'YYYY-MM-DD'` strings to `Date` objects before calling `mutate`.
8. **Error handling changes shape.** The generated client's `processX` methods call `throwException(...)` (which throws `ApiException`, itself an `Error` subclass) on non-2xx responses instead of the hooks' current `throw new Error('Failed to fetch DQT runs: ' + status)`. Consumers only do `(error as Error).message` (`DqtRunsTable.tsx:83`, `DqtRunDetail.tsx:147`), which still works, but the message text will change (ApiException's message format differs from the current hand-written one) — acceptable, not a regression, but worth a quick visual check post-change since these messages are user-facing (Czech UI, "Chyba při načítání: {message}").

## Functional requirements

- **FR-1**: `useDqtRuns` calls `apiClient.dataQuality_GetRuns(testType, status, pageNumber, pageSize)` instead of building a URL and using `(apiClient as any).http.fetch`.
  - AC: no `as any` remains in `useDataQuality.ts`; `testType`/`status` params are passed as `DqtTestType | undefined` / `DqtRunStatus | undefined` (cast from the existing `GetDqtRunsParams` string params, or the params type is updated to use the enums).
  - AC: pagination behavior (`pageNumber`, `pageSize`) and the 30s `refetchInterval`/`staleTime` are unchanged.
- **FR-2**: `useDqtRunDetail` calls `apiClient.dataQuality_GetRunDetail(runId, resultPage, resultPageSize)` instead of manual fetch.
  - AC: `enabled: !!runId` gating and query key are unchanged.
- **FR-3**: `useRunDqt` calls `apiClient.dataQuality_RunDqt(request)` where `request` is a proper `RunDqtRequest` instance/shape instead of manual fetch.
  - AC: `onSuccess` invalidation of `dataQualityKeys.all` is unchanged.
- **FR-4**: The hand-duplicated interfaces (`DqtRunDto`, `InvoiceDqtResultDto`, `GetDqtRunsResponse`, `GetDqtRunDetailResponse`, `RunDqtRequest`, `DqtDriftResultDto`, `RunDqtResponse`) are deleted from `useDataQuality.ts`; the generated equivalents from `../generated/api-client` are used/re-exported instead.
  - AC: `grep -c "as any" frontend/src/api/hooks/useDataQuality.ts` returns 0.
- **FR-5**: All three consuming components (`DqtSummaryCards.tsx`, `DqtRunsTable.tsx`, `DqtRunDetail.tsx`) are updated to import types from the generated client (directly or via the hook file) and to correctly handle `Date`-typed fields (`dateFrom`, `dateTo`, `startedAt`, `completedAt`) instead of raw strings.
  - AC: no `Date` object is passed directly as a JSX child anywhere in these three files.
- **FR-6**: `RunDqtButton.tsx` converts its local string-based date-picker state into a valid `RunDqtRequest` (enum `testType`, `Date` `dateFrom`/`dateTo`) before calling `mutate`.
  - AC: triggering a manual DQT run from the UI still POSTs the same wire payload shape as before (`testType`, `dateFrom`, `dateTo` as ISO date strings in the JSON body — `RunDqtRequest.toJSON()` already serializes `Date` via `formatDate`, matching prior behavior).

## Non-functional requirements

- **Type safety**: `tsc`/`npm run build` must pass with no new `any` casts introduced to work around the type change (temporary casts for enum string literals, e.g. `testType as DqtTestType`, are acceptable since the string values are already valid enum members; blanket `as any` on the response/request objects is not).
- **No behavior change**: request URLs, query params, request/response JSON payloads, polling intervals, and cache keys must stay identical to today — this is a refactor, not a feature change. Verify manually in the browser (data quality page loads runs, selecting a run loads detail, running a manual DQT triggers with the right payload) since this touches UI rendering of dates.

## Data model

No backend/data model changes. Frontend-only: the generated `DqtRunDto`, `InvoiceDqtResultDto`, `DqtDriftResultDto`, `GetDqtRunsResponse`, `GetDqtRunDetailResponse`, `RunDqtRequest`, `RunDqtResponse`, `DqtTestType`, `DqtRunStatus` (already generated at `frontend/src/api/generated/api-client.ts:19780-20180`, `19913-19924`) become the single source of truth, replacing the hand-rolled duplicates.

## Interfaces

No API surface changes — same three endpoints, same routes (`GET /api/data-quality/runs`, `GET /api/data-quality/runs/{id}`, `POST /api/data-quality/runs`), called through the typed generated methods instead of manual fetch.

## Dependencies and scope

- Depends on: the generated client already containing `dataQuality_GetRuns`, `dataQuality_RunDqt`, `dataQuality_GetRunDetail` with the DTOs shown above (confirmed present, no backend/codegen work needed).
- In scope: `useDataQuality.ts` and its three direct consumers (`DqtSummaryCards.tsx`, `DqtRunsTable.tsx`, `DqtRunDetail.tsx`, `RunDqtButton.tsx`) and the page that wires them (`DataQualityPage.tsx`, likely untouched since it only reads `latestRunData?.items?.[0]`).
- Out of scope: any backend controller/DTO changes, the Photobank/Manufacture equivalents of this finding (separate accepted findings, #3815/#3797/#3730), any change to the `refetchInterval`/`staleTime` tuning, redesigning the DQT UI.

## Rough plan

1. Rewrite `useDataQuality.ts`: drop the local interfaces, import the generated types (`DqtRunDto`, `InvoiceDqtResultDto`, `DqtDriftResultDto`, `GetDqtRunsResponse`, `GetDqtRunDetailResponse`, `RunDqtRequest`, `RunDqtResponse`, `DqtTestType`, `DqtRunStatus`) from `../generated/api-client`, and re-export the ones consumers need (or have consumers import from generated directly — pick one convention and apply consistently, matching sibling hook files).
2. Replace each `queryFn`/`mutationFn` body with the corresponding `apiClient.dataQuality_*` call; keep `GetDqtRunsParams`/query-key/staleTime/refetchInterval logic as-is, adapting only the `testType`/`status` param types to the enums.
3. Update `DqtSummaryCards.tsx`, `DqtRunsTable.tsx`, `DqtRunDetail.tsx` imports and any JSX/date-formatting code that assumed `string` dates, per FR-5.
4. Update `RunDqtButton.tsx` to build a proper `RunDqtRequest`-shaped payload (enum cast + `Date` conversion) before calling `mutate`, per FR-6.
5. Build and typecheck (`npm run build`), run `npm run lint`, fix fallout.
6. Manually exercise the Data Quality page in the browser: runs list loads/paginates, selecting a run shows detail (both the invoice-comparison table and the drift-result table variants), and triggering a manual run via `RunDqtButton` succeeds and invalidates the list.

## Open questions

- **Re-export vs. direct import for types.** The plan defaults to having components import DTOs directly from `../generated/api-client` rather than re-exporting through the hook file, since that's the pattern `useConfiguration.ts` and peers follow and it's the more direct fix. If the implementer finds sibling hooks in this codebase commonly re-export generated types through the hook module instead, follow that established convention rather than this default.
- **Exact date-formatting choice for `DqtSummaryCards`/`DqtRunsTable` "Období" column.** The current raw string concatenation (`{run.dateFrom} — {run.dateTo}`) happened to render `YYYY-MM-DD` because the backend sent bare date strings. Once these are real `Date` objects, a formatting call is required (e.g., `toLocaleDateString('cs-CZ')` or a small local helper) — implementer's choice, but it should produce equivalent or better-looking output than today, not a regression (e.g., not `.toString()`'s verbose format).
