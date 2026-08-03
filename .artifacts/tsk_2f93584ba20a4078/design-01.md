# Design: DataQuality hooks — migrate to generated API client

No UI is added or restructured by this change (no new screens, wireframes, or
interaction flows). It is an internal data-layer refactor of an existing page
(`frontend/src/components/data-quality/*`); the one rendering-visible
consequence (date fields changing from `string` to `Date`) is handled by
reusing an existing formatting utility, specified precisely below. The
UX/UI section is therefore omitted per the design brief.

## Component design

### 1. `frontend/src/api/hooks/useDataQuality.ts` — full rewrite

Drop all seven hand-rolled interfaces (current lines 6–71). Replace with a
direct import from the generated client, matching the convention already used
by `useRecurringJobs.ts` / `useConfiguration.ts` (import generated types,
call `apiClient.<method>(...)` directly, no re-implemented fetch/JSON parsing):

```ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  DqtTestType,
  DqtRunStatus,
  RunDqtRequest,
  type GetDqtRunsResponse,
  type GetDqtRunDetailResponse,
  type RunDqtResponse,
} from '../generated/api-client';
```

`DqtRunDto`, `InvoiceDqtResultDto`, `DqtDriftResultDto` are not imported here
— components that need them import directly from `../generated/api-client`
(see §2). This mirrors the `useRecurringJobs.ts` pattern: the hook file only
imports what it constructs or returns as its own signature; DTOs nested
inside a response are consumed by importing from the generated module at the
component that needs them, not re-exported through the hook.

**Query key factory** — unchanged verbatim (`dataQualityKeys.all/runs/runDetail`),
since it doesn't touch the fetch layer.

**`GetDqtRunsParams`** — keep as a locally-defined interface (the generated
client has no equivalent request-params type for a GET-with-query-string
endpoint), but retype the two filter fields to the generated enums since
nothing else about the interface changes:

```ts
export interface GetDqtRunsParams {
  testType?: DqtTestType;
  status?: DqtRunStatus;
  pageNumber?: number;
  pageSize?: number;
}
```

No consumer currently passes `testType`/`status` (`DqtRunsTable.tsx` only
passes `pageNumber`/`pageSize`), so this retype has zero call-site fallout.

**`useDqtRuns`**:

```ts
export const useDqtRuns = (params: GetDqtRunsParams = {}) => {
  return useQuery({
    queryKey: dataQualityKeys.runs(params),
    queryFn: (): Promise<GetDqtRunsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.dataQuality_GetRuns(
        params.testType,
        params.status,
        params.pageNumber,
        params.pageSize,
      );
    },
    staleTime: 30 * 1000,
    gcTime: 5 * 60 * 1000,
    refetchInterval: 30 * 1000,
  });
};
```

`staleTime`/`gcTime`/`refetchInterval` values are carried over unchanged
(FR-1 AC). `queryFn` no longer needs to be `async` since
`dataQuality_GetRuns` already returns a `Promise`.

**`useDqtRunDetail`**:

```ts
export const useDqtRunDetail = (
  runId: string | null,
  resultPage: number = 1,
  resultPageSize: number = 50,
) => {
  return useQuery({
    queryKey: dataQualityKeys.runDetail(runId ?? ''),
    queryFn: (): Promise<GetDqtRunDetailResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.dataQuality_GetRunDetail(runId!, resultPage, resultPageSize);
    },
    enabled: !!runId,
    staleTime: 30 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};
```

The `runId!` non-null assertion under `enabled: !!runId` is the established
pattern in this codebase (`useBackgroundRefresh.ts` `useTaskHistory`/
`useTaskStatus` do the same) — TanStack Query guarantees `queryFn` never
runs while `enabled` is false, so the assertion is safe.

**`useRunDqt`**:

```ts
export const useRunDqt = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: RunDqtRequest): Promise<RunDqtResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.dataQuality_RunDqt(request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: dataQualityKeys.all });
    },
  });
};
```

`mutate()` must be called with an actual `RunDqtRequest` **class instance**,
not a plain object literal — see the wire-format note in §3 (Data schemas)
for why this is load-bearing, and §2.4 for how `RunDqtButton.tsx` constructs
it.

### 2. Consumer components

#### 2.1 `DqtSummaryCards.tsx`

- Replace `import { DqtRunDto } from '../../api/hooks/useDataQuality';` with
  `import { DqtRunDto } from '../../api/generated/api-client';`.
- `run.totalMismatches` and `run.totalChecked` are now `number | undefined`
  (generated DTO marks all fields optional) instead of `number`. Every
  comparison/render site must tolerate `undefined`:
  - Line 28, 36: `run.totalMismatches > 0` → `(run.totalMismatches ?? 0) > 0`.
  - Line 91: same pattern inside the ternary class-name expression.
  - Line 96, 104: `{run != null ? run.totalMismatches : '—'}` — `undefined`
    is already a valid (invisible) `ReactNode`, but for a stat card showing
    "no value" should still render `—`, so change to
    `{run?.totalMismatches ?? '—'}` (equivalent for `totalChecked`).
- Line 114 (`{run.dateFrom} — {run.dateTo}`): `dateFrom`/`dateTo` are now
  `Date | undefined`, not a JSX-renderable primitive. Import and use the
  existing shared formatter — **do not** write a new one:
  ```tsx
  import { formatDate } from '../../utils/formatters';
  ...
  {formatDate(run.dateFrom)} — {formatDate(run.dateTo)}
  ```
  `formatDate` (in `frontend/src/utils/formatters.ts:8`) already accepts
  `Date | string | null | undefined` and renders `dd.MM.yyyy` in `cs-CZ`,
  falling back to `—`. This is the same formatter already used elsewhere in
  the app for equivalent DTO date fields (e.g.
  `PurchaseStockAnalysis.tsx:466`), so output is visually consistent with
  the rest of the product, not a one-off.

#### 2.2 `DqtRunsTable.tsx`

- Replace `import { useDqtRuns, DqtRunDto } from '../../api/hooks/useDataQuality';`
  with `import { useDqtRuns } from '../../api/hooks/useDataQuality';` plus
  `import { DqtRunDto } from '../../api/generated/api-client';`.
- Delete the local `formatDateTime` (lines 27–37) entirely and import the
  shared one instead: `import { formatDate, formatDateTime } from '../../utils/formatters';`.
  The shared `formatDateTime` (`utils/formatters.ts:26`) has **identical**
  `cs-CZ` options (`day/month: '2-digit'`, `year: 'numeric'`, `hour/minute:
  '2-digit'`) to the local one being deleted, and it additionally accepts
  `Date` directly (the local version only accepted `string` and did
  `new Date(iso)` — no longer valid once `run.startedAt` is already a
  `Date`). Line 160: `{formatDateTime(run.startedAt)}` — no other change
  needed since the shared helper handles `undefined` internally.
- Line 136 (`{run.dateFrom} — {run.dateTo}`): same fix as §2.1 —
  `{formatDate(run.dateFrom)} — {formatDate(run.dateTo)}`.
- `run.totalMismatches` comparisons (lines 46, 143, 145) need the same
  `?? 0` treatment as §2.1 since it's now optional:
  `(run.totalMismatches ?? 0) > 0`.
- Line 133: `TEST_TYPE_LABELS[run.testType] ?? run.testType` — `run.testType`
  is `string | undefined` now (was `string`); `TEST_TYPE_LABELS` is
  `Record<string, string>`, so indexing with `undefined` needs a guard:
  `run.testType ? (TEST_TYPE_LABELS[run.testType] ?? run.testType) : '—'}`.
  (`DqtRunDto.testType` stays a plain `string` on the response side — the
  generated enum only appears on `RunDqtRequest`, see §3 — so no enum
  import is needed here, just the optionality fix.)

#### 2.3 `DqtRunDetail.tsx`

- Replace `import { useDqtRunDetail, InvoiceDqtResultDto, DqtDriftResultDto } from '../../api/hooks/useDataQuality';`
  with:
  ```ts
  import { useDqtRunDetail } from '../../api/hooks/useDataQuality';
  import { InvoiceDqtResultDto, DqtDriftResultDto } from '../../api/generated/api-client';
  ```
- `row.mismatchCode` (generated `DqtDriftResultDto.mismatchCode?: number`)
  and `run?.testType` are now optional; existing code already guards
  `run?.testType` with optional chaining everywhere (lines 157–180), so no
  change needed there. `decodeMismatchFlags(row.mismatchCode, flagMap)` at
  line 198 needs `row.mismatchCode ?? 0` since the function signature takes
  `code: number`.
- `result.mismatchFlags.map(...)` (line 79): `mismatchFlags` is
  `string[] | undefined` now (was `string[]`) — guard with
  `(result.mismatchFlags ?? []).map(...)`.
- No date fields are rendered in this component, so no `formatDate` change
  needed here.

#### 2.4 `RunDqtButton.tsx`

This is the one component that must construct a request object, not just
read a response, so it needs the most structural change:

- Import the generated types: `import { DqtTestType, RunDqtRequest } from '../../api/generated/api-client';`.
- Change `TEST_TYPE_OPTIONS` and the `testType` state from raw strings to
  the enum, eliminating string-literal/enum casts entirely:
  ```ts
  type TestTypeOption = { value: DqtTestType; label: string };

  const TEST_TYPE_OPTIONS: TestTypeOption[] = [
    { value: DqtTestType.IssuedInvoiceComparison, label: 'Porovnání faktur' },
    { value: DqtTestType.ProductPairing, label: 'Párování produktů' },
    { value: DqtTestType.StockWriteBackReconciliation, label: 'Zpětný zápis skladu' },
    { value: DqtTestType.LotSumVsErpStock, label: 'Šarže vs. ERP sklad' },
  ];

  const [testType, setTestType] = useState<DqtTestType>(DqtTestType.IssuedInvoiceComparison);
  ```
  The `<select>`'s `onChange` still receives a plain `string` from the DOM
  (`e.target.value`), so the single narrow boundary cast
  `setTestType(e.target.value as DqtTestType)` is the only cast in this
  component — acceptable per plan's NFR (narrow enum-literal casts at a DOM
  boundary, not a blanket `as any`).
- Keep the `dateFrom`/`dateTo` local state as `YYYY-MM-DD` strings (the
  `<input type="date">` contract is unchanged) but convert them to `Date`
  **using explicit local-date components**, not `new Date(isoString)`,
  right before calling `mutate`:
  ```ts
  const toLocalDate = (yyyyMmDd: string): Date => {
    const [y, m, d] = yyyyMmDd.split('-').map(Number);
    return new Date(y, m - 1, d);
  };

  const handleRun = () => {
    setFeedback(null);
    mutate(
      new RunDqtRequest({
        testType,
        dateFrom: toLocalDate(dateFrom),
        dateTo: toLocalDate(dateTo),
      }),
      { /* onSuccess/onError unchanged */ },
    );
  };
  ```
  Why `new RunDqtRequest({...})` and not a plain object literal, and why
  `new Date(y, m-1, d)` and not `new Date('YYYY-MM-DD')`: see §3.

### 3. Data schemas

No backend contract changes — same three routes, same JSON shapes on the
wire. What changes is which TypeScript types describe them and two
serialization details implementers must get right:

**`RunDqtRequest` — request instance, not literal.** The generated
`ApiClient.dataQuality_RunDqt(request: RunDqtRequest)` does
`JSON.stringify(request)` directly (`api-client.ts` around line 2900) — it
does **not** call `request.toJSON()` itself. `JSON.stringify` only invokes a
`.toJSON()` method if the argument actually has one. `RunDqtRequest` (the
generated class) has `toJSON()` (`api-client.ts:20173`), which serializes
`dateFrom`/`dateTo` via `formatDate(date)` — a **date-only**,
local-calendar-component formatter (`YYYY-M-D`, not zero-padded month, see
`api-client.ts:43156`) — matching what the backend's `DateOnly`/date-only
query parameter expects. A plain object literal `{ testType, dateFrom,
dateTo }` has no `toJSON()`, so `JSON.stringify` would fall through to each
`Date`'s own `.toJSON()` (= `.toISOString()`), which is a **UTC instant**,
not a local date — for a `Date` built from a local midnight this still lands
on the same calendar day for Prague's positive UTC offset, but is fragile
and inconsistent with how every other generated request DTO in this codebase
serializes dates. `mutate()` must therefore be called with a real
`RunDqtRequest` instance (`new RunDqtRequest({ testType, dateFrom, dateTo })`),
per §2.4.

**Why `new Date(y, m-1, d)` and not `new Date('YYYY-MM-DD')`.**
`new Date('YYYY-MM-DD')` parses as UTC midnight; `formatDate` then reads
`getFullYear()/getMonth()/getDate()` in the **local** timezone. For Prague
(UTC+1/+2, always ahead of UTC) this happens to still resolve to the same
calendar day, but the codebase has no reason to depend on that coincidence —
`new Date(year, monthIndex, day)` constructs local midnight directly and is
the correct, timezone-independent way to turn a `YYYY-MM-DD` picker value
into the calendar date the user selected.

**Response DTOs — enum vs. string asymmetry (as found in the existing
generated client, not something this design introduces):**

| Field | Type | Notes |
|---|---|---|
| `DqtRunDto.testType` | `string` | plain string on responses, safe for existing `===` comparisons in `DqtRunsTable`/`DqtRunDetail` |
| `DqtRunDto.status` | `string` | same — `'Failed'`/`'Completed'`/`'Running'` comparisons unchanged |
| `DqtRunDto.dateFrom/dateTo/startedAt` | `Date` | requires `formatDate`/`formatDateTime` at render sites (§2.1, §2.2) |
| `DqtRunDto.completedAt` | `Date \| undefined` | was `string \| null`; no current render site uses it, no action needed |
| `RunDqtRequest.testType` | `DqtTestType` (enum) | only the *request* side is enum-typed; drives §2.4 |
| `GetDqtRunsResponse/GetDqtRunDetailResponse/RunDqtResponse.success` | `boolean \| undefined` (from `BaseResponse`) | was locally declared `boolean`; no consumer reads `.success` today (all three hooks are consumed via `.items`/`.run`/`.dqtRunId`), so this is inert |
| `DqtDriftResultDto.testType` | `string \| undefined` | additive field vs. the deleted local interface; not read by any consumer, no action needed |

**Error payload shape — unchanged from the caller's point of view.** The
generated `processDataQuality_*` methods throw `ApiException` (an `Error`
subclass) on non-2xx instead of the hooks' current
`new Error('Failed to fetch DQT runs: ' + status)`. Both
`DqtRunsTable.tsx:83` and `DqtRunDetail.tsx:147` only read `(error as
Error).message`, which remains valid — the displayed Czech message text
changes from `"Failed to fetch DQT runs: 500"`-style to whatever
`ApiException`'s constructed message is (typically including status and
response body). No code change required at these two call sites; flagged
here only so the message-text change isn't mistaken for a regression during
manual verification.
