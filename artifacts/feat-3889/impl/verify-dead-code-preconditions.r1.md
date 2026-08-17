# Implementation: verify-dead-code-preconditions

## What was implemented

This was a read-only verification task with 6 numbered steps, confirming the
preconditions for a later dead-code deletion task (removal of the transport
box "allowed transitions" plumbing: `useTransportBoxTransitions.ts` and its
callers). All 6 steps were executed and every actual result matched the
"Expected" result stated in the task context file. No files under `frontend/`
or `docs/` were modified, and nothing was committed.

## Files created/modified

(none — read-only task)

## Tests

Ran the regression guard specified in Step 5:

```bash
cd frontend
CI=true npm test -- --watchAll=false --testPathPattern="api/hooks/__tests__/useTransportBoxes.test.ts"
```

Result: `Test Suites: 1 passed, 1 total`, `Tests: 12 passed, 12 total`, exit
code 0. The specific target test,
`useChangeTransportBoxState › should call API and invalidate queries on
success`, passed.

Note: `frontend/node_modules` was not present in the worktree, so `npm ci`
was required before the test could run. Plain `npm ci` failed with an
`ERESOLVE` peer-dependency conflict (`react-i18next@15.7.4` wants
`typescript@^5`, the project pins `typescript@^4.9.5`) — this is a
pre-existing condition of the repo's dependency tree, unrelated to this
task. `npm ci --legacy-peer-deps` succeeded and installed against the
existing `package-lock.json` without modifying it. `node_modules` is
gitignored and does not appear in `git status`.

## How to verify

1. `git rev-parse --abbrev-ref HEAD` → `feature/3889-Arch-Review-Transportboxes-Usetransportboxtransiti`
2. `git status --short` → only `artifacts/feat-3889/state.json` shows modified (pre-existing, unrelated to this task); nothing under `frontend/` or `docs/`.
3. `grep -rn "allowed-transitions\|GetAllowedTransitions" backend/src --include=*.cs` → zero output, exit code 1.
4. `grep -rn "useTransportBoxTransitions\|useAllowedTransitionsQuery\|GetAllowedTransitionsResponse\|transportBoxTransitions" frontend/src frontend/test docs/` → exactly the 10 expected hits plus the 1 expected extra hit in the historical plan doc (11 lines total), none of them an `import` of the hook. `ls frontend/src/api/hooks/index.ts` → `No such file or directory` (no barrel file).
5. `sed -n '177,201p' frontend/src/api/hooks/useTransportBoxes.ts` → matches the baseline text in the task file verbatim.
6. `CI=true npm test -- --watchAll=false --testPathPattern="api/hooks/__tests__/useTransportBoxes.test.ts"` (from `frontend/`, after `npm ci --legacy-peer-deps`) → 12/12 tests pass.
7. `git status --short frontend docs` → empty output.

## Notes

All 6 steps matched their "Expected" results exactly:

- **Step 1 (branch/clean status):** PASS. Correct branch checked out.
  `git status --short` shows only `artifacts/feat-3889/state.json` modified
  (pre-existing before this task started); nothing under `frontend/` or
  `docs/`, consistent with "files under `artifacts/` may appear — that is
  fine."
- **Step 2 (backend route absence):** PASS. `grep -rn
  "allowed-transitions\|GetAllowedTransitions" backend/src --include=*.cs`
  produced zero output (exit code 1).
- **Step 3 (hook importer enumeration):** PASS. The grep produced exactly
  the 10 expected lines at the exact expected line numbers, plus the one
  documented extra hit in
  `docs/superpowers/plans/2026-06-13-telemetry-stockupoperations-summary-403-storm.md:629`
  (a quoted `jest.mock` literal in a historical plan, left untouched as
  instructed). No line is an `import` of `useTransportBoxTransitions`.
  `frontend/src/api/hooks/index.ts` does not exist, confirming no barrel
  file.
- **Step 4 (baseline `onSuccess` snippet):** PASS. `sed -n '177,201p'
  frontend/src/api/hooks/useTransportBoxes.ts` output is byte-for-byte
  identical to the expected snippet in the task file.
- **Step 5 (green regression baseline):** PASS. 12/12 tests passed, 1/1
  test suite passed, exit code 0. (Required an `npm ci --legacy-peer-deps`
  first since `frontend/node_modules` was absent from the worktree; this
  did not alter `package-lock.json` or any tracked file.)
- **Step 6 (no commit / no diff):** PASS. `git status --short frontend
  docs` returned empty output both before and after running the test
  suite.

No mismatches were found. The premise of the planned deletion (that
`useTransportBoxTransitions.ts` and the backend `allowed-transitions`
route are dead code with no live importers) holds.

## PR Summary

All 6 verification steps passed exactly as expected: the backend
`allowed-transitions`/`GetAllowedTransitions` route does not exist, the
`useTransportBoxTransitions` hook has zero real importers (only
test-fixture/query-key string matches and one historical doc), no barrel
file re-exports it, the pre-change baseline snippet matches verbatim, and
the target Jest suite is green (12/12). No files were modified and nothing
was committed; the premise for the subsequent dead-code removal task is
confirmed sound.
