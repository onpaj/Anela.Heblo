# Specification: Marketing Calendar 14-Day View

## Summary
Add a 14-day `dayGrid` view to the Marketing Calendar so all events on each day are visible without being collapsed into "+N more" overflow links. The existing 5-week view and list view are retained, and users switch between them via a 3-button toolbar (`5 týdnů | 14 dní | Seznam`). No backend changes are required — the existing range-based endpoint accepts arbitrary date windows.

## Background
The current Marketing Calendar offers two display modes: a 5-week `dayGrid` view and a list view. In the 5-week view, days with multiple marketing actions collapse into "+N more" links because each cell has limited vertical space. Marketing operators need a denser, less-aggregated view that shows every event on every day at a glance for short-term planning. A 14-day view (two weeks of `dayGrid`) provides ~2.5× the vertical space per day cell and removes event-count truncation, while preserving the familiar grid layout, drag/drop/resize semantics, and event styling already established in the 5-week view.

## Functional Requirements

### FR-1: Three-way view toolbar
The Marketing Calendar page exposes a single segmented control with three buttons in this order: `5 týdnů`, `14 dní`, `Seznam`. The currently active button is visually highlighted (indigo background, white text); inactive buttons render with the neutral hover style consistent with the existing toolbar. The default view on page load is `5 týdnů`.

**Acceptance criteria:**
- All three buttons render with their Czech labels.
- Exactly one button is highlighted at any time, matching `viewMode` state.
- Switching views is purely client-side (no route change).
- The `5 týdnů` and `14 dní` buttons use the `Calendar` icon; `Seznam` uses the `List` icon (existing lucide-react icons).

### FR-2: 14-day calendar view
When `14 dní` is active, FullCalendar renders a `dayGrid` view with a `{ weeks: 2 }` duration. `dayMaxEvents` is set to `false` so all events on each day display without truncation. Day cells expand vertically to fit their events; the calendar uses `height="auto"` so the parent container's `overflow-auto` handles scrolling when content exceeds the viewport.

**Acceptance criteria:**
- The view shows exactly 14 days (2 rows × 7 columns).
- No "+N more" link appears on any day, regardless of event count.
- Day cells have a minimum height of 80px even when empty.
- When the expanded grid exceeds viewport height, the parent container scrolls vertically.

### FR-3: 5-week calendar view (preserved)
The existing 5-week view remains the default and is unchanged in behavior. Its `dayMaxEvents=true` setting still produces "+N more" links for crowded days.

**Acceptance criteria:**
- Initial render shows 5 weeks with today positioned in week 2 of the grid.
- Days with overflow events still collapse into "+N more" links.

### FR-4: View switching semantics
Switching between `5 týdnů` and `14 dní` remounts `MarketingMonthCalendar` (via `key={viewMode}`) so FullCalendar reinitializes with the new view. On every switch away from `Seznam`, `visibleRange` is reset to `null`, allowing the fallback range derived from `viewMode` (35 days for `fiveWeeks`, 14 days for `twoWeeks`) to drive the next data fetch. FullCalendar's `datesSet` callback fires after remount and updates `visibleRange` to the actual visible window.

**Acceptance criteria:**
- Switching to `14 dní` triggers a `GET /api/MarketingCalendar/calendar` with a 14-day date window.
- Switching to `5 týdnů` triggers a fetch with a 35-day window.
- Switching to `Seznam` unmounts the calendar (no FullCalendar in the DOM).
- Returning from `Seznam` to either calendar view remounts FullCalendar with the correct view.

### FR-5: Navigation in 14-day view
The existing `CalendarNavigation` component (`<`, `>`, `Today` buttons) operates on the active view via the FullCalendar API. In 14-day mode, `<` / `>` jump by 14 days; `Today` resets the window so today appears in week 2 of the grid (matching the existing `getCalendarStartForToday` logic for the 5-week view).

**Acceptance criteria:**
- Prev/Next buttons advance/retreat by exactly 14 days in 14-day mode.
- `Today` re-anchors the window so today is visible.
- Date range fetching follows navigation automatically through `datesSet`.

### FR-6: Event interactions preserved across views
All event interactions available in the 5-week view (click to open `MarketingActionModal`, drag-and-drop to reschedule, resize to change duration, range select to create) remain functional and unchanged in the 14-day view. They use the same handlers and `useUpdateMarketingAction` mutation.

**Acceptance criteria:**
- Clicking an event opens the edit modal in both calendar views.
- Drag/drop fires `onEventMove` with correct ISO date strings.
- Resize fires `onEventResize` with correct ISO date strings.
- Range select fires `onDateRangeSelect`.

## Non-Functional Requirements

### NFR-1: Performance
View switching must not introduce noticeable lag (< 300ms perceived) on a typical workstation. A single fetch fires per view switch (the existing query layer handles caching). No additional re-renders beyond those caused by the deliberate `key`-based remount.

### NFR-2: Visual consistency
The 14-day view inherits all existing event styling (colors, truncation rules, font sizes) from `marketingCalendar.css` and the existing `eventContent` renderer. New CSS is scoped to `.marketing-calendar.two-weeks` to avoid affecting the 5-week view.

### NFR-3: Type safety
TypeScript strict checks must pass with no new errors. The new `viewName` prop is typed as `'fiveWeeks' | 'twoWeeks'` (exported as `CalendarViewName`).

### NFR-4: Test coverage
Unit tests must cover:
- The `viewName` prop selecting the correct `initialView`.
- Both views being registered with their `dayMaxEvents` settings.
- Toolbar rendering all three buttons.
- Toolbar button clicks toggling the rendered view (calendar mounted/unmounted; correct view active).

### NFR-5: Localization
Czech labels (`5 týdnů`, `14 dní`, `Seznam`) are used in the toolbar. The `cs` locale already loaded in FullCalendar applies to both calendar views.

### NFR-6: Accessibility
Each toolbar button is a real `<button>` with discoverable accessible name (its visible Czech label), matching the pattern used by the existing 2-button toggle.

## Data Model
No data model changes. The feature is presentational and reuses:
- `CalendarEvent` (frontend type, mapped via existing `toFcEvent` adapter).
- The existing `MarketingActionDto` returned by the calendar endpoint.

## API / Interface Design

### Backend
No backend changes. The feature reuses:
- `GET /api/MarketingCalendar/calendar?StartDate={ISO}&EndDate={ISO}` — returns actions whose date ranges intersect the window. Already accepts arbitrary ranges.

### Component interface — `MarketingMonthCalendar`
New required prop:
```ts
viewName: 'fiveWeeks' | 'twoWeeks';
```
Both views are pre-registered in a single `views` config:
```ts
{
  fiveWeeks: { type: 'dayGrid', duration: { weeks: 5 }, dayMaxEvents: true },
  twoWeeks:  { type: 'dayGrid', duration: { weeks: 2 }, dayMaxEvents: false },
}
```
`height` is derived: `'auto'` for `twoWeeks`, `'100%'` for `fiveWeeks`.
Wrapper element receives an extra `two-weeks` CSS class when `viewName === 'twoWeeks'`.

### Component interface — `MarketingCalendarPage`
Internal state change:
```ts
type ViewMode = 'fiveWeeks' | 'twoWeeks' | 'list';
const [viewMode, setViewMode] = useState<ViewMode>('fiveWeeks');
```
New handler:
```ts
const handleViewModeChange = (mode: ViewMode) => {
  setViewMode(mode);
  if (mode !== 'list') setVisibleRange(null);
};
```
Fallback fetch range becomes view-aware:
```ts
end.setDate(start.getDate() + (viewMode === 'twoWeeks' ? 14 : 35));
```
Calendar render:
```tsx
<MarketingMonthCalendar
  key={viewMode}
  viewName={viewMode}
  {/* ...existing props */}
/>
```
Conditional change:
```tsx
{viewMode !== 'list' ? <CalendarBlock/> : <ListBlock/>}
```

### CSS additions (`marketingCalendar.css`)
```css
.marketing-calendar.two-weeks .fc-daygrid-day-frame { min-height: 80px; }
.marketing-calendar.two-weeks .fc-daygrid-day-events { overflow: visible; }
```

## Dependencies
- `@fullcalendar/react` v6
- `@fullcalendar/daygrid` v6
- `@fullcalendar/interaction` v6
- `@fullcalendar/core/locales/cs`
- `react`, `react-dom` (existing)
- `tailwindcss` (existing)
- `lucide-react` (`Calendar`, `List` icons — already imported)
- Jest + React Testing Library (existing test stack)
- Existing project hooks: `useMarketingCalendar`, `useUpdateMarketingAction`, `useAuth`
- Existing components: `CalendarNavigation`, `MarketingActionModal`, `MarketingActionGrid`
- Existing adapters: `toFcEvent`, `fromFcDates` from `fullcalendarAdapters`

## Out of Scope
- Per-view URL routing (view selection remains client state only).
- Mobile-specific layout for the 14-day view.
- Renaming `MarketingMonthCalendar` (component name remains, even though it now hosts non-month views).
- Any backend changes.
- Outlook import flow.
- Persisting the chosen `viewMode` across page loads (e.g., via `localStorage`).
- Keyboard shortcuts for view switching.

## Open Questions
None.

## Status: COMPLETE