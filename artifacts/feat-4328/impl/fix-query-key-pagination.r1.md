# Implementation: fix-query-key-pagination

## What was implemented

`useDqtRunDetail`'s React Query key (`dataQualityKeys.runDetail`) only
included `runId`, so changing `resultPage`/`resultPageSize` while the
`runId` stayed the same did not bust the cache — the hook silently kept
serving the previously cached page instead of refetching. Updated the key
factory to also include `resultPage` and `resultPageSize`, matching the
convention already used elsewhere in this codebase for detail-style query
keys (e.g. `useCatalog.ts`'s `detail(productCode, monthsBack)`).

## Files created/modified

- `frontend/src/api/hooks/useDataQuality.ts` — `dataQualityKeys.runDetail`
  now takes `(runId, resultPage, resultPageSize)` and includes all three in
  the returned key tuple; the `useQuery` call site in `useDqtRunDetail` was
  updated to pass the new arguments. No other line in `useDqtRunDetail`
  (`queryFn`, `enabled`, `staleTime`, `gcTime`) changed, and the hook's
  public signature is unchanged, so `DqtRunDetail.tsx` needed no changes.
- `frontend/src/api/hooks/__tests__/useDataQuality.test.ts` — added two
  tests inside the existing `describe('useDqtRunDetail', ...)` block:
  - `fetches again when resultPage changes for the same runId` — rerenders
    the hook with a new `resultPage` for the same `runId` and asserts the
    mocked API client is called a second time with the new page.
  - `does not refetch when re-rendered with the same runId and paging` —
    rerenders with identical props and asserts no extra fetch occurs
    (documents the caching behavior that must be preserved).

## Tests

- `frontend/src/api/hooks/__tests__/useDataQuality.test.ts` — all 8 tests
  pass, including the two new regression tests and the pre-existing
  `useDqtRuns`/`useDqtRunDetail`/`useRunDqt` tests.
- `frontend/src/components/data-quality/__tests__/DqtRunDetail.test.tsx` —
  all 4 tests pass; this file mocks `useDqtRunDetail` directly, so it is
  unaffected by the internal key-factory change, confirming no regression
  in the component that consumes the hook.

## How to verify

```bash
cd frontend
CI=true npx react-scripts test src/api/hooks/__tests__/useDataQuality.test.ts --watchAll=false
CI=true npx react-scripts test src/components/data-quality/__tests__/DqtRunDetail.test.tsx --watchAll=false
```

Before the fix, the new test
`fetches again when resultPage changes for the same runId` failed
(`dataQuality_GetRunDetail` was called once total instead of twice) because
the query key didn't change when `resultPage` changed. This was confirmed
by running the test against the pre-fix code before applying Steps 3–4.

## Notes

- The environment's `node_modules` for this worktree had to be populated
  (a fresh `git worktree add` does not install dependencies); `npm install`
  / `npm ci` both hit a pre-existing `@types/node` peer-dependency conflict
  between `knip@5.88.1` and the root project's `@types/node@^16.18.108`
  that is unrelated to this change. Copied `node_modules` from the primary
  checkout (`/home/user/Anela.Heblo/frontend`, identical `package-lock.json`)
  instead of resolving that conflict, since it's out of scope for this task.
- `npx jest <file>` directly does not work in this project — it isn't
  TypeScript/Babel-aware for this CRA setup and fails with a parse error
  unrelated to the change. Used `CI=true npx react-scripts test <file>
  --watchAll=false` instead, which is the project's actual test runner
  (`npm test` → `react-scripts test`).

## PR Summary
Fixed `useDqtRunDetail`'s React Query cache key to include `resultPage`
and `resultPageSize` alongside `runId`, so changing pages actually
triggers a refetch instead of silently reusing a stale cached page. Added
two regression tests covering both the refetch-on-page-change case and the
no-refetch-on-identical-rerender case, and verified the paired
`DqtRunDetail` component test still passes unmodified.

### Changes
- `frontend/src/api/hooks/useDataQuality.ts` — `dataQualityKeys.runDetail` now takes `(runId, resultPage, resultPageSize)`
- `frontend/src/api/hooks/__tests__/useDataQuality.test.ts` — added two tests for the pagination cache-key fix

## Status
DONE
