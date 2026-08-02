# Architecture review: DataQuality hooks migration (design-01.md)

## Verdict

**One correction required before implementation; everything else in design-01.md checks out against the actual codebase.**

I verified every factual claim in design-01.md against the live source (generated client, `utils/formatters.ts`, all four consumer components, and the sibling hooks it cites as precedent). The technical claims — method signatures, DTO field types/optionality, the `RunDqtRequest.toJSON()`/`formatDate` serialization behavior, the `formatDate`/`formatDateTime` utility signatures, and the line-by-line fallout in the three consumer components — are all accurate. The one substantive problem is an inverted convention claim in §1/§2 that, if followed, would violate this codebase's dominant hook/DTO pattern.

## Verified correct (no changes needed)

- `apiClient.dataQuality_GetRuns(testType, status, pageNumber, pageSize)`, `dataQuality_RunDqt(request)`, `dataQuality_GetRunDetail(id, resultPage, resultPageSize)` signatures match `api-client.ts:2854/2900/2938` exactly, including the `DqtTestType | null | undefined` / `DqtRunStatus | null | undefined` parameter typing.
- `DqtRunDto`, `GetDqtRunsResponse`, `GetDqtRunDetailResponse`, `InvoiceDqtResultDto`, `DqtDriftResultDto`, `RunDqtResponse`, `RunDqtRequest`, `DqtTestType`, `DqtRunStatus` field shapes (`api-client.ts:19780-20186`) match the design's table in §3 exactly — including the enum/string asymmetry (`DqtRunDto.testType: string` vs `RunDqtRequest.testType: DqtTestType`) and the `Date` typing on `dateFrom/dateTo/startedAt/completedAt`.
- `RunDqtRequest.toJSON()` (`api-client.ts:20173-20179`) serializes `dateFrom`/`dateTo` via the module-local `formatDate(d: Date)` (local calendar components, not `toISOString()`) — confirms the design's §3 claim that a plain object literal (no `.toJSON()`) would silently fall back to UTC-instant serialization via `Date.prototype.toJSON`, and that `new RunDqtRequest({...})` is required. This is a real, non-obvious footgun the design correctly caught and documented.
- `utils/formatters.ts` `formatDate`/`formatDateTime` accept `string | Date | null | undefined` and produce `cs-CZ` `dd.MM.yyyy` / `dd.MM.yyyy HH:mm` — matches §2.1/§2.2 exactly, including the claim that `DqtRunsTable.tsx`'s local `formatDateTime` (lines 27–37) is a byte-for-byte duplicate safe to delete in favor of the shared one.
- Every cited line number and code fragment in the four consumer components (`DqtSummaryCards.tsx:28,36,91,96,104,114`; `DqtRunsTable.tsx:27-37,46,133,136,143,145,160`; `DqtRunDetail.tsx:79,157-180,198`; `RunDqtButton.tsx`'s plain-object `mutate({testType,dateFrom,dateTo})` call and string-typed `testType` state) matches the current source exactly.
- `getAuthenticatedApiClient()` is synchronous (`client.ts:276`), consistent with the design's non-`await` usage in all three rewritten hooks and consistent with `useRecurringJobs.ts`'s usage.

## Finding: the "import DTOs directly from generated/api-client" directive contradicts the codebase's actual convention

**design-01.md §1** states:

> `DqtRunDto`, `InvoiceDqtResultDto`, `DqtDriftResultDto` are not imported here — components that need them import directly from `../generated/api-client`... This mirrors the `useRecurringJobs.ts` pattern: the hook file only imports what it constructs or returns as its own signature; DTOs nested inside a response are consumed by importing from the generated module at the component that needs them, not re-exported through the hook.

This is factually backwards. `useRecurringJobs.ts:124-125` does the opposite:

```ts
// Re-export types for convenience
export type { RecurringJobDto, UpdateRecurringJobStatusResponse, UpdateRecurringJobCronResponse, TriggerRecurringJobResponse };
```

and its actual consumer imports the DTO *from the hook*, not from generated/api-client:

```ts
// RecurringJobsPage.tsx:3
import { useRecurringJobsQuery, ..., RecurringJobDto } from '../api/hooks/useRecurringJobs';
```

This is not an isolated case. It is the dominant pattern across the hooks directory — I confirmed explicit `// Re-export types...` + `export type {...} from '../generated/api-client'` (or `export {...}`) blocks in at least seven sibling hook files: `useCatalog.ts:19-34`, `useManufactureOrders.ts:219-238`, `useBankStatements.ts:14`, `useSuppliers.ts:5-6`, `useRecurringJobs.ts:124-125`, plus `useAccessManagement.ts`, `useFeatureFlagsAdmin.ts`, `useFinancialOverview.ts`, `useProductMargins.ts`, and others. `useConfiguration.ts` (the design's other cited precedent) doesn't re-export anything only because no consumer needs a nested DTO from it — it's not evidence of a "components import from generated directly" convention, it's an absence of the scenario.

**Why this matters:** `plan-01.md`'s own "Open questions" section explicitly flagged this exact fork ("If the implementer finds sibling hooks in this codebase commonly re-export generated types through the hook module instead, follow that established convention rather than this default") and the design resolved it — incorrectly, citing the wrong behavior for its own cited example. Following design-01.md §2.1–2.3 as written (`import { DqtRunDto } from '../../api/generated/api-client';` in each of the three consumer components) would introduce a new, second import convention alongside the dominant one for no functional benefit, and would fail the project's own "surgical changes" principle (CLAUDE.md) by widening the diff in three consumer files beyond what's needed.

**Required correction:** `useDataQuality.ts` should re-export the DTOs its consumers need, matching `useCatalog.ts`/`useManufactureOrders.ts`/`useRecurringJobs.ts`:

```ts
export type { DqtRunDto, InvoiceDqtResultDto, DqtDriftResultDto } from '../generated/api-client';
```

and the three consumer components (`DqtSummaryCards.tsx`, `DqtRunsTable.tsx`, `DqtRunDetail.tsx`) should keep importing these types from `'../../api/hooks/useDataQuality'` unchanged — only dropping the hand-rolled local interface, not repointing the import path. `RunDqtButton.tsx` importing `DqtTestType`/`RunDqtRequest` directly from `'../../api/generated/api-client'` is fine as-is since the hook file doesn't own those (they're request-side types the button constructs itself, not something `useDataQuality.ts` returns) — that part of §2.4 is consistent with how `useManufactureOrders.ts`/`useRecurringJobs.ts` consumers construct request bodies (`new UpdateJobStatusRequestBody(...)` imported directly).

Everything else in design-01.md's rewrite (§1's `dataQuality_*` call bodies, §2's optionality/`Date` fixups, §3's serialization notes) stands as specified. This is a one-line-per-file correction (re-export statement in the hook; revert the three import-path changes in consumers), not a rework.

## Risks / prerequisites

- None beyond the correction above. No backend, DB, or API surface changes; no new dependencies; the generated client already contains everything needed (confirmed, not assumed).
- Implementer should run `npm run build` + `npm run lint` per CLAUDE.md's validation gate before completion, since the `Date`/optional-field fallout touches several render sites — the design's per-line fixups look correct on inspection but the type checker is the actual gate here.
