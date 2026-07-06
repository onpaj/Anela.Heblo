### task: urlutils-unit-tests

## Goal
Close the unit-test coverage gap for `frontend/src/utils/urlUtils.ts` by adding a new test file that covers the edge cases of the two pure functions `createFilteredUrl` and `isTileClickable`. No production code changes — this is a test-only task against existing, unmodified source.

## Files
- Create: `frontend/src/utils/__tests__/urlUtils.test.ts`
- Reference only (read, do not modify): `frontend/src/utils/urlUtils.ts`
- Reference only (existing convention example): `frontend/src/utils/__tests__/dateUtils.test.ts`

## Steps
1. Create `frontend/src/utils/__tests__/urlUtils.test.ts` (per the architecture review, this supersedes the spec's literal colocated-path suggestion — the repo convention places all `frontend/src/utils/*.ts` tests under the sibling `__tests__/` directory).
2. Import the functions under test: `import { createFilteredUrl, isTileClickable } from '../urlUtils';`. Do not import or reference `getTileTooltip` — it is out of scope.
3. Add a `describe('createFilteredUrl', ...)` block with one `it(...)` per case:
   - `{ enabled: false }` → result contains `enabled=false`.
   - `{ page: 0 }` → result contains `page=0`.
   - `{ a: null }` → `a` is excluded from the result.
   - `{ a: undefined }` → `a` is excluded from the result.
   - `{ a: '' }` → `a` is excluded from the result.
   - All filter values excluded (or an empty filters object) → function returns `baseUrl` unchanged with no trailing `?`.
   - At least one valid filter present → result matches the shape `${baseUrl}?${queryString}`.
   - Mixed object combining includable values (`false`, `0`, a non-empty string) and excludable values (`null`, `undefined`, `''`) in one call → assert only the includable key=value pairs appear (`toContain`) and the excludable keys do not (`not.toContain`), avoiding brittle full-string equality across multiple keys.
4. Add a `describe('isTileClickable', ...)` block with one `it(...)` per case:
   - `{ drillDown: { enabled: true, filters: {} } }` → `toBe(true)` (name the test to note it documents current behavior: an empty-but-present `filters` object is truthy).
   - `{ drillDown: { enabled: false, filters: { x: 1 } } }` → `toBe(false)`.
   - `{ drillDown: { enabled: true } }` (no `filters`) → `toBe(false)`.
   - `{}` (no `drillDown`) → `toBe(false)`.
   - `{ drillDown: { enabled: true, filters: { x: 1 } } }` → `toBe(true)` (baseline positive case).
5. Follow the structural/style conventions of the existing sibling test files in `frontend/src/utils/__tests__/` (e.g. `dateUtils.test.ts`): plain `describe`/`it`/`expect`, no RTL, no MSW, no mocks, no shared fixture modules — inline object literals only.
6. Run `npm test -- --coverage --watchAll=false` (or the project's standard non-watch test invocation) scoped to `urlUtils` to confirm all new tests pass and file coverage for `urlUtils.ts` reaches at least 60%. If coverage remains below 60% after implementing every acceptance criterion above, do not add tests beyond this task's scope to compensate — flag the shortfall instead.

## Acceptance Criteria
- `frontend/src/utils/__tests__/urlUtils.test.ts` exists and contains two `describe` blocks: `createFilteredUrl` and `isTileClickable`.
- `frontend/src/utils/urlUtils.ts` is unmodified (byte-for-byte identical to before this task).
- All 8 `createFilteredUrl` cases and all 5 `isTileClickable` cases listed in Steps 3 and 4 are present as individual `it(...)` tests and pass.
- `npm test` (CRA/Jest runner) passes with no failing tests, including the new file.
- `npm run build` and `npm run lint` succeed with no new errors introduced.
- Coverage for `frontend/src/utils/urlUtils.ts` is at or above 60% after the new tests are added (verified via `npm test -- --coverage`).
