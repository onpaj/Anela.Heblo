# Implementation: write-tests

## What was implemented
Added a unit test suite for `useSubmitLeafletFeedbackMutation` in `frontend/src/api/hooks/useLeaflet.ts`, covering the three branches of its `mutationFn`: HTTP 409 (already-submitted, no throw), a non-ok/non-409 response (throws with the status code in the message), and an ok response (resolves with the parsed JSON body). No production code was changed — this is a coverage-only task.

## Files created/modified
- `frontend/src/api/hooks/__tests__/useLeaflet.test.ts` — new test file with 3 tests for `useSubmitLeafletFeedbackMutation`, mocking `../../client` (`getAuthenticatedApiClient`, `QUERY_KEYS`) and driving the hook through `renderHook` + a `QueryClientProvider` wrapper.

## Tests
- `frontend/src/api/hooks/__tests__/useLeaflet.test.ts`
  - `returns { success: false, alreadySubmitted: true } without throwing on HTTP 409`
  - `throws with the status code in the message on a non-ok, non-409 response`
  - `returns the parsed JSON body on an ok response`

## How to verify
```bash
cd frontend
npm install --legacy-peer-deps   # node_modules was absent in the worktree; see Notes
CI=true npx react-scripts test src/api/hooks/__tests__/useLeaflet.test.ts --watchAll=false
CI=true npm test -- --watchAll=false   # full suite, no regressions
npm run lint
CI=true npm run build
```

## Notes
- Deviation from the task's literal Step 2 command: `npx jest src/api/hooks/__tests__/useLeaflet.test.ts --no-coverage` fails in this repo because the project isn't runnable via bare `npx jest` (it relies on CRA's `react-scripts test` Jest wrapper/config, e.g. TS/babel transforms). Ran the equivalent via `npx react-scripts test ... --watchAll=false` instead (same file, same assertions), which is also what `npm test` invokes under the hood. Test content matches the spec verbatim.
- The worktree had no `node_modules` at all, so `npm ci` was attempted first (failed on a peer-dependency conflict between `@types/node` versions pulled by `jest`/`react-scripts` and `knip`), then `npm install --legacy-peer-deps` succeeded and was used to run all subsequent verification steps. This is an environment-setup step only — no `package.json`/`package-lock.json` changes were made or committed.
- Full suite: 324 test suites, 2721 passed, 5 skipped (pre-existing skips, unrelated to this change), 0 failed.
- `npm run lint` reports 236 pre-existing errors across ~15 unrelated test files (mostly `testing-library/no-node-access` and `import/first` rule violations). None are in the new file — confirmed via `git status --porcelain` (only the new test file plus a pre-existing, already-modified `artifacts/feat-3942/state.json` are touched) and by grepping the lint output for the new filename (no hits). These pre-existing violations were left untouched per the "surgical changes" rule.
- `npm run build` compiled successfully (`Compiled successfully.`), confirmed both via a foreground rerun and the earlier backgrounded run.
- `useLeaflet.ts` was read-only, as instructed — verified no diff exists for it.

## PR Summary

Adds a missing unit test suite for `useSubmitLeafletFeedbackMutation`'s `mutationFn`, which previously had no direct coverage for its HTTP 409 (already-submitted) short-circuit, its generic error-throw path, or its normal success path. The tests mock `getAuthenticatedApiClient`/`QUERY_KEYS` from `../../client` and drive the mutation through `renderHook` with a `QueryClientProvider` wrapper, asserting on `mutateAsync`'s resolved value or thrown error for each of the three response shapes. Purely additive — no production code changes.

### Changes
- `frontend/src/api/hooks/__tests__/useLeaflet.test.ts` — new: 3 tests covering the 409 / non-ok-non-409 / ok response branches of `useSubmitLeafletFeedbackMutation`.

## Status
DONE
