# Marketing Calendar – 14-day view must start at the current week

## Context

The marketing calendar's "14 dní" (two-week) view currently shows the **previous** week as the first row and the current week as the second row. The user wants the **current week as the first week** of the 14-day view.

### Why this happens

`MarketingCalendarPage.tsx:33-41` defines `getCalendarStartForToday()` which returns *Monday of the current week minus 7 days*. The `-7` offset was designed for the 5-week mode, where today is intentionally placed in week 2 (one week of past context, four weeks ahead).

The same helper is reused for the 14-day mode at `MarketingCalendarPage.tsx:71-73`:

```ts
const start = getCalendarStartForToday();
const end = new Date(start);
end.setDate(start.getDate() + (viewMode === 'twoWeeks' ? 14 : 35));
```

The window length switches per mode, but the **anchor** does not — so 14-day mode inherits the "show last week first" behavior.

### Scope assumption

Only the 14-day mode changes. The 5-week mode keeps the existing "today in week 2" behavior, since it deliberately surfaces one past week of context. If the user wants the 5-week mode to change too, that is a separate decision.

## Approach

Make the start-of-window helper view-aware: for `twoWeeks`, return the Monday of the current week (no offset); for `fiveWeeks`, keep the existing `-7` offset.

Also reset `currentDate` when the user toggles between calendar modes so the freshly mounted calendar lands on the correct anchor.

## Changes

### `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`

1. **Replace `getCalendarStartForToday` (lines 32-41)** with a view-aware version. Suggested shape:

   ```ts
   function startOfWeekMonday(date: Date): Date {
     const d = new Date(date);
     const dow = d.getDay(); // 0 = Sunday
     const daysToMonday = dow === 0 ? 6 : dow - 1;
     d.setHours(0, 0, 0, 0);
     d.setDate(d.getDate() - daysToMonday);
     return d;
   }

   // 14-day view: current week is week 1.
   // 5-week view: today sits in week 2 (one past week of context).
   function getCalendarStartForToday(viewMode: 'fiveWeeks' | 'twoWeeks'): Date {
     const monday = startOfWeekMonday(new Date());
     if (viewMode === 'fiveWeeks') {
       monday.setDate(monday.getDate() - 7);
     }
     return monday;
   }
   ```

2. **Update line 47** — pass the initial view mode to the lazy initializer:
   `useState(() => getCalendarStartForToday('fiveWeeks'))`.

3. **Update line 71** inside the `startDate/endDate` `useMemo` to pass `viewMode` (narrowed; `viewMode !== 'list'` is guaranteed by the surrounding render path).

4. **Update line 141** (`goToToday`) to pass the current `viewMode` so "Today" lands on the right anchor for the active view.

5. **Update `handleViewModeChange` (lines 59-62)** — when switching between calendar modes, also reset `currentDate` so the remounted `MarketingMonthCalendar` (which is keyed by `viewMode`) gets the correct `initialDate`:

   ```ts
   const handleViewModeChange = (mode: ViewMode) => {
     setViewMode(mode);
     if (mode !== 'list') {
       setVisibleRange(null);
       setCurrentDate(getCalendarStartForToday(mode));
     }
   };
   ```

### Tests to update

- `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx` — any test that asserts the `startDate` passed to `useMarketingCalendar` for the 14-day mode must drop the `-7` offset. Add (or adjust) at least one assertion that the 14-day view's first day equals Monday-of-current-week.
- `frontend/test/e2e/marketing/calendar-view.spec.ts` — only update if it asserts visible date headers for 14-day mode; otherwise leave as-is.

## Critical files

- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` (the only production file changed)
- `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`
- (Touch only if necessary) `frontend/test/e2e/marketing/calendar-view.spec.ts`

No backend change required — the request contract takes arbitrary `startDate`/`endDate`, so the new dates are transparent to the handler.

## Verification

1. **Unit tests**: `npm test -- MarketingCalendarPage` — adjusted assertions pass.
2. **Manual / dev server**:
   - `npm run start` (or repo's standard dev command), navigate to the marketing calendar.
   - Click **14 dní** — the **first row** of the calendar must contain today's date; the second row is the following week.
   - Click **5 týdnů** — today must still appear in row 2 (regression check on the 5-week mode).
   - Toggle 5 týdnů → 14 dní → 5 týdnů → 14 dní and confirm the anchor resets correctly each time.
   - Click **Dnes** (Today) while in 14-day mode after navigating forward/back — the view returns to current-week-first.
3. **Build & lint**: `npm run build` and `npm run lint` from `frontend/`.
4. **(Optional) E2E**: `./scripts/run-playwright-tests.sh` — only required if the E2E spec asserts visible dates.