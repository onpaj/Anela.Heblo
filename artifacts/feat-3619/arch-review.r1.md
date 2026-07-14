# Architecture Review: BackgroundTasksCard — extract & test duration/status helpers, fix multi-day duration bug

## Skip Design: true

No new or changed UI/UX. The three functions being extracted (`formatDuration`, `getTimeUntilNextRun`, `getStatusBadge`) already produce the exact same text and badge markup they produce today — `getStatusBadge`'s JSX (badge `<span>` + icon, Tailwind classes) is moved verbatim into the new module, not redesigned. The only observable behavior change is that `formatDuration` will now render correct day/hour values for spans ≥ 24h instead of a truncated value — a correctness fix to existing text, not a new visual element. Confirmed by reading `frontend/src/components/BackgroundTasksCard.tsx:101-209` in full: no new badge states, no new columns, no new components are introduced.

## Architectural Fit Assessment

This is a pure frontend refactor + bugfix, fully aligned with existing conventions:

- **Extraction pattern precedent**: `frontend/src/components/catalog/detail/charts/ChartHelpers.tsx` already establishes the exact pattern this spec asks for — pure helper functions (some returning primitives, some returning callback objects) extracted from a component-adjacent concern into a sibling `.tsx` module, imported as named exports, tested via a co-located `__tests__/` file (`ChartHelpers.test.ts`). `backgroundTasksHelpers.tsx` follows this precedent directly.
- **JSX-returning helper precedent**: `getStatusBadge` returns JSX, which is why it needs `.tsx` (not `.ts`). The project doesn't yet have a helper module that itself returns JSX (ChartHelpers returns data/callbacks only), but `frontend/src/components/ui/TagBadge.tsx` + `frontend/src/components/ui/__tests__/TagBadge.test.tsx` establishes the RTL `render()` + `getByText`/`getByTestId` pattern for asserting badge-shaped JSX, which the spec correctly points to for FR-4.
- **Test co-location convention**: `docs/architecture/filesystem.md` §"Test Organization Structure" mandates `src/components/__tests__/` for React component tests, co-located with the code under test. `frontend/src/components/__tests__/backgroundTasksHelpers.test.tsx` satisfies this directly (it's a sibling of `BackgroundTasksCard.tsx`, not nested under a subfolder, matching where the new helpers module itself lives).
- **No cross-module or contract impact**: `RefreshTaskDto` (from `frontend/src/api/generated/api-client.ts:16201-16208`) is consumed as-is, unchanged. `getStatusBadge`'s parameter type (`task: RefreshTaskDto`) and field usage (`enabled`, `lastExecution?.status`) require no new types per the spec's Data Model section — verified against the generated client, which is correct as written.
- **No backend involvement**: The bug is purely in client-side parsing of an already-correct .NET `TimeSpan` string; no serialization change, no API contract change.

Net: this task requires no deviation from existing patterns. The only judgment call is precisely how to structure the bugfix logic in `formatDuration`, addressed below.

## Proposed Architecture

### Component Overview

```
BackgroundTasksCard.tsx  (unchanged responsibilities: data fetch, grouping,
                           table rendering, force-refresh/tier-run handlers)
        │  imports (named)
        ▼
backgroundTasksHelpers.tsx  (new sibling module — pure functions only)
        │  imports
        ▼
RefreshTaskDto  (frontend/src/api/generated/api-client.ts — unchanged)

Test coverage:
frontend/src/components/__tests__/backgroundTasksHelpers.test.tsx
        │  imports
        ▼
backgroundTasksHelpers.tsx  (functions under test)
```

No new runtime dependency edges are introduced beyond the import from `BackgroundTasksCard.tsx` to the new module and `lucide-react`'s `RefreshCw`/`CheckCircle`/`XCircle` icons (already imported in the component today; `getStatusBadge` moves with them).

### Key Design Decisions

#### Decision 1: `formatDuration` bug-fix strategy — parse days directly, don't re-derive from hours

**Options considered:**
1. Keep `parts[0] = parseInt(timeSpan.split(":")[0])` and compute `days = Math.floor(hours / 24)` — this is the *existing, broken* approach. `parseInt("1.05")` truncates at the `.`, yielding `1`, so `hours` never reaches a value that makes `Math.floor(hours/24)` meaningful. This option is rejected — it's the bug.
2. Detect a `.` in `parts[0]`; if present, split `parts[0]` on `.` into `days` and `hours` explicitly; if absent, `days = 0` and `hours = parseInt(parts[0])`. Use `days` directly for the `>= 24h` branch display, not a re-derived value.

**Chosen approach:** Option 2, exactly as prescribed in spec FR-2. Detect the day component structurally (presence of `.` in the segment before the first `:`), not by magnitude (`hours >= 24`), because the source string already tells you unambiguously whether a day component is present — inferring it from a parsed `hours` value is what caused the original bug (parsing `"1.05"` as an integer silently discards the day information before the magnitude check ever runs).

**Rationale:** This makes the fix robust to the exact boundary case that exposed the bug (`hours >= 24` used as a proxy for "has a day component" fails whenever the numeric parse itself is already corrupted). Structural detection (`parts[0].includes(".")`) is a one-line change to the parsing branch and requires no change to the two.NET-originated format assumptions the function already documents in its comment.

**Implementation sketch** (illustrative — developer may phrase differently but must preserve this branching):
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
This satisfies every acceptance criterion in FR-2, including the `"2.00:00:00"` → `"2d 0h"` case (day component present but `hours === 0` still routes through the `days > 0` branch, not the `hours > 0` branch — the original code's `hours >= 24` condition is retired entirely in favor of `days > 0`).

#### Decision 2: File extension and module boundary — single `.tsx` file for all three functions

**Options considered:**
1. Split into `backgroundTasksHelpers.ts` (for `formatDuration`, `getTimeUntilNextRun`) + a separate `.tsx` file for `getStatusBadge` alone.
2. Single `backgroundTasksHelpers.tsx` file for all three, per spec FR-1.

**Chosen approach:** Option 2, matching the `ChartHelpers.tsx` precedent (which itself mixes non-JSX and JSX-adjacent exports, e.g. `generateTooltipCallback` returns a plain object, not JSX, alongside pure array-builders, all in one `.tsx` file). Splitting into two files for three closely-related, always-co-imported functions would add navigation overhead without a corresponding benefit — there's no independent reuse case for `getStatusBadge` outside this component today, and TypeScript/CRA's build has no issue with `.tsx` files that mix JSX and non-JSX exports.

**Rationale:** Minimizes file count and import surface in `BackgroundTasksCard.tsx` (`import { formatDuration, getTimeUntilNextRun, getStatusBadge } from "./backgroundTasksHelpers"`), consistent with how `ChartHelpers.tsx` is imported today.

## Implementation Guidance

### Directory / Module Structure

New files:
- `frontend/src/components/backgroundTasksHelpers.tsx` — the three extracted, exported functions.
- `frontend/src/components/__tests__/backgroundTasksHelpers.test.tsx` — unit tests for all three.

Modified file:
- `frontend/src/components/BackgroundTasksCard.tsx` — remove the inline definitions of `formatDuration` (lines 101-115), `getTimeUntilNextRun` (lines 129-152), and `getStatusBadge` (lines 154-209); add `import { formatDuration, getTimeUntilNextRun, getStatusBadge } from "./backgroundTasksHelpers";` near the existing imports. `formatDateTime` (lines 117-127) stays inline, untouched, per spec.

No other files are touched. No changes to `TaskHistoryModal`, `useBackgroundRefresh` hooks, or `RefreshTaskDto`.

### Interfaces and Contracts

```ts
// frontend/src/components/backgroundTasksHelpers.tsx
import { RefreshTaskDto } from "../api/generated/api-client";

export function formatDuration(timeSpan: string): string;

export function getTimeUntilNextRun(
  nextScheduledRun: Date | string | undefined | null
): string;

export function getStatusBadge(task: RefreshTaskDto): JSX.Element | null;
```

These signatures are unchanged from the current inline closures (they don't currently close over any component state — `formatDuration` and `getTimeUntilNextRun` are pure; `getStatusBadge` only reads its `task` argument), so the extraction is a mechanical cut-paste-import with the `formatDuration` body changed per Decision 1. No adapter/wrapper layer is needed at the call sites in `BackgroundTasksCard.tsx` (4 call sites: `formatDuration` ×3 at lines 345, 349, 359; `getTimeUntilNextRun` ×1 at line 377; `getStatusBadge` ×1 at line 339 — all continue to call the imported functions with identical arguments).

### Data Flow

Unchanged at runtime: `useBackgroundTasks()` → `tasks: RefreshTaskDto[]` → `groupedTasks` (memoized) → per-row render calls `getStatusBadge(task)`, `formatDuration(task.initialDelay!)`, `formatDuration(task.refreshInterval!)`, `formatDuration(task.lastExecution.duration)`, `getTimeUntilNextRun(task.nextScheduledRun)`. The only change is these calls now resolve to imported functions instead of closures defined in the same render scope — no memoization or re-render behavior changes since none of the original functions were wrapped in `useCallback`/`useMemo`.

For tests: `backgroundTasksHelpers.test.tsx` calls the exported functions directly with hand-constructed inputs (TimeSpan strings, `Date`/string/`null`/`undefined` values, minimal `RefreshTaskDto`-shaped fixtures) — no `useBackgroundTasks()` mocking, no `QueryClientProvider` wrapper needed, since these are pure/presentational functions, not the connected component. For `getStatusBadge`, wrap each case in `render(<>{getStatusBadge(fixture)}</>)` (or a trivial functional wrapper component) since RTL's `render` expects a JSX element/component, not a bare function call — a one-line wrapper is sufficient and needs no `QueryClientProvider`, since `getStatusBadge` reads only its `task` argument.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Extraction accidentally alters `getStatusBadge` JSX/Tailwind classes during copy, causing a visual regression | Low | Copy-paste verbatim (no rewrite); FR-4 acceptance criteria assert on visible text per status, catching gross regressions; recommend a quick visual diff (or `npm run build` + manual check of the admin page) before merge since class-level regressions aren't asserted by text-based RTL tests |
| `getTimeUntilNextRun` tests become flaky if not using fixed/mocked time | Medium | Spec FR-3 already mandates `jest.useFakeTimers().setSystemTime(...)`; enforce in test file — do not construct boundary-adjacent inputs (e.g. "59 vs 60 minutes") relative to real `Date.now()` |
| `formatDuration` fix changes output for an input shape not covered by the spec's acceptance criteria (e.g. malformed/empty string) | Low | Out of scope per spec — the function has no defensive parsing today (`parseInt` on malformed input already returns `NaN` upstream) and this task doesn't add input validation; do not add new error handling beyond what's specified, to keep the change surgical |
| Barrel/import path mismatch (`./backgroundTasksHelpers` vs `./backgroundTasksHelpers.tsx`) breaks the build | Low | CRA/webpack resolves `.tsx` extension automatically from `./backgroundTasksHelpers`; matches how `ChartHelpers` is already imported without extension in its consumers — verify with `npm run build` before completion, per repo-wide validation requirement |

## Specification Amendments

None required. The spec (`spec.r1.md`) is implementation-ready as written: FR-1 through FR-4 are unambiguous, the acceptance criteria are concrete and testable, and the file/module layout it prescribes matches verified precedent in the codebase (`ChartHelpers.tsx`, `TagBadge.test.tsx`). The only addition this review makes is the concrete `formatDuration` implementation sketch in Decision 1, to remove any residual ambiguity about "compute total hours as `days * 24 + hours`" vs. "output `days` directly" — the spec already states the correct intent (output `days` directly, don't re-derive), this review just makes the resulting branch structure explicit since the wording in FR-2 paragraph 2 is easy to misread as requiring a `days*24+hours` intermediate value.

## Prerequisites

None. No migrations, no config changes, no new dependencies (`@testing-library/react` and `react-scripts test`/Jest are already in use, per `TagBadge.test.tsx` and `ChartHelpers.test.ts`). Implementation can start immediately.
