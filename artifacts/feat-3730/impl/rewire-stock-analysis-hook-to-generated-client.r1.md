# Implementation: rewire-stock-analysis-hook-to-generated-client

## What was implemented

Rewired `useManufacturingStockAnalysisQuery` (and its exported types/enums) off the hand-rolled
`(apiClient as any).http.fetch` URL-building code path and onto the typed generated OpenAPI client
method `apiClient.manufacturingStockAnalysis_GetStockAnalysis(...)`. Per FR-1, the six hand-coded
types (`ManufacturingStockSortBy`, `ManufacturingStockSeverity`, `ManufacturingStockItemDto`,
`ManufacturingStockSummaryDto`, `GetManufacturingStockAnalysisResponse`) are now imported and
re-exported from `../generated/api-client` instead of being declared locally, so existing
consumers (`ManufacturingStockAnalysis.tsx`, `ManufactureBatchPlanning.tsx`) keep importing them
from this hook module unchanged (FR-4).

The local `timePeriod` field on `GetManufacturingStockAnalysisRequest` stays typed against the
app-level `TimePeriod`/`TimePeriodFilter` (not the generated enum) and is converted only at the
call boundary via a new exported `toGeneratedTimePeriod()` helper, per FR-3 and design.r1.md's
Component Design section. This helper also preserves the pre-refactor behavior of omitting the
`timePeriod` argument entirely when it equals `Q9M` (the backend's implicit default) — verified by
a dedicated test. `toGeneratedTimePeriod` is exported so Task 2's `handleExport` in
`ManufacturingStockAnalysis.tsx` can reuse the same single conversion point instead of duplicating
the cast + Q9M-omission logic.

`formatNumber`/`formatPercentage` were widened to accept `number | undefined` because the
generated `ManufacturingStockItemDto` marks every numeric field optional (NSwag's convention for
response DTOs) — this mirrors the existing pattern already shipped in
`usePurchaseStockAnalysis.ts`. `formatDateForApi` and all manual `URLSearchParams` building were
deleted; the generated client serializes `Date` params itself.

## Files created/modified

- `frontend/src/api/hooks/useManufacturingStockAnalysis.ts` (rewrite) — hook now calls the
  generated client method positionally; local DTO/enum types replaced by re-exports from
  `../generated/api-client`; new exported `toGeneratedTimePeriod` boundary-conversion helper;
  `formatNumber`/`formatPercentage` widened to accept `undefined`.
- `frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx` (rewrite of the
  `useManufacturingStockAnalysisQuery` describe block only) — now mocks
  `manufacturingStockAnalysis_GetStockAnalysis` via `mockAuthenticatedApiClient`/
  `createQueryClientWrapper` from `../../testUtils` (the established pattern from
  `useKnowledgeBase.test.ts`) instead of mocking `http.fetch`. `calculateTimePeriodRange` and
  `formatWarehouseStock` describe blocks are byte-for-byte unchanged, as specified.

## Tests

`frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx`:
- `useManufacturingStockAnalysisQuery`: asserts full positional argument order against the
  generated client mock (guards against transposition of the 15 positional params), error
  propagation, Q9M omission, non-default period inclusion, and Date passthrough for
  `customFromDate`/`customToDate`.
- `calculateTimePeriodRange`, `formatWarehouseStock`: untouched, pre-existing tests.

Test run results (after `npm ci --legacy-peer-deps` — see Notes on the environment quirk):

- Step 2 (before the hook rewrite): 5 failed / 9 passed — all 4 failures were the expected
  `useManufacturingStockAnalysisQuery` assertions against the not-yet-mocked
  `manufacturingStockAnalysis_GetStockAnalysis`, as predicted by the task context. (A 5th
  pre-existing, unrelated failure in `calculateTimePeriodRange › calculates Q9M with two ranges`
  also showed up — see Notes.)
- Step 4 (after the hook rewrite), with `TZ=Europe/Prague`: **14 passed / 14 total**, full green,
  including `calculateTimePeriodRange`.
- Without `TZ=Europe/Prague` (this shell's default `TZ=PDT`): 13 passed / 14 total — only
  `calculateTimePeriodRange › calculates Q9M with two ranges` fails, by exactly one calendar day.
  Confirmed via `git stash` that this same test fails identically against the **original,
  pre-task** hook and test file under the same `TZ=PDT` shell — it is a pre-existing
  timezone-dependent test, entirely unrelated to this task's change, not something this task
  introduced or is responsible for fixing.

## How to verify

```bash
cd frontend
npm ci --legacy-peer-deps   # see Notes — plain `npm install`/`npm ci` currently ERESOLVE-fails here
TZ="Europe/Prague" CI=true npx react-scripts test --watchAll=false src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx
npm run build
```

Expect: all 14 tests green; build fails with exactly one `TS2345` error in
`src/components/pages/ManufacturingStockAnalysis.tsx` (line 286, `item.productFamily!`) — that
file is Task 2's responsibility, not this task's.

## Notes

- **Environment quirk, not a code issue:** this worktree had no `node_modules` installed. Plain
  `npm install`/`npm ci` fails with an ERESOLVE conflict between the pinned `typescript@^4.9.5` and
  `react-i18next@15.7.4`'s `peerOptional typescript@^5`. Used `npm ci --legacy-peer-deps` to
  proceed (does not touch `package.json`/`package-lock.json`). Worth fixing at the repo level at
  some point but out of scope here.
- **Timezone-dependent pre-existing test:** `calculateTimePeriodRange › calculates Q9M with two
  ranges` fails under a `TZ=PDT` (US Pacific) shell but passes under `TZ=Europe/Prague`. Verified
  via `git stash` that this is not a regression from this task — it fails identically on the
  original pre-task file. Flagging so it isn't mistaken for a side effect of this change; the CI
  environment presumably runs in a TZ where it's stable.
- `npx tsc --noEmit -p tsconfig.json` currently reports 38 syntax errors, all inside
  `node_modules/react-i18next/*.d.ts` — an artifact of the `--legacy-peer-deps` TS4/TS5 version
  mismatch in this ad-hoc install, not a real project issue. Used `npm run build` (react-scripts /
  fork-ts-checker) instead for the authoritative type-check, per the task's Step 5 command.
- CRA's build stops at the **first** TS error per compile pass — confirmed by direct `grep` that
  the one error shown (`ManufacturingStockAnalysis.tsx:286`) is real and not in the hook file, and
  that only `ManufacturingStockAnalysis.tsx` and `ManufactureBatchPlanning.tsx` import from this
  hook anywhere in `src/`. It's possible `ManufacturingStockAnalysis.tsx` and/or
  `ManufactureBatchPlanning.tsx` have **additional** TS errors beyond this first one that won't
  surface until this one is fixed — worth keeping in mind for whichever task fixes
  `ManufacturingStockAnalysis.tsx` (Task 2), in case build still fails after that one line is
  addressed.
- `state.json` under `artifacts/feat-3730/` was already modified (unstaged) in this worktree before
  this task began; left it untouched/uncommitted per the instruction to only commit files genuinely
  part of this change.
- No deviations from the task context's exact specified code — both files were written verbatim as
  given in Steps 1 and 3.

## PR Summary

Rewires `useManufacturingStockAnalysisQuery` off the hand-rolled `(apiClient as any).http.fetch`
call and onto the generated OpenAPI client's `manufacturingStockAnalysis_GetStockAnalysis` method,
replacing six hand-coded DTO/enum types with re-exports from the generated client (FR-1/FR-2) and
adding a single, shared `toGeneratedTimePeriod` conversion point at the API boundary that preserves
the existing Q9M-omission behavior (FR-3). No consumer-facing type/import changes (FR-4) —
`ManufacturingStockAnalysis.tsx` and `ManufactureBatchPlanning.tsx` still import everything from
this hook module unchanged.

### Changes
- `frontend/src/api/hooks/useManufacturingStockAnalysis.ts` — rewritten to call the generated
  client method; local types replaced with generated-client re-exports; new exported
  `toGeneratedTimePeriod` helper.
- `frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx` — query-hook tests
  rewritten to mock the generated client method instead of `http.fetch`.

## Status
DONE
