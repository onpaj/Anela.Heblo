# Implementation: update-tests-for-typed-client-methods

## What was implemented

Rewrote `frontend/src/api/hooks/__tests__/useDashboard.test.tsx` to match the typed-client-method migration of `useDashboard.ts` from the previous task. The mock setup no longer fakes `{ baseUrl, http: { fetch } }` on the API client; instead it mocks the six typed methods directly (`dashboard_GetAvailableTiles`, `dashboard_GetUserSettings`, `dashboard_GetTileData`, `dashboard_SaveUserSettings`, `dashboard_EnableTile`, `dashboard_DisableTile`) and asserts on calls to those methods rather than on raw fetch URLs/payloads.

Two new tests were added to exercise the `isForbidden` 403-fallback paths added to the hooks:
- `useUserDashboardSettings`: "should return empty fallback settings on 403 Forbidden" — rejects with `{ status: 403 }` and expects `{ tiles: [], lastModified: <ISO string> }`.
- `useTileData`: "should return empty array fallback on 403 Forbidden" — rejects with `{ status: 403 }` and expects `[]`.

The former "API URL construction" describe block (which asserted on raw fetch URL strings) was replaced with a "typed client method calls" describe block asserting the correct typed methods are invoked with the correct arguments (mirroring the restructuring `useBankStatements.test.ts` underwent in commit `2e178ff`).

All 11 steps from the task context (`artifacts/feat-3442/task-context/update-tests-for-typed-client-methods.md`) were executed exactly as specified. The "before" code blocks in the task context matched the file's actual content verbatim (only trivial whitespace differences, e.g. trailing spaces on the `jest.clearAllMocks()` line), so no interpretation/adaptation was needed beyond what the task context already anticipated (e.g. `lastModified` now a `Date` object at the mock-input layer since `toUserDashboardSettings` calls `.toISOString()` on it).

## Files created/modified

- `frontend/src/api/hooks/__tests__/useDashboard.test.tsx` — rewritten to mock the six typed `dashboard_*` client methods instead of raw `fetch`; added two 403-fallback tests; replaced the "API URL construction" block with a "typed client method calls" block.

## Tests

- `frontend/src/api/hooks/__tests__/useDashboard.test.tsx` — 17 tests, all passing. Covers `useAvailableTiles`, `useUserDashboardSettings` (including 403 fallback), `useTileData` (including 403 fallback), `useSaveDashboardSettings`, `useEnableTile`, `useDisableTile`, and typed-method call-argument assertions.

## How to verify

```
grep -n "mockFetch\|baseUrl\|http\.fetch" frontend/src/api/hooks/__tests__/useDashboard.test.tsx
# expect: no output

cd frontend && CI=true npx react-scripts test src/api/hooks/__tests__/useDashboard.test.tsx --watchAll=false
# expect: Test Suites: 1 passed, Tests: 17 passed
```

## Notes

- **Deviation from the task context's literal verification command.** The task context specifies `npx jest src/api/hooks/__tests__/useDashboard.test.tsx --no-coverage`. Running that verbatim fails with a babel parse error on JSX syntax (`Unexpected token, expected ","` at the `return ({ children }: ...) => (` line) — a bare `jest` binary resolved via `npx` does not pick up this Create React App project's babel/JSX transform config. The project's actual test script (`package.json` `"test": "react-scripts test"`) wraps jest with CRA's config. I ran `CI=true npx react-scripts test src/api/hooks/__tests__/useDashboard.test.tsx --watchAll=false` instead, which is the equivalent non-interactive invocation of the project's real test runner. All 17 tests passed. This is an environment/tooling difference, not a defect in the rewritten test file.
- The worktree had no `node_modules` installed at all; I ran `npm ci --legacy-peer-deps` to install (the repo has a pre-existing peer-dependency conflict between `typescript@4.9.5` and `react-i18next@15.7.4`'s `typescript@^5` peer, unrelated to this change).
- `artifacts/feat-3442/state.json` shows as modified in git status — this is pipeline-harness-managed task-tracking state (not edited by me directly), reflecting task progress transitions during this run.

## Status
DONE
