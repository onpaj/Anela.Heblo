# Implementation: replace-http-fetch-with-generated-client-method

## What was implemented

Replaced the raw, untyped `queryFn` body in `useFinancialOverviewQuery` (which manually built a
query string with `URLSearchParams` and called `(apiClient as any).http.fetch(...)` directly,
bypassing type safety) with a call to the already-generated, typed
`apiClient.financialOverview_GetFinancialOverview(...)` method. No other part of the hook
(signature, defaults, `queryKey`, `staleTime`, `gcTime`, re-exported types) was touched. Added a
new unit test file for the hook, since none previously existed.

## Files created/modified

- `frontend/src/api/hooks/useFinancialOverview.ts` — replaced the `queryFn` body only: removed
  `URLSearchParams` construction, the two `as any` casts, and the manual `fetch`/`response.ok`
  handling; now calls `apiClient.financialOverview_GetFinancialOverview(months, includeStockData,
  excludedDepartments, includeCurrentMonth)` and returns its result directly.
- `frontend/src/api/hooks/__tests__/useFinancialOverview.test.ts` — new test file (created exactly
  per the task-context's specified content), mocking `getAuthenticatedApiClient` from `../../client`
  synchronously (`mockReturnValue`, matching the real non-async call site).

## Tests

`frontend/src/api/hooks/__tests__/useFinancialOverview.test.ts` — 4 test cases:
1. Calls the generated client method with default parameters (`6, true, [], false`).
2. Calls the generated client method with explicit parameters, including a populated
   `excludedDepartments` array (`12, false, ["Sales", "Marketing"], true`).
3. Asserts only the typed `financialOverview_GetFinancialOverview` method is present/used on the
   mocked client (no `.http.fetch` / `URLSearchParams` bypass).
4. Asserts an `Error` rejected by the generated method surfaces as `result.current.error` (an
   `Error` instance with the original message).

Test run (via `npx react-scripts test --testPathPattern=... --watchAll=false`, `CI=true`):

```
PASS src/api/hooks/__tests__/useFinancialOverview.test.ts (6.135 s)
  useFinancialOverviewQuery
    ✓ calls the generated client method with default parameters (76 ms)
    ✓ calls the generated client method with explicit parameters, including excludedDepartments (56 ms)
    ✓ does not use (apiClient as any).http.fetch or manual URLSearchParams (55 ms)
    ✓ surfaces an Error from the generated client method as the query error (55 ms)

Test Suites: 1 passed, 1 total
Tests:       4 passed, 4 total
```

`npm run build` (frontend): `Compiled successfully.` — no TypeScript errors.

`npm run lint` (frontend): no new errors/warnings on the changed files (`useFinancialOverview.ts`
and the new test file do not appear anywhere in lint output). The lint run reports 148 pre-existing
errors / 14 warnings across many unrelated test files in the repo (mostly
`testing-library/no-node-access`, `no-wait-for-multiple-assertions`, etc.) — none introduced by
this change and none touching the files modified here.

Note: `frontend/node_modules` was not present in the worktree; it had to be installed
(`npm install --legacy-peer-deps`, required due to a pre-existing `react-i18next` / `typescript`
peer-dependency conflict in `package.json` unrelated to this task) before build/lint/tests could run.

## How to verify

```bash
cd frontend
npm install --legacy-peer-deps   # only needed if node_modules is missing
npm run build
npm run lint
npx react-scripts test --testPathPattern=src/api/hooks/__tests__/useFinancialOverview.test.ts --watchAll=false
```

Also verify the acceptance greps return nothing:
```bash
grep -n "as any" frontend/src/api/hooks/useFinancialOverview.ts
grep -nE "URLSearchParams|\.http\.fetch" frontend/src/api/hooks/useFinancialOverview.ts
```
Both were confirmed empty.

## Notes

- Only `frontend/src/api/hooks/useFinancialOverview.ts` and the new test file were staged and
  committed. The worktree also had an unrelated modification to `artifacts/feat-3494/state.json`
  (pipeline-managed status tracking, changed to `in_progress` before this task started) — left
  uncommitted/untouched by this commit since it's out of scope for this task.
- `npm ci` failed due to a pre-existing `ERESOLVE` peer-dependency conflict between
  `react-i18next@15.7.4` (wants `typescript@^5`) and the pinned `typescript@^4.9.5` in
  `package.json`. This is unrelated to this task; installed with `--legacy-peer-deps` purely to run
  local validation. No `package.json`/`package-lock.json` changes were made or committed.
- No deviations from the task context's specified code or test content — the edit and the test
  file match the exact blocks given verbatim.

## PR Summary

`useFinancialOverviewQuery` previously bypassed the generated, typed API client by reaching into
its internals via `(apiClient as any).http.fetch(...)`, manually reconstructing the query string
with `URLSearchParams` that duplicated logic already present in the generated
`financialOverview_GetFinancialOverview` method. This change replaces that `queryFn` body with a
direct call to the generated method, eliminating both `as any` casts and the manual URL/fetch
handling while preserving the hook's public signature, defaults, query key, and cache timings
exactly as before. A new unit test suite (`useFinancialOverview.test.ts`) was added — none existed
previously — covering default and explicit parameter passthrough (including a populated
`excludedDepartments` array), confirming only the typed generated method is invoked on the client,
and verifying that errors thrown by the generated method propagate correctly as the query's error
state.

### Changes
- `frontend/src/api/hooks/useFinancialOverview.ts`: `queryFn` now calls
  `apiClient.financialOverview_GetFinancialOverview(months, includeStockData, excludedDepartments, includeCurrentMonth)`
  instead of manually fetching.
- `frontend/src/api/hooks/__tests__/useFinancialOverview.test.ts`: new test file with 4 cases.

## Status
DONE
