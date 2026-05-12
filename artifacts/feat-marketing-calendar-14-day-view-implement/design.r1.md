# Design: Marketing Calendar 14-Day View

## UX/UI Design

### Toolbar — 3-button segmented control

The existing 2-button row (`Kalendář | Seznam`) is replaced with a 3-button segmented control. The visual language (indigo active state, neutral hover, shared border, rounded container) is unchanged.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Marketingový kalendář    [■ 5 týdnů][□ 14 dní][□ Seznam]   [+ Nová akce] … │
└──────────────────────────────────────────────────────────────────────────────┘
  ■ = active  (bg-indigo-600 text-white)
  □ = inactive (text-gray-600 hover:bg-gray-50)
```

Button order: `5 týdnů` (Calendar icon) · `14 dní` (Calendar icon) · `Seznam` (List icon).  
Default active on page load: `5 týdnů`.

Both calendar buttons use the already-imported `Calendar` icon from `lucide-react`; the list button uses `List`. The `Download` / `Nová akce` buttons are unchanged and remain after the toggle group.

### 5-week view (unchanged)

```
Po  Út  St  Čt  Pá  So  Ne
────────────────────────────
 …   …   …   …   …   …   …   ← week –1
 …   …  [T]  …   …   …   …   ← today's week  (T = indigo circle)
 …   …   …   …   …   …   …
 …   …   …   …   …   …   …
 …   …   …   …   …   …   …   ← week +3
                  +2 more ↑   ← "+N more" truncation still present
```

### 14-day view (new)

```
Po  Út  St  Čt  Pá  So  Ne
────────────────────────────────────────
 …   …  [T]  …   …   …   …   ← today's week
 ┌─────────────────────────┐
 │ Event A                 │  all events
 │ Event B                 │  visible —
 │ Event C                 │  no "+N more"
 └─────────────────────────┘
 …   …   …   …   …   …   …   ← week +1
                               ↕ parent container scrolls when grid > viewport
```

Each cell has a minimum height of 80 px even when empty. Vertical expansion is unbounded; the existing `overflow-auto` parent container provides the single scroll context — no nested scrollbars.

### Key interactions

| Action | Behavior |
|--------|----------|
| Click `14 dní` | `viewMode` → `'twoWeeks'`; `visibleRange` reset to `null`; `MarketingMonthCalendar` remounts via `key`; 14-day fallback fetch fires; `datesSet` updates `visibleRange` with actual window |
| Click `5 týdnů` | Same flow with 35-day fallback range |
| Click `Seznam` | `viewMode` → `'list'`; calendar unmounts; list + filter panel render (unchanged) |
| Prev / Next in 14-day mode | FullCalendar API advances/retreats exactly 14 days |
| Today in 14-day mode | `gotoDate(getCalendarStartForToday())` — today lands in week 2 of the 2-row grid |
| Event click / drag / resize / range select | Identical handler chain to 5-week view; no behavioral change |

---

## Component Design

### `MarketingCalendarPage`

**Responsibility:** owns all view state, orchestrates data fetching, renders toolbar and the active view block.

**`ViewMode` union widened** (current `'calendar' | 'list'` → new):

```ts
type ViewMode = 'fiveWeeks' | 'twoWeeks' | 'list';
const [viewMode, setViewMode] = useState<ViewMode>('fiveWeeks');
```

**New handler** (replaces two inline `setViewMode` calls on the toolbar buttons):

```ts
const handleViewModeChange = (mode: ViewMode) => {
  setViewMode(mode);
  if (mode !== 'list') setVisibleRange(null);
};
```

**Fallback fetch range** (currently hardcoded to 35 days at `MarketingCalendarPage.tsx:64-72`) becomes view-aware; `viewMode` is added to the `useMemo` dependency array:

```ts
end.setDate(start.getDate() + (viewMode === 'twoWeeks' ? 14 : 35));
```

**Conditional render** (`viewMode === 'calendar'` → `viewMode !== 'list'`):

```tsx
{viewMode !== 'list' ? <CalendarBlock /> : <ListBlock />}
```

**`MarketingMonthCalendar` usage inside `CalendarBlock`:**

```tsx
<MarketingMonthCalendar
  key={viewMode}
  viewName={viewMode as CalendarViewName}
  events={calendarEvents}
  initialDate={currentDate}
  onEventClick={openEdit}
  onEventMove={handleEventMove}
  onEventResize={handleEventResize}
  onDateRangeSelect={handleDateRangeSelect}
  onDatesSet={handleDatesSet}
  calendarRef={calendarRef}
  className="h-full"
/>
```

`key={viewMode}` forces a React remount on every calendar-mode switch. This fires `datesSet` exactly once with the new window's true bounds and eliminates any stale FullCalendar internal state from the previous view.

---

### `MarketingMonthCalendar`

**Responsibility:** renders a single FullCalendar `dayGrid` instance for either the 5-week or 14-day view. No awareness of the list view.

**New exported type:**

```ts
export type CalendarViewName = 'fiveWeeks' | 'twoWeeks';
```

**Props interface — `viewName` added as required:**

```ts
interface MarketingMonthCalendarProps {
  events: CalendarEvent[];
  initialDate: Date;
  viewName: CalendarViewName;           // NEW — required, no default
  onEventClick: (id: number) => void;
  onEventMove: (id: number, dateFrom: string, dateTo: string) => void;
  onEventResize: (id: number, dateFrom: string, dateTo: string) => void;
  onDateRangeSelect: (dateFrom: string, dateTo: string) => void;
  onDatesSet: (visibleStart: Date, visibleEnd: Date, currentStart: Date) => void;
  calendarRef: React.RefObject<FullCalendar>;
  className?: string;
}
```

**Internal view registry:**

```ts
const CALENDAR_VIEWS = {
  fiveWeeks: { type: 'dayGrid', duration: { weeks: 5 }, dayMaxEvents: true  },
  twoWeeks:  { type: 'dayGrid', duration: { weeks: 2 }, dayMaxEvents: false },
} as const;
```

**Derived render values:**

```ts
const calendarHeight = viewName === 'twoWeeks' ? 'auto' : '100%';

const wrapperClass =
  `marketing-calendar${viewName === 'twoWeeks' ? ' two-weeks' : ''} h-full`
  + (className ? ` ${className}` : '');
```

**FullCalendar prop changes from current code:**

| Prop | Before | After |
|------|--------|-------|
| `initialView` | `"fiveWeeks"` (hardcoded) | `{viewName}` |
| `views` | `{ fiveWeeks: { type, duration } }` | `{CALENDAR_VIEWS}` (both views) |
| `height` | `"100%"` (hardcoded) | `{calendarHeight}` |
| `dayMaxEvents` | `{true}` (top-level) | removed — lives in `CALENDAR_VIEWS` per-view |

No handler logic changes. All callbacks (`handleEventClick`, `handleEventDrop`, `handleEventResize`, `handleSelect`, `handleDatesSet`) are identical in both views.

---

### `marketingCalendar.css`

Two rules appended at the end of the file, scoped to `.marketing-calendar.two-weeks` to leave the 5-week view untouched:

```css
.marketing-calendar.two-weeks .fc-daygrid-day-frame {
  min-height: 80px;
}

.marketing-calendar.two-weeks .fc-daygrid-day-events {
  overflow: visible;
}
```

---

### Test files (new)

**`frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx`**

Covers:
- `viewName='fiveWeeks'` → FullCalendar receives `initialView="fiveWeeks"` and the views registry carries `dayMaxEvents: true` for `fiveWeeks`.
- `viewName='twoWeeks'` → FullCalendar receives `initialView="twoWeeks"`, views registry carries `dayMaxEvents: false` for `twoWeeks`, wrapper element has `two-weeks` CSS class.
- Both views are present in the registry simultaneously.
- `height='auto'` for `twoWeeks`; `height='100%'` for `fiveWeeks`.

**`frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`**

Covers:
- Toolbar renders all three buttons with Czech labels (`5 týdnů`, `14 dní`, `Seznam`).
- Default active button is `5 týdnů` on first render.
- Clicking `14 dní` highlights it and mounts `MarketingMonthCalendar` with `viewName='twoWeeks'`.
- Clicking `Seznam` unmounts the calendar and renders the list.
- Returning from `Seznam` to `5 týdnů` remounts the calendar with `viewName='fiveWeeks'`.

---

## Data Schemas

### Backend API — no changes

The feature is entirely presentational. The existing endpoint accepts arbitrary date windows and requires no modification:

```
GET /api/MarketingCalendar/calendar?StartDate={ISO}&EndDate={ISO}
```

Response shape is unchanged — `MarketingActionDto[]` consumed by `useMarketingCalendar`.

### Frontend data flow — view-aware fetch range

The `useMemo` at `MarketingCalendarPage.tsx:64-72` gains `viewMode` as a dependency:

```ts
const { startDate, endDate } = useMemo(() => {
  if (visibleRange) {
    return { startDate: visibleRange.start, endDate: visibleRange.end };
  }
  const start = getCalendarStartForToday();
  const end = new Date(start);
  end.setDate(start.getDate() + (viewMode === 'twoWeeks' ? 14 : 35));
  return { startDate: start, endDate: end };
}, [visibleRange, viewMode]);
```

The React Query key for `useMarketingCalendar` already includes `startDate` and `endDate`; different windows produce separate cache entries with no key changes required.

### `CalendarViewName` — new exported type

```ts
// frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx
export type CalendarViewName = 'fiveWeeks' | 'twoWeeks';
```

`ViewMode` on the page (`'fiveWeeks' | 'twoWeeks' | 'list'`) is a strict superset. The `as CalendarViewName` cast in the JSX is safe because the calendar block is only rendered when `viewMode !== 'list'`.
