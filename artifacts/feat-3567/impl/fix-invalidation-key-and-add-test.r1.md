# Implementation: fix-invalidation-key-and-add-test

## What was implemented
Fixed the `onSuccess` handler of `useEnqueueInvoiceImport` in
`frontend/src/api/hooks/useAsyncInvoiceImport.ts`, which was invalidating the
React Query cache with the literal key `["invoices", "jobs"]`. That key never
prefix-matched the running-jobs query key
`["invoices", "import", "jobs", "running"]` registered by
`useRunningInvoiceImportJobs` (they diverge right after `"invoices"`), so the
running-jobs list never refreshed after a new import was enqueued.

The fix replaces the ad-hoc literal with the module's existing canonical key
factory, `invoiceImportQueryKeys.jobs()`, which returns
`["invoices", "import", "jobs"]` — a true prefix of both the running-jobs key
and the per-job-status key (`["invoices", "import", "jobs", "status", jobId]`).

A regression test was added that renders the mutation hook, spies on the
`QueryClient`'s `invalidateQueries`, resolves the mutation, and asserts:
- the invalidation call used `invoiceImportQueryKeys.jobs()` exactly,
- the old broken literal `["invoices", "jobs"]` was never used,
- `invoiceImportQueryKeys.runningJobs()` is indeed a prefix-extension of
  `invoiceImportQueryKeys.jobs()` (making the "why this matters" point
  concrete in the test itself).

Note: the task context assumed
`frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts` was a new
file, but it already existed with a full suite of tests covering
`useInvoiceImportJobStatus` / `useRunningInvoiceImportJobs` polling behavior
(`describe('useAsyncInvoiceImport - Job Polling Logic', ...)`). Rather than
overwrite it, the new test was added as an additional `describe` block in
the same file, reusing the project's existing shared test helpers
(`createMockApiClient`, `mockAuthenticatedApiClient`,
`createQueryClientWrapper` from `frontend/src/api/testUtils.ts`) to match the
established style of the pre-existing tests in that file, rather than the
bespoke `jest.mock`/manual-wrapper approach sketched in the task context.

## Files created/modified
- `frontend/src/api/hooks/useAsyncInvoiceImport.ts` — one-line fix: `onSuccess` now calls `queryClient.invalidateQueries({ queryKey: invoiceImportQueryKeys.jobs() })` instead of the stale literal `[...QUERY_KEYS.invoices, "jobs"]`.
- `frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts` — added a new `describe('useEnqueueInvoiceImport onSuccess invalidation', ...)` block (existing polling-logic tests untouched); also removed a pre-existing unused import (`getAuthenticatedApiClient`) that ESLint flagged as dead code in this file.

## Tests
- `frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts`:
  - Pre-existing: `useInvoiceImportJobStatus` and `useRunningInvoiceImportJobs` polling-interval and stale-time behavior (7 tests, unchanged, still passing).
  - New: `useEnqueueInvoiceImport onSuccess invalidation` — verifies the mutation's `onSuccess` invalidates the cache with `invoiceImportQueryKeys.jobs()`, not the old broken literal, and that this key is a true prefix of the running-jobs key.
- Confirmed the new test fails against the pre-fix code (invalidateQueries called with `['invoices', 'jobs']` instead of the expected key) and passes after the fix.

## How to verify
```
cd frontend
CI=true npm test -- useAsyncInvoiceImport --watchAll=false   # 8/8 tests pass
npm run lint                                                  # no new errors in changed files
npm run build                                                 # compiles successfully
```

## Notes
- The task context's literal test-file template assumed the file didn't exist yet; it did, with unrelated valuable tests, so the test was appended as a new `describe` block using the project's established `testUtils` helpers instead of the from-scratch mocking shown in the task context. This is a deviation from the literal task text but preserves existing test coverage and follows repo conventions (surgical, matches existing style).
- Removed one pre-existing unused import (`getAuthenticatedApiClient`) from the test file as a minimal drive-by cleanup, since it was flagged by `npm run lint` in the very file being touched and had zero usages before or after this change.
- Full-project `npm run lint` shows ~148 pre-existing errors/warnings across unrelated files (testing-library rule violations, an unused import in `CatalogList.test.tsx`, etc.) — none of these are in the two files changed by this task, and none were introduced by this change.
- No changes were made to `artifacts/feat-3567/state.json` (it had a pre-existing modification from the pipeline before this task started, left as-is).

## PR Summary

### Changes
- Fixed `useEnqueueInvoiceImport`'s `onSuccess` handler in `frontend/src/api/hooks/useAsyncInvoiceImport.ts` to invalidate the React Query cache using the canonical `invoiceImportQueryKeys.jobs()` factory instead of a stale, non-matching literal key (`["invoices", "jobs"]`), which meant the running-jobs list never refreshed after enqueuing a new invoice import.
- Added a regression test in `frontend/src/api/hooks/__tests__/useAsyncInvoiceImport.test.ts` asserting the correct invalidation key is used and the old broken key is not.

### Test plan
- `cd frontend && CI=true npm test -- useAsyncInvoiceImport --watchAll=false` — all 8 tests pass.
- `npm run lint` and `npm run build` — no new errors introduced.

## Status
DONE
