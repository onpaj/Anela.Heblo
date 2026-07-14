# Specification: BackgroundTasksCard — extract & test duration/status helpers, fix multi-day duration bug

## Summary
`BackgroundTasksCard.tsx` has three pure utility functions (`formatDuration`, `getTimeUntilNextRun`, `getStatusBadge`) with 0% test coverage and one confirmed bug: `formatDuration` mis-parses multi-day .NET TimeSpan strings (`"dd.hh:mm:ss"`), showing e.g. `"1h 30m"` instead of `"1d 5h"` for a 1-day-5-hour-30-minute span. This change extracts the three functions into a standalone `backgroundTasksHelpers.ts` module, fixes the parsing bug, and adds unit tests covering all branches.

## Background
`BackgroundTasksCard` renders the admin table of scheduled background refresh tasks. `formatDuration` is used to display each task's `initialDelay`, `refreshInterval`, and last-execution `duration` (all .NET `TimeSpan` strings serialized as either `"hh:mm:ss"` or `"dd.hh:mm:ss"`). Because the function currently does `parseInt(parts[0])` on the string split by `:`, a value like `"1.05:30:00"` yields `parts[0] === "1.05"`, and `parseInt("1.05")` truncates to `1`, so the `hours >= 24` branch never fires. Any task with a delay/interval/duration of a day or more (e.g. a weekly catalog refresh) renders a misleading short duration with no visible error. This was flagged by the weekly coverage-gap routine (CI run #28968007617) as both a coverage gap and a live bug. The fix and tests are scoped to these three pure functions only; no other behavior of the component changes.

## Functional Requirements

### FR-1: Extract `formatDuration`, `getTimeUntilNextRun`, `getStatusBadge` into `backgroundTasksHelpers.ts`
Move the three functions out of `BackgroundTasksCard.tsx` into a new sibling module `frontend/src/components/backgroundTasksHelpers.ts` (or `.tsx`, since `getStatusBadge` returns JSX — see note below), exported as named functions. `BackgroundTasksCard.tsx` imports and uses them in place of its inline definitions. `formatDateTime` is out of scope and stays inline (not flagged in the brief, no bug, purely a locale-formatting wrapper).

**Note on file extension:** `getStatusBadge` returns JSX (`<span>...</span>`), so the extracted module must use a `.tsx` extension (`backgroundTasksHelpers.tsx`) to compile under the project's TypeScript/JSX settings — matching the existing precedent of `ChartHelpers.tsx` in `frontend/src/components/catalog/detail/charts/`, which mixes helper logic and JSX-returning functions in one `.tsx` file.

**Acceptance criteria:**
- `frontend/src/components/backgroundTasksHelpers.tsx` exports `formatDuration`, `getTimeUntilNextRun`, and `getStatusBadge` as named exports with unchanged signatures (see FR-2/FR-3/FR-4).
- `BackgroundTasksCard.tsx` no longer defines these three functions inline; it imports them from `./backgroundTasksHelpers` and all four call sites (`formatDuration` x3, `getTimeUntilNextRun` x1, `getStatusBadge` x1) continue to work unmodified.
- No visual or behavioral change to the rendered component other than the `formatDuration` bug fix in FR-2.
- `npm run build` and `npm run lint` pass with no new errors.

### FR-2: Fix `formatDuration` multi-day parsing bug
`formatDuration(timeSpan: string): string` must correctly parse both TimeSpan formats:
- `"hh:mm:ss"` (e.g. `"02:15:00"`) — no day component.
- `"dd.hh:mm:ss"` (e.g. `"1.05:30:00"`) — day component present, separated from hours by `.`.

Detect the day component by checking whether `parts[0]` (the substring before the first `:`) contains a `.`. If it does, split it into `days` and `hours` on `.`; otherwise treat `parts[0]` as `hours` with `days = 0`. Total hours for the `>= 24` branch should be computed as `days * 24 + hours` (or equivalently, since days is already parsed, output `days` directly rather than re-deriving it via `Math.floor(hours / 24)`, which was the source of the original bug — the fix must not reintroduce hour-based day derivation from a value that never exceeds 23 in the `hh:mm:ss`-only path).

**Acceptance criteria:**
- `formatDuration("1.05:30:00")` returns `"1d 5h"`.
- `formatDuration("00:30:00")` returns `"30m"`.
- `formatDuration("02:15:00")` returns `"2h 15m"`.
- `formatDuration("00:00:00")` returns `"0m"`.
- `formatDuration("23:59:00")` returns `"23h 59m"` (boundary just under 1 day, no day component in input).
- `formatDuration("2.00:00:00")` returns `"2d 0h"` (exact multiple of 24h expressed with an explicit day component).
- Existing single-day call sites in `BackgroundTasksCard.tsx` (`initialDelay`, `refreshInterval`, `lastExecution.duration`) are unaffected for values under 24 hours.

### FR-3: Unit tests for `getTimeUntilNextRun`
Add tests covering all four branches of `getTimeUntilNextRun(nextScheduledRun: Date | string | undefined | null): string`, using a fixed/mocked "now" (e.g. via `jest.useFakeTimers().setSystemTime(...)` or by constructing inputs relative to `new Date()` at test-run time) so tests are deterministic and not flaky near boundaries.

**Acceptance criteria:**
- Overdue/past timestamp → returns `"Spouští se..."`.
- Future timestamp < 60 minutes away → returns `"za N min"` with correct `N`.
- Future timestamp >= 60 minutes and < 24 hours away → returns `"za Nh Nm"` with correct hour/minute split.
- Future timestamp >= 24 hours away → returns `"za Nd Nh"` with correct day/hour split.
- `undefined` and `null` inputs → return `"N/A"`.
- A string-typed date input (as opposed to a `Date` object) is handled identically to a `Date` input (component always passes `nextScheduledRun` as either).

### FR-4: Unit tests for `getStatusBadge`
Add tests covering all five status paths of `getStatusBadge(task: RefreshTaskDto)`, using React Testing Library's `render` (this function returns JSX, so it must be tested by rendering, not by inspecting a plain return value) and matching the project's existing pattern for JSX-returning helpers/components (e.g. `frontend/src/components/ui/__tests__/TagBadge.test.tsx`).

**Acceptance criteria:**
- `task.enabled === false` → renders badge with text "Vypnuto".
- `task.enabled === true`, `task.lastExecution` absent → renders badge with text "Čeká".
- `task.lastExecution.status === "Running"` → renders badge with text "Běží" (and the spinning refresh icon is present, e.g. via a test id or class check if easily assertable; text assertion is sufficient if icon assertion is impractical).
- `task.lastExecution.status === "Completed"` → renders badge with text "Úspěch".
- `task.lastExecution.status === "Failed"` → renders badge with text "Chyba".
- `task.lastExecution.status === "Cancelled"` → renders badge with text "Zrušeno".
- An unrecognized status value → component renders `null` (test asserts the container is empty rather than throwing).
- Minimal `RefreshTaskDto`-shaped test fixtures are constructed per case (only the fields `getStatusBadge` reads: `enabled`, `lastExecution.status`).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — pure synchronous functions with no perceptible performance requirement beyond existing behavior.

### NFR-2: Security
Not applicable — no new data handling, auth, or external input beyond what the component already receives from `useBackgroundTasks()`.

### NFR-3: Test coverage
The new `backgroundTasksHelpers.tsx` module should reach effectively full line/branch coverage for the three extracted functions (all branches enumerated in FR-2/FR-3/FR-4), raising `BackgroundTasksCard`'s effective coverage above the 60% filter threshold that flagged this gap. `BackgroundTasksCard.tsx` itself is not required to gain additional direct tests beyond what the extraction naturally shifts into the helper module.

## Data Model
No changes to data models. `getStatusBadge` consumes the existing `RefreshTaskDto` type (`frontend/src/api/generated/api-client`), specifically `enabled: boolean` and `lastExecution?.status: string`. No new types are introduced; if a shared literal union type for status values (`"Running" | "Completed" | "Failed" | "Cancelled"`) does not already exist in the generated client, do not invent one for this change — keep the existing string comparisons as-is.

## API / Interface Design
No API or endpoint changes. This is a pure frontend refactor plus a bugfix in client-side display logic.

New module public interface:
```ts
// frontend/src/components/backgroundTasksHelpers.tsx
export function formatDuration(timeSpan: string): string;
export function getTimeUntilNextRun(
  nextScheduledRun: Date | string | undefined | null
): string;
export function getStatusBadge(task: RefreshTaskDto): JSX.Element | null;
```

Test file location (following the project's existing `__tests__` convention, e.g. `frontend/src/components/catalog/detail/charts/__tests__/ChartHelpers.test.ts`):
```
frontend/src/components/__tests__/backgroundTasksHelpers.test.tsx
```
(`.tsx` because `getStatusBadge` tests use React Testing Library's `render`.)

## Dependencies
- No new third-party dependencies. Uses the project's existing test stack: `react-scripts test` (Jest) and `@testing-library/react` (already used elsewhere, e.g. `TagBadge.test.tsx`).
- Depends on the existing `RefreshTaskDto` generated type from `frontend/src/api/generated/api-client`.

## Out of Scope
- `formatDateTime` is not extracted or modified.
- No changes to `BackgroundTasksCard`'s data fetching, sorting/grouping logic, tier-run/force-refresh handlers, or `TaskHistoryModal`.
- No backend/API changes — the TimeSpan serialization format itself is not touched, only client-side parsing.
- No visual/styling changes to the status badges beyond what's needed to keep existing output identical.
- No broader coverage push for the rest of `BackgroundTasksCard.tsx` (e.g. integration tests for the full table render, loading/error states) — this task targets only the three pure functions named in the brief.

## Open Questions

None.

## Status: COMPLETE
