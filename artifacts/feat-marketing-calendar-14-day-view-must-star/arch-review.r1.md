# Architecture Review: Marketing Calendar – 14-day view starts at current week

## Skip Design: true

No UI components, layouts, or visual elements change. The fix is pure date arithmetic inside an existing page-level container. Calendar styling, toolbar labels, and component contracts are untouched.

## Architectural Fit Assessment

The change fits cleanly into existing patterns:

- **Vertical slice boundary respected.** All changes stay inside `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`, the page-level container of the marketing slice. No cross-slice or cross-module surface is touched.
- **Component contract preserved.** `MarketingMonthCalendar` exposes `initialDate: Date` and `viewName: 'fiveWeeks' | 'twoWeeks'` (verified at `MarketingMonthCalendar.tsx:12,79–80`) and consumes `initialDate` for FullCalendar's anchor. The fix changes how the parent computes `initialDate`; the child's contract is unchanged.
- **Remount-on-toggle pattern preserved.** The calendar is keyed by `viewMode` (line 302), so changing `currentDate` in `handleViewModeChange` guarantees the next mount receives the correct `initialDate`. This matches the existing pattern and is already exercised by `MarketingCalendarPage.test.tsx:115–126`.
- **Backend contract preserved.** `useMarketingCalendar({ startDate, endDate })` accepts arbitrary ranges. No DTO, handler, or migration is required.
- **Project rules respected.** The change is surgical, immutable (per spec NFR-4), and TypeScript types are tightened (discriminated union excludes `'list'`).

The primary integration concern is the interaction between three state sources: React's `currentDate`, FullCalendar's internal anchor (advanced via `calendarRef.current.getApi()`), and the `datesSet` callback that syncs them. The spec correctly addresses this in FR-3 (toggle resets `currentDate`) and FR-4 (`goToToday` uses the active mode).

## Proposed Architecture

### Component Overview

```
MarketingCalendarPage (state owner)
├── state: viewMode, currentDate, visibleRange
├── helpers (colocated, module-scoped)
│   ├── startOfWeekMonday(date)           ← new, pure, immutable
│   └── getCalendarStartForToday(viewMode) ← view-aware (replaces non-parameterised version)
├── handlers
│   ├── handleViewModeChange(mode)        ← resets currentDate via getCalendarStartForToday(mode)
│   ├── goToToday()                       ← calls API with getCalendarStartForToday(viewMode)
│   └── handleDatesSet(start, end, currentStart)  ← unchanged, syncs FC → state
└── renders
    └── MarketingMonthCalendar
        key={viewMode}                    ← forces remount, picks up new currentDate
        viewName={viewMode}
        initialDate={currentDate}         ← consumed once per mount
```

Data flow for the bug:
- Old: `getCalendarStartForToday()` always returns Monday − 7 days → `MarketingMonthCalendar` mounts with that anchor → 14-day view shows "previous week first."
- New: `getCalendarStartForToday('twoWeeks')` returns Monday of current week → 14-day view shows current week first. `getCalendarStartForToday('fiveWeeks')` retains the −7 offset.

### Key Design Decisions

#### Decision 1: View-aware helper vs. inlining the branch at each call site
**Options considered:**
- (A) Pass `viewMode` into a single helper (spec's choice).
- (B) Inline the branch at the three call sites (lazy init, `useMemo`, `goToToday`).
- (C) Extract two named helpers (`getFiveWeekAnchor`, `getTwoWeekAnchor`) and dispatch at call sites.

**Chosen approach:** (A). One parameterised helper.

**Rationale:** Three call sites need the same branching logic. Inlining (B) violates DRY and is the bug's root cause (the offset semantics drifted from "what fiveWeeks needs" to "what every caller uses"). Splitting into two helpers (C) is fine but offers no real readability gain over a discriminated-union parameter and adds a name to remember. The view-aware helper localises the policy ("when does today appear in row 1 vs row 2?") to one function.

#### Decision 2: Reset `currentDate` on view-mode toggle vs. relying on `key={viewMode}` alone
**Options considered:**
- (A) Reset `currentDate` in `handleViewModeChange` (spec's choice).
- (B) Rely only on the `key={viewMode}` remount, with `currentDate` derived inside the child.
- (C) Move `currentDate` ownership into the calendar component.

**Chosen approach:** (A).

**Rationale:** `currentDate` lives in the page so it can drive `periodLabel` and the fallback fetch range (`useMemo` at line 67). Without resetting on toggle, the child remounts with stale `initialDate` from a prior navigation, so FR-3 fails. (B) would require duplicating anchor logic into the child; (C) would split the source of truth and break the period-label render path. Resetting in the toggle handler is the smallest correct change.

#### Decision 3: Helper stays colocated in the page file
**Options considered:**
- (A) Keep `startOfWeekMonday` and `getCalendarStartForToday` in `MarketingCalendarPage.tsx`.
- (B) Extract to a shared `dateUtils` module under `frontend/src/utils/`.

**Chosen approach:** (A). Explicitly out of scope per the spec.

**Rationale:** Only one consumer exists. Premature extraction is YAGNI. If a second caller emerges, lift then — not now.

## Implementation Guidance

### Directory / Module Structure
No new files. All edits to:
- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` (production)
- `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx` (tests)

### Interfaces and Contracts

```ts
// Module-scoped helpers in MarketingCalendarPage.tsx
function startOfWeekMonday(date: Date): Date;
function getCalendarStartForToday(viewMode: 'fiveWeeks' | 'twoWeeks'): Date;
```

**Contract requirements:**
- `startOfWeekMonday` MUST clone its argument; the input `Date` is not mutated.
- `startOfWeekMonday` zeros the time component (00:00:00.000 local).
- `getCalendarStartForToday('twoWeeks')` returns Monday of the current ISO week.
- `getCalendarStartForToday('fiveWeeks')` returns Monday of the current ISO week minus 7 days.
- The parameter type excludes `'list'` so `viewMode` cannot be erroneously forwarded from the `ViewMode` union without narrowing.

**Narrowing at the call site in the `useMemo` (line 67–75):** since the surrounding render branch (`viewMode !== 'list'`) protects this code path conceptually, but the `useMemo` runs unconditionally, narrow explicitly:

```ts
const mode = viewMode === 'list' ? 'fiveWeeks' : viewMode;
const start = getCalendarStartForToday(mode);
```

Or guard with `if (viewMode !== 'list')` inside the memo and skip the fetch range computation when in list mode (the current behaviour already feeds those values into `useMarketingCalendar` regardless of mode — preserve that, just narrow safely).

**Unchanged contracts:**
- `MarketingMonthCalendar` props (`initialDate`, `viewName`, etc.) — no change.
- `useMarketingCalendar({ startDate, endDate })` — no change.
- `CalendarNavigation` props (`onPrevious`, `onNext`, `onToday`) — no change.

### Data Flow

**Initial mount (fiveWeeks):**
1. `useState(() => getCalendarStartForToday('fiveWeeks'))` → `currentDate = Monday − 7 days`.
2. `useMemo` computes `{ startDate, endDate }` with the same offset and a 35-day window.
3. `MarketingMonthCalendar` mounts with `initialDate=currentDate`, `viewName='fiveWeeks'`.
4. FullCalendar fires `datesSet` → `handleDatesSet` updates `currentDate` and `visibleRange` to the actual visible range.

**Toggle 5w → 14d:**
1. `handleViewModeChange('twoWeeks')`:
   - `setViewMode('twoWeeks')`
   - `setVisibleRange(null)` (forces fallback computation)
   - `setCurrentDate(getCalendarStartForToday('twoWeeks'))` ← Monday of current week.
2. `key={viewMode}` change unmounts old calendar, mounts new one with the freshly reset `currentDate` and `viewName='twoWeeks'`.
3. `useMemo` recomputes `startDate/endDate` for a 14-day window from current Monday.
4. `useMarketingCalendar` re-fetches with the new range.

**Click "Dnes" (Today) in 14-day mode after navigation:**
1. `goToToday()` calls `calendarRef.current.getApi().gotoDate(getCalendarStartForToday('twoWeeks'))`.
2. FullCalendar repositions to current Monday; `datesSet` fires, `handleDatesSet` syncs `currentDate` and `visibleRange`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `useMemo` at line 67–75 receives `viewMode === 'list'` (page renders list view but memo still computes a range used by the always-mounted `useMarketingCalendar`). | LOW | Narrow inside the memo. Either branch on `viewMode === 'list'` and return a sensible default (e.g. fall through to fiveWeeks anchor), or pass a narrowed mode. The current code already feeds the calendar query unconditionally, so the safest preserving-behaviour fix is `viewMode === 'list' ? 'fiveWeeks' : viewMode`. |
| `getCalendarStartForToday` is called multiple times per render (lazy init, memo, `goToToday`). Each call instantiates a new `Date`. | NEGLIGIBLE | Existing code already does this. No change in performance footprint. |
| FullCalendar's `gotoDate` may not land on a Monday for a `dayGrid` view if a non-Monday date is passed. | NONE | The helper always returns a Monday, so this never occurs. |
| Test snapshots or assertions depending on the −7 offset for the 14-day mode. | MEDIUM | Spec lists the exact assertion updates. Verify no other test file references the 14-day window start. |
| Time-of-day creep in `currentDate` (e.g. if any code path reads `new Date()` without zeroing the time). | LOW | `startOfWeekMonday` zeroes the time. All entry points route through it. |
| Mutation in `startOfWeekMonday`. | LOW | Explicitly required to clone; the test plan should assert non-mutation. |
| Date arithmetic across DST transitions could leave the resulting `Date` at 23:00 or 01:00 instead of 00:00. | LOW | `setHours(0, 0, 0, 0)` is called before `setDate(...)`, but `setDate` itself does not re-normalise after a DST boundary. In practice `setDate` followed by an inspection of `.getHours()` still returns 0 in V8/JSC because the day arithmetic is in local-time. Acceptable. If concerned, call `setHours(0,0,0,0)` after `setDate` as well — costs nothing. |

## Specification Amendments

The spec is implementation-ready. Two small clarifications to capture:

1. **Narrow `viewMode` inside the `useMemo` (line 67–75).** The spec says "narrowed by the surrounding render path," but the `useMemo` runs unconditionally (the page renders list view via a sibling JSX branch — the memo still executes). Either:
   - Add `const mode = viewMode === 'list' ? 'fiveWeeks' : viewMode;` before calling the helper, or
   - Early-return a stable sentinel when `viewMode === 'list'` (e.g. return the previous range if available).

   Preserving current behaviour ("list view still fetches calendar data on the fiveWeeks window") is the lowest-risk choice. Document the narrowing decision in a code comment.

2. **DST guard (optional, defence-in-depth).** Add a second `setHours(0, 0, 0, 0)` after `setDate(...)` in `startOfWeekMonday`. The cost is one line; the benefit is robustness against any future arithmetic that crosses a DST boundary.

3. **Test for non-mutation.** Add a unit test asserting that `startOfWeekMonday(input).getTime() !== input.getTime()` does not imply mutation — i.e. the input's `.getTime()` should be unchanged after the call.

## Prerequisites

None. No migrations, configuration, infrastructure, or upstream changes are needed. The change is a single-file frontend edit plus accompanying unit-test updates. Verification is:
- `npm run build` and `npm run lint` from `frontend/`.
- `npm test -- MarketingCalendarPage` for the unit suite.
- Manual smoke per spec's "Manual verification" steps 1–5.