# Implementation: update-archive-page-tests

## What was implemented
Narrowed `ExpeditionListArchivePage.test.tsx` to cover only archive-browsing behavior. Replaced the mocks/setup block (previously pulling in `useExpeditionList`, `useRecurringJobs`, and `PermissionsContext` for the robot-toggle/permission-gating tests) with the archive-only subset: mocks for `useExpeditionListArchive` and `api/client`, plus a stub for the new `ExpeditionJobControlsBar` component (`() => null`) so the page renders without needing the job-controls hooks. Deleted the `"ExpeditionListArchivePage – expedition robot toggle"` and `"ExpeditionListArchivePage – permission gating"` `describe` blocks, since that coverage now lives in `ExpeditionJobControlsBar.test.tsx` (written by a prior task in this same feature). Kept the `"ExpeditionListArchivePage – refresh button"` `describe` block unchanged as the only remaining block in the file.

## Files created/modified
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — trimmed to archive-only mocks/setup and the single "refresh button" describe block (152 lines removed net, from 278 lines to 132 lines).

## Tests
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — 4 tests (refresh button: renders, invalidates queries on click, disables while pending, re-enables after completion). Ran via `CI=true npx react-scripts test src/pages/__tests__/ExpeditionListArchivePage.test.tsx --watchAll=false`: **PASS**, 4/4, no act() warnings or console errors.
- `frontend/src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx` — 9 tests (robot toggle + permission gating, migrated by a prior task, unmodified here). Ran standalone: **PASS**, 9/9. Note: this file emits one pre-existing `act()` console warning (`setToasts` inside `handleToggleJob`) — present before my change, not introduced by it, and does not fail the test.
- Full suite: `CI=true npm test -- --watchAll=false`: **PASS** — 329 test suites passed / 329 total, 2770 tests passed, 5 skipped, 0 failed. (The console.error output visible in the run for `src/test/App.test.tsx` is an intentional negative-path assertion in that pre-existing test — not a failure.)

## How to verify
```bash
cd frontend
npm install --legacy-peer-deps   # node_modules were not present in this checkout; installs cleanly with this flag due to a pre-existing @types/node/knip peer conflict unrelated to this change
CI=true npx react-scripts test src/pages/__tests__/ExpeditionListArchivePage.test.tsx --watchAll=false
CI=true npm test -- --watchAll=false
```

## Notes
- The task spec's Step 3 command (`npx jest src/pages/__tests__/ExpeditionListArchivePage.test.tsx --no-coverage`) does not work in this project: it's a Create-React-App (react-scripts) project, and a bare `npx jest` bypasses CRA's babel/TS transform config, failing with a TSX parse error. I ran the equivalent via `react-scripts test` (what `npm test` invokes) instead, which is the project's actual test runner, per `package.json`'s `"test": "react-scripts test"`.
- This worktree had no `node_modules` installed. `npm ci` failed on a pre-existing peer-dependency conflict between `knip@5.88.1` (wants `@types/node@>=18`) and the pinned `@types/node@^16.18.108` — unrelated to this change. Installed with `npm install --legacy-peer-deps` to proceed.
- No production code was touched; only the test file was edited, matching the task's constraint.
- End state of the mocks/describe blocks matches the spec's required end state exactly (verified by direct read of the final file): only `useExpeditionListArchive`, the stubbed `ExpeditionJobControlsBar`, and `api/client` are mocked; only the "refresh button" describe block remains.

## PR Summary
Splits `ExpeditionListArchivePage.test.tsx` so it only exercises archive-browsing behavior (date list, item list, refresh button), removing the robot-toggle and permission-gating tests that were migrated to `ExpeditionJobControlsBar.test.tsx` in a prior task. The page's dependency on the now-extracted `ExpeditionJobControlsBar` component is stubbed to a no-op render, so the file no longer needs to mock `useExpeditionList`, `useRecurringJobs`, or `PermissionsContext`.

### Changes
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — removed mocks/setup for job-control hooks and permissions context, added a stub mock for `ExpeditionJobControlsBar`, deleted the two migrated `describe` blocks, kept the "refresh button" `describe` block as-is.

## Status
DONE
