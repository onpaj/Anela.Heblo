# Implementation: urlutils-unit-tests

## What was implemented
Added a new unit test file `frontend/src/utils/__tests__/urlUtils.test.ts` covering the two pure functions in `frontend/src/utils/urlUtils.ts`: `createFilteredUrl` and `isTileClickable`. No production code was changed. `getTileTooltip` was intentionally left untested per the task scope.

## Files created/modified
- `frontend/src/utils/__tests__/urlUtils.test.ts` — new test file with two `describe` blocks:
  - `createFilteredUrl`: 9 tests covering `false`/`0` inclusion, `null`/`undefined`/`''` exclusion, all-excluded/empty-filters returning `baseUrl` unchanged (no `?`), valid-filter query string shape, and a mixed-object case asserting includable pairs are present and excludable keys are absent.
  - `isTileClickable`: 5 tests covering `filters: {}` truthy-object behavior (documented in test name), `enabled: false`, missing `filters`, missing `drillDown`, and the baseline positive case.

## Tests
Ran from `frontend/`:
```
CI=true npx react-scripts test --testPathPattern=urlUtils --coverage --watchAll=false --collectCoverageFrom='src/utils/urlUtils.ts' --watchAll=false
```
Result:
```
-------------|---------|----------|---------|---------|-------------------
File         | % Stmts | % Branch | % Funcs | % Lines | Uncovered Line #s
-------------|---------|----------|---------|---------|-------------------
All files    |     100 |     90.9 |     100 |     100 |
 urlUtils.ts |     100 |     90.9 |     100 |     100 | 51
-------------|---------|----------|---------|---------|-------------------

Test Suites: 286 passed, 286 total
Tests:       5 skipped, 2355 passed, 2360 total
```
(Line 51 uncovered is inside `getTileTooltip`, out of scope for this task.) All 2355 non-skipped tests across the whole suite passed, including the 14 new `urlUtils.test.ts` tests — no regressions.

Note: `node_modules` was missing in this worktree and `npm install` failed with an ERESOLVE peer-dependency conflict (react-i18next requires TypeScript ^5, project pins ^4.9.5) that is pre-existing and unrelated to this change. Since `package.json`/`package-lock.json` are byte-identical to the main checkout at `/home/user/Anela.Heblo/frontend`, `node_modules` was copied from there instead of forcing an install with `--legacy-peer-deps`.

## How to verify
```
cd frontend
npm test -- --testPathPattern=urlUtils --coverage --watchAll=false
```
Confirm all `urlUtils.test.ts` tests pass and `urlUtils.ts` coverage is ≥60% (actual: 100% stmts/funcs/lines, 90.9% branch).

Lint check:
```
npx eslint src/utils/__tests__/urlUtils.test.ts
```
returns no output (clean). Full `npm run lint` shows 148 pre-existing errors in unrelated test files (testing-library rule violations, import ordering, etc.) — none in the new file, confirmed baseline noise not introduced by this change.

`git diff --stat -- frontend/src/utils/urlUtils.ts` is empty — source file untouched.

## Notes
- Coverage exceeds the 60% threshold (100% statements/lines/functions, 90.9% branches) using only the tests specified in the task's Steps 3 and 4 — no extra tests were added.
- `getTileTooltip` remains untested per explicit task scope, leaving one branch (the tooltip-return line) uncovered; this does not affect the required threshold.
- Committed only `frontend/src/utils/__tests__/urlUtils.test.ts`; `artifacts/feat-3503/state.json` was left modified/uncommitted per instructions (pipeline handles that separately).

## Status
DONE
