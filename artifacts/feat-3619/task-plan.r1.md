# Implementation Plan: BackgroundTasksCard — extract & test duration/status helpers, fix multi-day duration bug

## Overview

Extract three pure functions (`formatDuration`, `getTimeUntilNextRun`, `getStatusBadge`) out of `frontend/src/components/BackgroundTasksCard.tsx` into a new sibling module `frontend/src/components/backgroundTasksHelpers.tsx`, fix the multi-day `formatDuration` parsing bug in the process, and add a unit test file covering all branches of all three functions. This is a single, tightly-coupled unit of work — splitting "extract" from "add tests" would leave an intermediate state where the extracted module has known-untested branches, and the bugfix only makes sense embedded in the extraction. One task.

Full context (read before implementing): `artifacts/feat-3619/spec.r1.md` and `artifacts/feat-3619/arch-review.r1.md`. The architecture review's Decision 1 implementation sketch is authoritative for the `formatDuration` fix; follow it exactly.

---

### task: extract-backgroundtasks-helpers-and-test

**Goal:** Move `formatDuration`, `getTimeUntilNextRun`, and `getStatusBadge` out of `BackgroundTasksCard.tsx` into a new `frontend/src/components/backgroundTasksHelpers.tsx`, fix the `formatDuration` multi-day parsing bug during the move, wire `BackgroundTasksCard.tsx` to import the extracted functions, and add a full-coverage unit test file at `frontend/src/components/__tests__/backgroundTasksHelpers.test.tsx`.

#### 1. Create `frontend/src/components/backgroundTasksHelpers.tsx`

New file, named exports, no default export. Import `RefreshTaskDto` from `../api/generated/api-client` and the icon components currently used inside `getStatusBadge` (`RefreshCw`, `CheckCircle`, `XCircle`) from `lucide-react`.

Move these three functions verbatim from `frontend/src/components/BackgroundTasksCard.tsx` (current line numbers, will shift as you edit — use as a locator, not a promise of exact position after other edits in the same file):

- `formatDuration` (currently lines 101–115)
- `getTimeUntilNextRun` (currently lines 129–152)
- `getStatusBadge` (currently lines 154–209)

They are currently defined as `const fn = (...) => {...}` closures inside the component body. Convert each to a top-level `export function` declaration (per the spec's public interface) — e.g. `export function formatDuration(timeSpan: string): string { ... }`. Do not change parameter names or add new parameters. `getStatusBadge` keeps its current JSX bodies unmodified — copy-paste the five status branches (`Vypnuto` / disabled, `Čeká` / no lastExecution, `Running` → `Běží`, `Completed` → `Úspěch`, `Failed` → `Chyba`, `Cancelled` → `Zrušeno`, plus the `return null` fallback for unrecognized status) verbatim, including all Tailwind classes and the `RefreshCw`/`CheckCircle`/`XCircle` icons. Do not touch `formatDateTime` — it stays inline in `BackgroundTasksCard.tsx`, untouched.

**Fix `formatDuration`'s multi-day bug while moving it.** Replace the current broken body:

```ts
const formatDuration = (timeSpan: string): string => {
  // TimeSpan format: "hh:mm:ss" or "dd.hh:mm:ss"
  const parts = timeSpan.split(":");
  const hours = parseInt(parts[0]);
  const minutes = parseInt(parts[1]);

  if (hours >= 24) {
    const days = Math.floor(hours / 24);
    return `${days}d ${hours % 24}h`;
  } else if (hours > 0) {
    return `${hours}h ${minutes}m`;
  } else {
    return `${minutes}m`;
  }
};
```

with (exactly this branch structure, per `arch-review.r1.md` Decision 1 — detect the day component structurally by checking for `.` in the first segment, do not re-derive days from an hours value via `Math.floor(hours/24)`):

```ts
export function formatDuration(timeSpan: string): string {
  // TimeSpan format: "hh:mm:ss" or "dd.hh:mm:ss"
  const parts = timeSpan.split(":");
  const firstSegment = parts[0];
  const minutes = parseInt(parts[1]);

  let days = 0;
  let hours: number;
  if (firstSegment.includes(".")) {
    const [dayPart, hourPart] = firstSegment.split(".");
    days = parseInt(dayPart);
    hours = parseInt(hourPart);
  } else {
    hours = parseInt(firstSegment);
  }

  if (days > 0) {
    return `${days}d ${hours}h`;
  } else if (hours > 0) {
    return `${hours}h ${minutes}m`;
  } else {
    return `${minutes}m`;
  }
}
```

#### 2. Update `frontend/src/components/BackgroundTasksCard.tsx`

- Remove the inline definitions of `formatDuration`, `getTimeUntilNextRun`, and `getStatusBadge` from inside the component body (the three blocks identified above). Leave `formatDateTime` in place, untouched.
- Add an import near the top of the file (alongside the existing `import TaskHistoryModal from "./TaskHistoryModal";`):
  ```ts
  import { formatDuration, getTimeUntilNextRun, getStatusBadge } from "./backgroundTasksHelpers";
  ```
- If the `RefreshCw`, `CheckCircle`, `XCircle` imports from `lucide-react` at the top of `BackgroundTasksCard.tsx` become unused after removing `getStatusBadge` (check: `RefreshCw` is still used elsewhere in the component for loading spinners/buttons, so it stays; `CheckCircle` and `XCircle` were only used inside `getStatusBadge` — verify with a search of the file after your edit and remove any of `CheckCircle`/`XCircle` from the `lucide-react` import if no longer referenced, to avoid an unused-import lint error).
- Do not change any of the four call sites (`getStatusBadge(task)` at the status column; `formatDuration(task.initialDelay!)`, `formatDuration(task.refreshInterval!)`, `formatDuration(task.lastExecution.duration)`; `getTimeUntilNextRun(task.nextScheduledRun)`) — they call the same function names with the same arguments, now resolved via import instead of closure.
- Do not touch any other logic: data fetching, `groupedTasks` memoization, `handleForceRefresh`, `handleRunTier`, `getTierBadgeColor`, loading/error rendering, or the table JSX.

#### 3. Add `frontend/src/components/__tests__/backgroundTasksHelpers.test.tsx`

New test file. Follow the project's Jest + React Testing Library conventions used in `frontend/src/components/ui/__tests__/TagBadge.test.tsx` (RTL `render`/`screen`) and `frontend/src/components/catalog/detail/charts/__tests__/ChartHelpers.test.ts` (plain-function `describe`/`it` blocks, imported via relative path `../backgroundTasksHelpers`). Run via the project's existing test command (`react-scripts test`, e.g. `CI=true npx react-scripts test src/components/__tests__/backgroundTasksHelpers.test.tsx --watchAll=false` from `frontend/`).

Imports:
```ts
import React from "react";
import { render, screen } from "@testing-library/react";
import { formatDuration, getTimeUntilNextRun, getStatusBadge } from "../backgroundTasksHelpers";
import { RefreshTaskDto, RefreshTaskExecutionLogDto } from "../../api/generated/api-client";
```

**`describe("formatDuration", ...)`** — one `it` per case, asserting exact return string:
- `formatDuration("1.05:30:00")` → `"1d 5h"`
- `formatDuration("00:30:00")` → `"30m"`
- `formatDuration("02:15:00")` → `"2h 15m"`
- `formatDuration("00:00:00")` → `"0m"`
- `formatDuration("23:59:00")` → `"23h 59m"`
- `formatDuration("2.00:00:00")` → `"2d 0h"`

**`describe("getTimeUntilNextRun", ...)`** — use `jest.useFakeTimers()` with `.setSystemTime(...)` to pin "now" (e.g. `new Date("2026-01-01T12:00:00.000Z")`) in a `beforeEach`, and `jest.useRealTimers()` in an `afterEach`, so the test isn't tied to the current wall-clock date. Cover:
- A `Date` timestamp before the pinned "now" → `"Spouští se..."`.
- A `Date` timestamp ~30 minutes after "now" (< 60 min) → `"za 30 min"` (or equivalent computed N — assert the exact expected string given your chosen offset).
- A `Date` timestamp ~90 minutes after "now" (>= 60 min, < 24h) → `"za 1h 30m"`.
- A `Date` timestamp ~29 hours after "now" (>= 24h) → `"za 1d 5h"`.
- `undefined` → `"N/A"`.
- `null` → `"N/A"`.
- A string-typed ISO date input equivalent to one of the above `Date` cases (e.g. the 90-minutes-away case passed as `nextRun.toISOString()` instead of a `Date` object) → same result as the `Date` version, proving string inputs are handled identically.

Compute expected offsets from the fixed "now" rather than hardcoding both sides independently, e.g.:
```ts
const NOW = new Date("2026-01-01T12:00:00.000Z");
beforeEach(() => {
  jest.useFakeTimers();
  jest.setSystemTime(NOW);
});
afterEach(() => {
  jest.useRealTimers();
});
```
then build each test's input as `new Date(NOW.getTime() + offsetMs)`.

**`describe("getStatusBadge", ...)`** — construct minimal `RefreshTaskDto` fixtures using the generated client's constructors (only set the fields `getStatusBadge` reads: `enabled`, `lastExecution.status`), and render each via RTL, matching the `TagBadge.test.tsx` pattern of `render(...)` + `screen.getByText(...)`. Since `getStatusBadge` returns `JSX.Element | null` rather than being itself a component, wrap the call in a trivial render, e.g. `render(<>{getStatusBadge(task)}</>)`. Cases:
- `new RefreshTaskDto({ enabled: false })` → `screen.getByText("Vypnuto")` present.
- `new RefreshTaskDto({ enabled: true })` (no `lastExecution`) → `screen.getByText("Čeká")` present.
- `new RefreshTaskDto({ enabled: true, lastExecution: new RefreshTaskExecutionLogDto({ status: "Running" }) })` → `screen.getByText("Běží")` present.
- `new RefreshTaskDto({ enabled: true, lastExecution: new RefreshTaskExecutionLogDto({ status: "Completed" }) })` → `screen.getByText("Úspěch")` present.
- `new RefreshTaskDto({ enabled: true, lastExecution: new RefreshTaskExecutionLogDto({ status: "Failed" }) })` → `screen.getByText("Chyba")` present.
- `new RefreshTaskDto({ enabled: true, lastExecution: new RefreshTaskExecutionLogDto({ status: "Cancelled" }) })` → `screen.getByText("Zrušeno")` present.
- `new RefreshTaskDto({ enabled: true, lastExecution: new RefreshTaskExecutionLogDto({ status: "SomeUnknownStatus" }) })` → assert no badge text is rendered; use `const { container } = render(<>{getStatusBadge(task)}</>); expect(container).toBeEmptyDOMElement();` to assert the `null` return renders nothing.

#### Verification

- `cd frontend && npx react-scripts test src/components/__tests__/backgroundTasksHelpers.test.tsx --watchAll=false` (or `CI=true npm test -- --testPathPattern=backgroundTasksHelpers`) — all new tests pass.
- `cd frontend && npm run build` — succeeds with no new TypeScript errors (confirms the `.tsx` extension resolves correctly on import from `BackgroundTasksCard.tsx` and no unused-import errors from the `lucide-react` cleanup in step 2).
- `cd frontend && npm run lint` — no new lint errors (e.g. unused imports, missing return types if the lint config enforces them).
- Manual/spot check: confirm `BackgroundTasksCard.tsx` no longer contains the string `Math.floor(hours / 24)` (the removed buggy line) and that the file's four call sites for `formatDuration`, `getTimeUntilNextRun`, and `getStatusBadge` are unchanged in behavior (same arguments passed).
- No changes to any file other than `frontend/src/components/BackgroundTasksCard.tsx` (edit), `frontend/src/components/backgroundTasksHelpers.tsx` (new), and `frontend/src/components/__tests__/backgroundTasksHelpers.test.tsx` (new).
