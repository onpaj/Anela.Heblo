# Code Review: Fix onSuccess invalidation key in useEnqueueInvoiceImport

## Summary
The implementation replaces the stale literal `[...QUERY_KEYS.invoices, "jobs"]` with the canonical `invoiceImportQueryKeys.jobs()` factory in `useEnqueueInvoiceImport`'s `onSuccess`, exactly as required by FR-1 and the arch review's Decision 1/2. The developer deviated from the task context's literal test-file scaffold (writing a new file with bespoke `jest.mock`) by instead appending a new `describe` block to the pre-existing `useAsyncInvoiceImport.test.ts`, reusing the file's established `testUtils` helpers — a reasonable, lower-risk choice since the "new file" premise in the task context was factually wrong (the file already existed with 7 passing tests). Independent verification confirms the diff matches the summary and all 8 tests pass.

## Review Result: PASS

### task: fix-invalidation-key-and-add-test
**Status:** PASS

**Verification performed:**
- `git show 0e64b53`: diff touches exactly the two expected files. The hook change is a single line: `queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.invoices, "jobs"] })` → `queryClient.invalidateQueries({ queryKey: invoiceImportQueryKeys.jobs() })`. No other lines in `useAsyncInvoiceImport.ts` changed — `mutationFn`, URL, request body, error handling, other hooks, and the `invoiceImportQueryKeys` factory itself are untouched, satisfying FR-1's "no other behavior changed" criterion and the spec's Out-of-Scope constraints.
- `QUERY_KEYS` import remains in the file and is still used (lines 50, 92 of `useAsyncInvoiceImport.ts`, in `useInvoiceImportJobStatus` and `useRunningInvoiceImportJobs`). Confirmed no other file in `frontend/src` references the stale `["invoices", "jobs"]` literal.
- Test file diff: the pre-existing `describe('useAsyncInvoiceImport - Job Polling Logic', ...)` block (7 tests) is untouched aside from an added import (`useEnqueueInvoiceImport`, `invoiceImportQueryKeys`) and removal of an unused `getAuthenticatedApiClient` direct import (dead code, correctly flagged by lint and safely removable since the test uses the `client` module mock + `testUtils` helpers instead). A new `describe('useEnqueueInvoiceImport onSuccess invalidation', ...)` block asserts: (a) `invalidateQueries` was called with `invoiceImportQueryKeys.jobs()` exactly, (b) the stale `['invoices', 'jobs']` literal was never used, (c) `runningJobs()` is a true prefix-extension of `jobs()`. This satisfies the arch review's Specification Amendment #1 and FR-1's acceptance criteria.
- Ran `cd frontend && CI=true npm test -- useAsyncInvoiceImport --watchAll=false` myself: **8/8 tests pass**, matching the implementation summary's claim exactly.
- Ran `npx eslint` on both changed files directly: zero errors/warnings, consistent with the summary's claim that the ~148 pre-existing lint issues elsewhere are unrelated.
- The chosen `jobs()` (broad) key over `runningJobs()` (narrow) matches the spec's explicitly preferred implementation and the arch review's Decision 1.

No functional requirement is unmet, no architecture guidance is contradicted, the required regression test exists and passes, and no correctness bugs were found. The deviation from the task context's literal test scaffold is justified (the file already existed) and does not weaken coverage — assertions are equivalent or stronger (it also checks the prefix relationship) than what the task context specified.

## Docs to Update
None. This is an internal bug fix with no public API, UI, or architecture surface change; no doc in `docs/` references this invalidation key.

## Overall Notes
- The task context's assumption that `useAsyncInvoiceImport.test.ts` was a new file was incorrect; the developer's summary transparently flagged this and adapted correctly rather than silently overwriting existing coverage.
- Minor, non-blocking observation: the new test's `wrongKey` comparison uses `JSON.stringify` equality inside `.some(...)`, which is slightly more verbose than necessary but functionally correct and harmless.
- The drive-by removal of the unused `getAuthenticatedApiClient` import in the test file is a minimal, justified cleanup (dead code in a file already being edited, flagged by lint) and does not violate the "surgical changes" principle in any meaningful way.
