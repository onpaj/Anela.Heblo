# Specification: Marketing Calendar – 14-day view starts at current week

## Summary
The marketing calendar's "14 dní" (two-week) view currently anchors on the previous Monday, placing today in the second week. This specification changes the 14-day view so the current week is always the first row, while preserving the 5-week view's existing "today in week 2" behavior.

## Background
`MarketingCalendarPage.tsx` shares a single anchor helper (`getCalendarStartForToday`) between the 5-week and 14-day calendar modes. The helper subtracts 7 days from Monday-of-current-week, a deliberate offset for the 5-week mode (which surfaces one past week of context). The 14-day mode reuses the same helper, inheriting an unintended "previous week first" layout.

Users expect a short two-week window to begin at the current week, not the past one. The fix is local to the page-level container; the backend already accepts arbitrary `startDate`/`endDate` ranges.

## Functional Requirements

### FR-1: 14-day view anchors on current week
When the user activates the "14 dní" view, the first row of the calendar must display the Monday-to-Sunday week containing today's date. The second row must display the following week.

**Acceptance criteria:**
- On any weekday (Mon–Sun), the 14-day view's `startDate` equals Monday of the current ISO week (time component zeroed).
- The 14-day view's `endDate` equals `startDate + 14 days`.
- Today's date is visually contained in the first row of the calendar.

### FR-2: 5-week view behavior unchanged
The existing 5-week view continues to anchor one week before the current week, so today appears in row 2.

**Acceptance criteria:**
- For the 5-week mode, `startDate` equals Monday-of-current-week minus 7 days.
- `endDate` equals `startDate + 35 days`.
- Today's date is visually contained in row 2.

### FR-3: View toggle resets anchor
When the user switches between calendar modes (5-week ↔ 14-day), the `currentDate` state must be reset to the correct anchor for the newly selected mode, so the remounted `MarketingMonthCalendar` (keyed by `viewMode`) receives the correct `initialDate`.

**Acceptance criteria:**
- Toggling from 5-week to 14-day shows current week as row 1.
- Toggling from 14-day to 5-week shows today in row 2.
- Repeated toggling (5w → 14d → 5w → 14d) consistently produces the correct anchor each time.
- Switching to the list view does not reset `currentDate` (existing behavior preserved).

### FR-4: "Today" button respects active view
The "Dnes" (Today) button must return the calendar to the correct anchor for the currently active view mode.

**Acceptance criteria:**
- After navigating forward/back in 14-day mode, clicking "Dnes" returns to current-week-first.
- After navigating forward/back in 5-week mode, clicking "Dnes" returns to today-in-week-2.

### FR-5: Monday-based week computation
Week-start computation must treat Monday as the first day of the week (Sunday = day 0 maps to 6 days back).

**Acceptance criteria:**
- For any date in a given ISO week, `startOfWeekMonday(date)` returns that week's Monday with time zeroed (00:00:00.000 local).
- The function does not mutate the input `Date` object.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The helper runs once per render cycle of `MarketingCalendarPage` and on view-mode toggles. The window length and request payload size are unchanged.

### NFR-2: Backwards compatibility
The backend `useMarketingCalendar` request contract is unchanged. `startDate` and `endDate` continue to be sent as arbitrary date values; the server is agnostic to the anchor logic.

### NFR-3: Security
No security surface affected. No new data is exposed; the date range remains user-driven and scoped to the existing marketing calendar query.

### NFR-4: Code quality
- Immutable date operations: `startOfWeekMonday` must clone the input before mutating.
- The view-aware helper signature uses a discriminated string union (`'fiveWeeks' | 'twoWeeks'`) so callers cannot pass `'list'`.
- No unrelated edits to surrounding code.

## Data Model
No data model changes. The change is purely client-side date arithmetic on the `currentDate` / `startDate` / `endDate` state inside `MarketingCalendarPage`.

## API / Interface Design

### Component-level (frontend)

**File:** `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`

1. **Add helper** `startOfWeekMonday(date: Date): Date` — returns a new `Date` set to Monday 00:00:00 of the given date's week. Does not mutate the argument.

2. **Replace** `getCalendarStartForToday()` (currently lines 32–41) with a view-aware variant:
   ```ts
   function getCalendarStartForToday(viewMode: 'fiveWeeks' | 'twoWeeks'): Date {
     const monday = startOfWeekMonday(new Date());
     if (viewMode === 'fiveWeeks') {
       monday.setDate(monday.getDate() - 7);
     }
     return monday;
   }
   ```

3. **Lazy initializer** for `currentDate` state (line 47) passes the initial view mode:
   `useState(() => getCalendarStartForToday('fiveWeeks'))`.

4. **`startDate/endDate` `useMemo`** (around line 71) passes `viewMode` (narrowed — `viewMode !== 'list'` is guaranteed by the surrounding render path).

5. **`goToToday`** (line 141) passes the current `viewMode` to `getCalendarStartForToday`.

6. **`handleViewModeChange`** (lines 59–62) resets `currentDate` to the new mode's anchor when switching between calendar modes:
   ```ts
   const handleViewModeChange = (mode: ViewMode) => {
     setViewMode(mode);
     if (mode !== 'list') {
       setVisibleRange(null);
       setCurrentDate(getCalendarStartForToday(mode));
     }
   };
   ```

### Backend
No change. The marketing calendar API accepts arbitrary `startDate`/`endDate`.

## Dependencies
- Existing `MarketingCalendarPage.tsx` component and its colocated unit tests.
- `MarketingMonthCalendar` child component (consumer of `initialDate`, keyed by `viewMode`).
- `useMarketingCalendar` data hook (unchanged contract).

## Out of Scope
- Changes to the 5-week view's anchor behavior.
- Changes to the "list" view mode.
- Changes to backend endpoints, DTOs, or query handlers.
- Localization or i18n changes (Czech labels "14 dní", "5 týdnů", "Dnes" are untouched).
- Visual styling changes to the calendar.
- Refactoring or extracting the date utilities to a shared module (helper stays colocated in the page file to keep the change surgical).

## Test Plan

### Unit tests (must update)
**File:** `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`

- Drop the `-7` offset from any assertion that checks the `startDate` passed to `useMarketingCalendar` in 14-day mode.
- Add: 14-day mode's `startDate` equals Monday of the current week (with time zeroed).
- Add: 14-day mode's `endDate` equals `startDate + 14 days`.
- Add: 5-week mode's `startDate` equals Monday-of-current-week minus 7 days (regression).
- Add: Toggling 5-week → 14-day updates `currentDate` to the 14-day anchor.
- Add: Toggling 14-day → 5-week updates `currentDate` to the 5-week anchor.
- Add: Clicking "Dnes" in 14-day mode (after navigation) returns to current-week-first.

### E2E tests
**File:** `frontend/test/e2e/marketing/calendar-view.spec.ts`

- Update only if the spec asserts visible date headers for the 14-day mode. Otherwise leave untouched.

### Manual verification
1. Start dev server, navigate to the marketing calendar.
2. Click **14 dní** — first row contains today's date; second row is the following week.
3. Click **5 týdnů** — today appears in row 2 (regression check).
4. Toggle 5 týdnů → 14 dní → 5 týdnů → 14 dní; each transition lands on the correct anchor.
5. In 14-day mode, navigate forward/back, then click **Dnes** — view returns to current-week-first.
6. Run `npm run build` and `npm run lint` in `frontend/`.

## Open Questions
None.

## Status: COMPLETE