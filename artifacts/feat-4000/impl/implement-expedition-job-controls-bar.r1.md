# Implementation: implement-expedition-job-controls-bar

## What was implemented

Created `ExpeditionJobControlsBar`, a self-contained component that owns the "expedition robot" toggle/next-run status and the three job-control action buttons (Tisknout zakázku, Spustit tisk oprav, Spustit tisk) that previously lived inline in `ExpeditionListArchivePage`. The component was written verbatim per the task-context spec: same JSX, same handlers, same permission gating (`jobs.trigger.read` / `jobs.disable.read`), and the same `PrintOrderModal` wiring, invalidating the shared `QUERY_KEYS.expeditionListArchive` query on a successful order print. The "Obnovit" button and all archive-browsing (date list, items table, reprint dialog) code were intentionally excluded — those stay on the page component and are out of scope for this task.

This task did not touch `ExpeditionListArchivePage.tsx` itself; wiring the new component into the page is the next task (`wire-controls-bar-into-page`) per the task plan.

## Files created/modified

- `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` — new component (created, matches task-context Step 1 exactly)

The test file `frontend/src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx` already existed on the branch (committed by the prior `create-expedition-job-controls-bar-failing-tests` task as a TDD red step) and was not modified — it was written against this same task-context spec and needed no changes to go green.

## Tests

`ExpeditionJobControlsBar.test.tsx` — 9 tests across two `describe` blocks, mirroring the equivalent tests already covering this behavior in `ExpeditionListArchivePage.test.tsx`:
- "expedition robot toggle" (4 tests): reflects enabled/disabled state, calls the status mutation with the negated value on click, renders an em dash and disables the toggle when the job is missing.
- "permission gating" (5 tests): run button and toggle visibility gated correctly by `jobs.trigger.read` / `jobs.disable.read`, individually and combined.

## How to verify

```bash
cd frontend
npm install --legacy-peer-deps   # node_modules was not present in this worktree; CI uses --legacy-peer-deps (see .github/workflows/ci-feature-branch.yml)
CI=true npx react-scripts test src/components/pages/ExpeditionListArchive/__tests__/ExpeditionJobControlsBar.test.tsx --watchAll=false --coverage=false
npx tsc --noEmit -p tsconfig.json
npm run lint
```

Results:
- Tests: **9/9 passed**.
- `tsc --noEmit`: pre-existing, unrelated syntax errors inside `node_modules/react-i18next/*.d.ts` (a repo-wide TypeScript/type-defs version mismatch present before this change); zero errors attributable to `ExpeditionJobControlsBar.tsx` or any other `src/` file.
- `npm run lint`: pre-existing repo-wide `testing-library/*` and `import/first` violations in unrelated existing test files (236 errors total, none touching this task's files); zero errors or warnings for `ExpeditionJobControlsBar.tsx` or its test file.

## Notes

- **Environment**: the worktree had no `node_modules` installed. Plain `npm install`/`npm ci` fails on a pre-existing `@types/node` (^16 declared) vs. `knip` (requires `@types/node` >=18) peer-dependency conflict unrelated to this task; installed with `--legacy-peer-deps`, matching what `.github/workflows/ci-feature-branch.yml` and `ci-main-branch.yml` already do. The task-context's suggested `npx jest ...` command doesn't work standalone (it pulls a bare, non-project jest with no TS/babel config); used the project's real test runner, `react-scripts test`, instead, pointed at the same test file.
- **Act() warning**: `react-scripts test` shows an "update to ToastProvider ... not wrapped in act(...)" warning on the "calls the status mutation with the negated value when toggled" test. This is pre-existing — the identical warning appears running the equivalent test in `ExpeditionListArchivePage.test.tsx` today, since the test file (and its `waitFor` pattern) was copied verbatim from that page's tests. Not introduced by this change; left as-is per "surgical changes only" — flagging for the reviewer/architect rather than silently reshaping the pre-existing test.
- No deviations from the task-context's exact component content.

## PR Summary
Extracted the expedition-robot toggle/next-run status and the three job-control action buttons out of `ExpeditionListArchivePage` into a new, self-contained `ExpeditionJobControlsBar` component, as the first implementation step of the architecture-review split (issue #4000). The component owns its own data (permissions, recurring-job query/mutations, print-fix mutation, print-order modal) and needs no props from the page; wiring it into the page itself is a separate follow-up task. The pre-existing test file for this component (written in an earlier TDD task) now passes unchanged.

### Changes
- `frontend/src/components/pages/ExpeditionListArchive/ExpeditionJobControlsBar.tsx` — new component: expedition-robot toggle + next-run label, "Tisknout zakázku" / "Spustit tisk oprav" / "Spustit tisk" buttons, `PrintOrderModal` integration

## Status
DONE
