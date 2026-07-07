# Specification: Unit test coverage for `urlUtils.ts` (`createFilteredUrl` and `isTileClickable`)

## Summary
`frontend/src/utils/urlUtils.ts` sits at 50% line coverage, below the 60% CI threshold. Two exported pure functions — `createFilteredUrl` and `isTileClickable` — have untested edge cases that a future refactor could silently break. This task adds targeted unit tests for both functions to close the gap and lock in their current, correct behavior.

## Background
`createFilteredUrl` builds query strings for API calls throughout the app from a filter object. Its falsy-value check (`value !== null && value !== undefined && value !== ''`) deliberately keeps `false` and `0` in the output, unlike a naive truthiness check. This distinction is not currently covered by any test, so a well-intentioned simplification (e.g. to `if (value)`) would pass existing tests while silently dropping legitimate filter values such as `enabled=false` or `page=0` from real API requests — a regression that would not throw or fail loudly, only produce wrong query results.

Similarly, `isTileClickable` requires both `drillDown.enabled === true` and a truthy `drillDown.filters`. Because `{}` is truthy in JavaScript, a tile with `enabled: true, filters: {}` currently reports as clickable even though it carries no actual filters. Whether or not that is the intended behavior, it is unverified and undocumented by tests, so any change to this logic today has no safety net.

This is a test-only, coverage-gap-closing task against existing, unmodified source code. No production logic changes are requested or in scope.

## Functional Requirements

### FR-1: Unit tests for `createFilteredUrl`
Add unit tests in the test file colocated with `urlUtils.ts` (create `frontend/src/utils/urlUtils.test.ts` if it does not already exist) covering the function's filter/inclusion behavior.

**Acceptance criteria:**
- A test asserts that a filter value of `false` is included in the resulting query string (e.g. `{ enabled: false }` → output contains `enabled=false`).
- A test asserts that a filter value of `0` is included in the resulting query string (e.g. `{ page: 0 }` → output contains `page=0`).
- A test asserts that a filter value of `null` is excluded from the query string.
- A test asserts that a filter value of `undefined` is excluded from the query string.
- A test asserts that a filter value of empty string `''` is excluded from the query string.
- A test asserts that when all filter values are excluded (or the filters object is empty), the function returns `baseUrl` unchanged, with no trailing `?`.
- A test asserts that when at least one valid filter is present, the returned URL has the form `${baseUrl}?${queryString}`.
- A test covers a mixed case: an object combining includable values (`false`, `0`, a non-empty string) and excludable values (`null`, `undefined`, `''`) in the same call, asserting only the includable ones appear in the output.

### FR-2: Unit tests for `isTileClickable`
Add unit tests covering the combinations of `enabled` and `filters` described in the brief.

**Acceptance criteria:**
- A test asserts `isTileClickable({ drillDown: { enabled: true, filters: {} } })` returns `true` (documents current behavior: an empty-but-present `filters` object is truthy).
- A test asserts `isTileClickable({ drillDown: { enabled: false, filters: { x: 1 } } })` returns `false` (non-empty filters do not override `enabled: false`).
- A test asserts `isTileClickable({ drillDown: { enabled: true } })` (no `filters` property at all) returns `false`.
- A test asserts `isTileClickable({})` (no `drillDown` property at all) returns `false`.
- A test asserts `isTileClickable({ drillDown: { enabled: true, filters: { x: 1 } } })` returns `true` (baseline positive case, for completeness alongside the edge cases).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — these are synchronous, in-memory pure-function unit tests with no I/O. No performance targets beyond keeping the suite fast (each test should run in well under a second, consistent with existing unit tests in the repo).

### NFR-2: Security
Not applicable — no authentication, authorization, or sensitive data is involved in these pure utility functions or their tests.

## Data Model
No data model changes. Tests exercise the existing `DrillDownInfo` and `TileDataWithDrillDown` interfaces already exported from `frontend/src/utils/urlUtils.ts`, and plain `Record<string, any>` filter objects for `createFilteredUrl`.

## API / Interface Design
No API or interface changes. This task adds test code only; `createFilteredUrl`, `isTileClickable`, `getTileTooltip`, `DrillDownInfo`, and `TileDataWithDrillDown` in `frontend/src/utils/urlUtils.ts` remain unmodified.

## Dependencies
- Existing frontend test tooling (Jest, per the repository's standard FE unit test setup) — no new libraries required.
- No dependency on other in-flight features or backend changes.

## Out of Scope
- Any change to the implementation of `createFilteredUrl`, `isTileClickable`, or `getTileTooltip`.
- Tests for `getTileTooltip` (not flagged in the coverage gap; may be incidentally exercised but is not a required deliverable).
- Broader coverage improvements elsewhere in `frontend/src/utils/` or other modules.
- Integration/E2E tests — this is unit-test-only work on pure functions.
- Enforcing or changing the 60% coverage threshold itself.

## Open Questions

None.

## Status: COMPLETE
