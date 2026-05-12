I have enough context. The current `MarketingMonthCalendar` uses a `views` config with a `fiveWeeks` view and `dayMaxEvents={true}`. The page has a 2-button toolbar (Kalendář/Seznam) and uses `getCalendarStartForToday()` plus a 35-day fallback range. The plan is consistent with these patterns. Let me produce the architecture review.

```markdown
# Architecture Review: Marketing Calendar 14-Day View

## Architectural Fit Assessment

The feature is a presentational extension of the existing `MarketingMonthCalendar` and `MarketingCalendarPage`. It aligns cleanly with the current architecture:

- **Vertical slice integrity preserved**: All changes stay within `frontend/src/components/marketing/{calendar,pages}` — no cross-module surface area is introduced.
- **No backend impact**: The existing `GET /api/MarketingCalendar/calendar?StartDate&EndDate` already accepts arbitrary windows and is consumed by `useMarketingCalendar` (a `@tanstack/react-query` hook keyed on `[..., 'calendar', startDate, endDate]`), so a 14-day window naturally produces a separate cached query without any contract change.
- **FullCalendar idioms reused**: The current component already uses the `views` registry pattern; adding `twoWeeks` is an additive change to that registry and matches FullCalendar v6 conventions.
- **Toolbar pattern consistent**: The existing 2-button segmented control (`Kalendář | Seznam`) already uses indigo-active styling at `MarketingCalendarPage.tsx:229-252`. Extending to 3 buttons preserves the same visual language; no new design primitive is needed.

Main integration points: (1) `viewName` prop on `MarketingMonthCalendar`, (2) `ViewMode` union widening on `MarketingCalendarPage`, (3) the fallback fetch-range `useMemo` at `MarketingCalendarPage.tsx:64-72`, (4) view-scoped CSS in `marketingCalendar.css`.

## Proposed Architecture

### Component Overview

```
MarketingCalendarPage  (state: viewMode ∈ {fiveWeeks, twoWeeks, list})
├── Toolbar [3-button segmented control]  ──onClick──▶ handleViewModeChange(mode)
│                                                       └─ setVisibleRange(null) when mode ≠ list
├── if viewMode ≠ 'list':
│     ├── CalendarNavigation  (prev/next/today via calendarRef.current.getApi())
│     └── MarketingMonthCalendar
│           key={viewMode}              ◀── forces remount on view change
│           viewName={viewMode}         ◀── selects entry in `views` registry
│           ...existing handlers
│              └── FullCalendar
│                    views = { fiveWeeks, twoWeeks }
│                    initialView = viewName
│                    height = (viewName === 'twoWeeks' ? 'auto' : '100%')
│                    datesSet ─────▶ onDatesSet(start, end, currentStart)
│                                       └─ setVisibleRange({start, end}) in page
│                                       └─ drives next useMarketingCalendar fetch
└── else: MarketingActionFilters + MarketingActionGrid (unchanged)
```

### Key Design Decisions

#### Decision 1: Remount via `key={viewMode}` vs imperative `getApi().changeView()`
**Options considered:**
- (a) Pre-register both views once and call `calendarRef.current.getApi().changeView('twoWeeks')` on switch.
- (b) Force a React remount of `MarketingMonthCalendar` using `key={viewMode}` so FullCalendar reinitializes from scratch.

**Chosen approach:** (b) Remount via `key`.

**Rationale:** The page already discards `visibleRange` on every view switch. A clean remount triggers `datesSet` exactly once with the new window's true bounds, eliminating a class of subtle bugs where the imperative API leaves stale `currentStart` or visible-range state straddling the transition. The cost is one extra mount per switch — negligible at this scale and well under the 300 ms NFR. The `key` approach also localizes the view-switch protocol to JSX, so the contract stays declarative.

#### Decision 2: `height='auto'` for `twoWeeks` with parent `overflow-auto` scroll
**Options considered:**
- (a) Fixed `height='100%'` for both views with internal FullCalendar scrolling.
- (b) `height='auto'` for `twoWeeks` so cells expand naturally; rely on the existing `<div className="flex-1 overflow-auto p-6">` at `MarketingCalendarPage.tsx:273` for vertical scrolling.

**Chosen approach:** (b).

**Rationale:** The whole point of the 14-day view is unbounded vertical expansion per cell. FullCalendar's internal scroll bands fight this — they would clip event lists or introduce a nested scrollbar. The page container is already `overflow-auto`, so delegating the scroll to it is a single-source-of-truth choice and produces the expected "one scrollbar that scrolls everything" behavior.

#### Decision 3: `viewName` as a required prop (no default)
**Options considered:**
- (a) Default `viewName` to `'fiveWeeks'` so existing callers compile unchanged.
- (b) Make `viewName` required so every caller is forced to pick.

**Chosen approach:** (b) — required prop.

**Rationale:** Only one caller exists (`MarketingCalendarPage`). A required prop prevents future regressions where a caller forgets to specify and silently lands on the legacy view. Since the rename touches a single file, the migration cost is zero.

#### Decision 4: `dayMaxEvents` and `height` belong in the `views` registry, not as top-level FullCalendar props
**Options considered:**
- (a) Keep `dayMaxEvents={true}` and `height="100%"` as top-level FullCalendar props and override conditionally.
- (b) Push both into per-view config in the `views` object (`dayMaxEvents` is per-view-supported by FullCalendar; `height` is computed at render time).

**Chosen approach:** (b) for `dayMaxEvents` (per-view in `views`); compute `height` at render from `viewName`.

**Rationale:** Per-view config keeps the policy where FullCalendar evaluates it, avoiding stale top-level props that could conflict with the active view. `height` is not a per-view config option in FullCalendar, so it must remain a top-level prop derived from `viewName`.

#### Decision 5: View-scoped CSS via parent class `.marketing-calendar.two-weeks`
**Options considered:**
- (a) Global CSS targeting `.fc-daygrid-day-frame` unconditionally.
- (b) Scope new rules under `.marketing-calendar.two-weeks` so the 5-week view is unaffected.

**Chosen approach:** (b).

**Rationale:** The 5-week view explicitly relies on tight cell heights to keep "+N more" links visible. Bleeding the `min-height: 80px` rule into it would shift its layout for no reason. The scoping pattern matches the existing `marketingCalendar.css` convention where every selector is rooted at `.marketing-calendar`.

## Implementation Guidance

### Directory / Module Structure

No new modules; only edits and tests:

```
frontend/src/components/marketing/
├── calendar/
│   ├── MarketingMonthCalendar.tsx              [MODIFY: add viewName, views registry, conditional height/class]
│   ├── marketingCalendar.css                   [MODIFY: append .two-weeks rules]
│   └── __tests__/
│       └── MarketingMonthCalendar.test.tsx     [CREATE]
└── pages/
    ├── MarketingCalendarPage.tsx               [MODIFY: ViewMode union, 3-button toolbar, view-aware fallback range]
    └── __tests__/
        └── MarketingCalendarPage.test.tsx      [CREATE]
```

### Interfaces and Contracts

**Exported from `MarketingMonthCalendar.tsx`:**

```ts
export type CalendarViewName = 'fiveWeeks' | 'twoWeeks';

interface MarketingMonthCalendarProps {
  events: CalendarEvent[];
  initialDate: Date;
  viewName: CalendarViewName;        // REQUIRED — no default
  onEventClick: (id: number) => void;
  onEventMove: (id: number, dateFrom: string, dateTo: string) => void;
  onEventResize: (id: number, dateFrom: string, dateTo: string) => void;
  onDateRangeSelect: (dateFrom: string, dateTo: string) => void;
  onDatesSet: (visibleStart: Date, visibleEnd: Date, currentStart: Date) => void;
  calendarRef: React.RefObject<FullCalendar>;
  className?: string;
}
```

**Internal (`MarketingMonthCalendar.tsx`):**

```ts
const CALENDAR_VIEWS = {
  fiveWeeks: { type: 'dayGrid', duration: { weeks: 5 }, dayMaxEvents: true },
  twoWeeks:  { type: 'dayGrid', duration: { weeks: 2 }, dayMaxEvents: false },
} as const;

const calendarHeight = viewName === 'twoWeeks' ? 'auto' : '100%';
const wrapperClassName =
  `marketing-calendar${viewName === 'twoWeeks' ? ' two-weeks' : ''} h-full`
  + (className ? ` ${className}` : '');
```

**Public contract change in `MarketingCalendarPage.tsx`:**

```ts
type ViewMode = 'fiveWeeks' | 'twoWeeks' | 'list';

const handleViewModeChange = (mode: ViewMode) => {
  setViewMode(mode);
  if (mode !== 'list') setVisibleRange(null);
};
```

**Fallback fetch range becomes view-aware:**

```ts
end.setDate(start.getDate() + (viewMode === 'twoWeeks' ? 14 : 35));
```

The `useMarketingCalendar` query key already includes `startDate`/`endDate`, so different windows automatically produce distinct cache entries — no key change needed.

### Data Flow

**View switch (e.g., `5 týdnů` → `14 dní`):**

1. User clicks `14 dní` → `handleViewModeChange('twoWeeks')`.
2. `viewMode` flips → `visibleRange` resets to `null`.
3. Memo recomputes `{ startDate, endDate }` using the 14-day fallback (`start = getCalendarStartForToday()`, `end = start + 14`). This fires a temporary `useMarketingCalendar` query.
4. `MarketingMonthCalendar` remounts due to `key={viewMode}` change. FullCalendar mounts with `initialView='twoWeeks'`, `initialDate=currentDate`.
5. FullCalendar fires `datesSet` with the actual visible window (which differs from the 14-day fallback because it snaps to week boundaries from `initialDate`).
6. `onDatesSet` updates `visibleRange`, replacing the temporary query with the canonical one. React Query may execute a second fetch; this is acceptable per spec (one fetch per view switch is "effective enough" given query-layer caching).
7. The grid renders all events without overflow truncation.

**Event interaction inside 14-day view:** Identical handler chain to 5-week; reuses `toFcEvent`/`fromFcDates` adapters and `useUpdateMarketingAction`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Double fetch on view switch (fallback range, then `datesSet` range) burns network/query cache. | Low | Acceptable per spec NFR-1; React Query dedupes inflight queries on identical keys. If observed in QA, raise `staleTime` for the calendar query or guard the fallback by only computing it on first mount. |
| `getCalendarStartForToday()` returns a Monday with today in week 2 of a 5-week grid; that same anchor in a 2-week grid puts today in **week 2** as well — but that's correct only by coincidence. If FR-5 is read strictly ("today in week 2"), behavior matches. Verify with QA. | Medium | Document the anchor's semantics in a comment. If a different anchor is needed for 2-week mode (e.g., today in week 1), introduce a `getCalendarStartForToday(viewName)` helper rather than branching at call sites. |
| `key={viewMode}` remount drops any pending in-flight FullCalendar interaction (drag, select). | Low | View switch is a deliberate user action; abandoning a drag is the expected outcome. No mitigation required beyond ensuring the toolbar buttons are not reachable mid-drag (they aren't — drag captures the pointer). |
| `height='auto'` plus parent `overflow-auto` could double-scroll on browsers that implement `overflow-auto` differently for nested flex children. | Low | The existing parent `<div className="flex-1 overflow-auto p-6">` at `MarketingCalendarPage.tsx:273` already provides a single scroll context; `MarketingMonthCalendar` wrapper passes `h-full` only — verify with manual smoke test on Chrome and Firefox. |
| `MarketingMonthCalendar` is a misleading name now that it hosts a 2-week view. | Low | Out of scope per spec; mention in CODEMAP if updated. Defer rename to a follow-up to keep this change surgical. |
| New tests mock `@fullcalendar/react` at the module level, masking real prop-passing bugs (e.g., a typo in `views` config). | Medium | Mock validates `initialView` and the `views` registry shape via `data-views` JSON. This catches the contract this PR introduces. Keep `fullcalendarAdapters.test.ts` (real, unmocked) for adapter logic. Add a manual smoke-test step (already in the brief's verification checklist). |
| `dayMaxEvents` set in per-view config may be ignored by FullCalendar v6 if the top-level `dayMaxEvents` is also present and conflicts. | Medium | Remove the top-level `dayMaxEvents={true}` from `MarketingMonthCalendar.tsx:86`. Per-view config in `views` is the single source of truth. The brief's diff already implies this — make it explicit. |

## Specification Amendments

1. **Remove top-level `dayMaxEvents` prop.** The current `MarketingMonthCalendar.tsx:86` sets `dayMaxEvents={true}` at the top level. The new implementation must move this exclusively into the per-view `CALENDAR_VIEWS` config to avoid policy conflicts. The brief's "after" snippet for Task 1 already does this; call it out in the spec under FR-2/FR-3 so reviewers don't accidentally retain the top-level prop.

2. **Wrapper class composition.** Update FR-2 / Task 3 Step 2 wording to clarify that the `two-weeks` class is added **before** the `h-full` and any caller-provided `className`, so CSS specificity remains predictable. The proposed string concatenation in the brief satisfies this; the spec should make it explicit.

3. **Anchor semantics in FR-5 ("Today in week 2").** Currently `getCalendarStartForToday()` produces a Monday one week before today's week, intended for the 5-week grid. In the 2-week grid this places today in week 2 (the second of two rows). State this as the explicit anchor invariant in FR-5 to prevent a future change to the helper from quietly breaking 14-day behavior.

4. **`onDatesSet` Date semantics.** `info.start` / `info.end` from FullCalendar's `DatesSetArg` are JS `Date` objects with timezone-local midnight — the page already passes them straight into `useMarketingCalendar` which accepts `Date`. No change required, but document this in the spec under "API / Interface Design" so future contributors don't second-guess and add UTC conversion.

5. **Test mock fidelity.** The spec's NFR-4 should require asserting the **shape** of the `views` registry (i.e., that both `fiveWeeks` and `twoWeeks` carry `dayMaxEvents` set correctly), not merely that each view "is registered." The brief's tests already do this — pull the assertion into the spec language.

## Prerequisites

- **None.** No backend changes, no migrations, no new packages, no config. The required FullCalendar v6 plugins (`@fullcalendar/react`, `@fullcalendar/daygrid`, `@fullcalendar/interaction`, `@fullcalendar/core/locales/cs`) are already installed and imported. The `cs` locale is already loaded. Jest + RTL are already in the test stack as verified by `frontend/src/**/__tests__/*.test.tsx` files.
- Recommended (not blocking): Run `npm test -- fullcalendarAdapters` first to confirm the existing test baseline is green before introducing new mocks for `@fullcalendar/react`.
```