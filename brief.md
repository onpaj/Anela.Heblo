# Marketing Calendar 14-Day View Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a 14-day dayGrid view alongside the existing 5-week view so all events are always visible without collapsing into "+N more" links.

**Architecture:** Extend `MarketingMonthCalendar` with a `viewName` prop that selects between two pre-registered FullCalendar views (`fiveWeeks` / `twoWeeks`). Update `MarketingCalendarPage` to expose a 3-button toolbar (`5 týdnů | 14 dní | Seznam`) and drive the calendar view through `viewName`. No backend changes needed — the existing `GET /api/MarketingCalendar/calendar?StartDate&EndDate` accepts arbitrary date ranges.

**Tech Stack:** React 18, TypeScript, FullCalendar v6 (`@fullcalendar/react`, `@fullcalendar/daygrid`, `@fullcalendar/interaction`), Tailwind CSS, Jest + React Testing Library.

---

## File Map

| Action | Path |
|---|---|
| Modify | `frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx` |
| Modify | `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` |
| Modify | `frontend/src/components/marketing/calendar/marketingCalendar.css` |
| Create | `frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx` |
| Create | `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx` |

---

### Task 1: Add `viewName` prop to MarketingMonthCalendar

**Files:**
- Modify: `frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx`
- Create: `frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx`

---

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx`:

```tsx
import React from 'react';
import { render } from '@testing-library/react';
import MarketingMonthCalendar from '../MarketingMonthCalendar';

// FullCalendar has browser-only APIs — mock the whole module.
// Expose props via data attributes so tests can assert them.
jest.mock('@fullcalendar/react', () => {
  const MockFullCalendar = React.forwardRef((props: any, _ref: any) => (
    <div
      data-testid="fullcalendar"
      data-initial-view={props.initialView}
      data-views={JSON.stringify(props.views)}
    />
  ));
  MockFullCalendar.displayName = 'MockFullCalendar';
  return MockFullCalendar;
});
jest.mock('@fullcalendar/daygrid', () => ({}));
jest.mock('@fullcalendar/interaction', () => ({}));
jest.mock('@fullcalendar/core/locales/cs', () => ({}));

const defaultProps = {
  events: [],
  initialDate: new Date('2026-05-05'),
  onEventClick: jest.fn(),
  onEventMove: jest.fn(),
  onEventResize: jest.fn(),
  onDateRangeSelect: jest.fn(),
  onDatesSet: jest.fn(),
  calendarRef: React.createRef<any>(),
};

describe('MarketingMonthCalendar — viewName prop', () => {
  it('passes fiveWeeks as initialView when viewName is fiveWeeks', () => {
    const { getByTestId } = render(
      <MarketingMonthCalendar {...defaultProps} viewName="fiveWeeks" />,
    );
    expect(getByTestId('fullcalendar')).toHaveAttribute('data-initial-view', 'fiveWeeks');
  });

  it('passes twoWeeks as initialView when viewName is twoWeeks', () => {
    const { getByTestId } = render(
      <MarketingMonthCalendar {...defaultProps} viewName="twoWeeks" />,
    );
    expect(getByTestId('fullcalendar')).toHaveAttribute('data-initial-view', 'twoWeeks');
  });

  it('registers both fiveWeeks and twoWeeks views', () => {
    const { getByTestId } = render(
      <MarketingMonthCalendar {...defaultProps} viewName="fiveWeeks" />,
    );
    const views = JSON.parse(getByTestId('fullcalendar').getAttribute('data-views') ?? '{}');
    expect(views.fiveWeeks).toBeDefined();
    expect(views.twoWeeks).toBeDefined();
  });

  it('sets dayMaxEvents to true for fiveWeeks', () => {
    const { getByTestId } = render(
      <MarketingMonthCalendar {...defaultProps} viewName="fiveWeeks" />,
    );
    const views = JSON.parse(getByTestId('fullcalendar').getAttribute('data-views') ?? '{}');
    expect(views.fiveWeeks.dayMaxEvents).toBe(true);
  });

  it('sets dayMaxEvents to false for twoWeeks', () => {
    const { getByTestId } = render(
      <MarketingMonthCalendar {...defaultProps} viewName="twoWeeks" />,
    );
    const views = JSON.parse(getByTestId('fullcalendar').getAttribute('data-views') ?? '{}');
    expect(views.twoWeeks.dayMaxEvents).toBe(false);
  });
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd frontend && npm test -- MarketingMonthCalendar.test --watchAll=false
```

Expected: FAIL — `viewName` prop does not exist yet, TS compilation error.

- [ ] **Step 3: Implement the viewName prop**

Replace the full content of `frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx`:

```tsx
import React, { useMemo } from 'react';
import FullCalendar from '@fullcalendar/react';
import dayGridPlugin from '@fullcalendar/daygrid';
import interactionPlugin from '@fullcalendar/interaction';
import csLocale from '@fullcalendar/core/locales/cs';
import type { EventClickArg, EventDropArg, DatesSetArg } from '@fullcalendar/core';
import type { EventResizeDoneArg } from '@fullcalendar/interaction';
import type { CalendarEvent } from './fullcalendarAdapters';
import { toFcEvent, fromFcDates } from './fullcalendarAdapters';
import './marketingCalendar.css';

export type CalendarViewName = 'fiveWeeks' | 'twoWeeks';

interface MarketingMonthCalendarProps {
  events: CalendarEvent[];
  initialDate: Date;
  viewName: CalendarViewName;
  onEventClick: (id: number) => void;
  onEventMove: (id: number, dateFrom: string, dateTo: string) => void;
  onEventResize: (id: number, dateFrom: string, dateTo: string) => void;
  onDateRangeSelect: (dateFrom: string, dateTo: string) => void;
  onDatesSet: (visibleStart: Date, visibleEnd: Date, currentStart: Date) => void;
  calendarRef: React.RefObject<FullCalendar>;
  className?: string;
}

const CALENDAR_VIEWS = {
  fiveWeeks: {
    type: 'dayGrid',
    duration: { weeks: 5 },
    dayMaxEvents: true,
  },
  twoWeeks: {
    type: 'dayGrid',
    duration: { weeks: 2 },
    dayMaxEvents: false,
  },
} as const;

const MarketingMonthCalendar: React.FC<MarketingMonthCalendarProps> = ({
  events,
  initialDate,
  viewName,
  onEventClick,
  onEventMove,
  onEventResize,
  onDateRangeSelect,
  onDatesSet,
  calendarRef,
  className,
}) => {
  const fcEvents = useMemo(() => events.map(toFcEvent), [events]);

  const handleEventClick = (info: EventClickArg) => {
    onEventClick(Number(info.event.id));
  };

  const handleEventDrop = (info: EventDropArg) => {
    const { dateFrom, dateTo } = fromFcDates(info.event.start!, info.event.end);
    onEventMove(Number(info.event.id), dateFrom, dateTo);
  };

  const handleEventResize = (info: EventResizeDoneArg) => {
    const { dateFrom, dateTo } = fromFcDates(info.event.start!, info.event.end);
    onEventResize(Number(info.event.id), dateFrom, dateTo);
  };

  const handleSelect = (info: { start: Date; end: Date; jsEvent: MouseEvent | null }) => {
    const { dateFrom, dateTo } = fromFcDates(info.start, info.end);
    onDateRangeSelect(dateFrom, dateTo);
    calendarRef.current?.getApi().unselect();
  };

  const handleDatesSet = (info: DatesSetArg) => {
    onDatesSet(info.start, info.end, info.view.currentStart);
  };

  // twoWeeks uses auto height so cells can expand to show all events;
  // the parent container's overflow-auto handles scrolling.
  const calendarHeight = viewName === 'twoWeeks' ? 'auto' : '100%';

  return (
    <div className={`marketing-calendar h-full${className ? ` ${className}` : ''}`}>
      <FullCalendar
        ref={calendarRef}
        plugins={[dayGridPlugin, interactionPlugin]}
        initialView={viewName}
        views={CALENDAR_VIEWS}
        locale={csLocale}
        initialDate={initialDate}
        headerToolbar={false}
        events={fcEvents}
        editable={true}
        selectable={true}
        selectMirror={true}
        height={calendarHeight}
        eventClick={handleEventClick}
        eventDrop={handleEventDrop}
        eventResize={handleEventResize}
        select={handleSelect}
        datesSet={handleDatesSet}
        eventContent={(arg) => (
          <div className="px-1 text-xs font-medium truncate leading-5">
            {arg.event.title}
          </div>
        )}
      />
    </div>
  );
};

export default MarketingMonthCalendar;
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd frontend && npm test -- MarketingMonthCalendar.test --watchAll=false
```

Expected: All 5 tests PASS.

- [ ] **Step 5: Verify TypeScript compiles**

```bash
cd frontend && npx tsc --noEmit
```

Expected: No errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx \
        frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx
git commit -m "feat(calendar): add viewName prop to MarketingMonthCalendar"
```

---

### Task 2: Update ViewMode and toolbar in MarketingCalendarPage

**Files:**
- Modify: `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`
- Create: `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`

**Key insight:** Switching the FullCalendar view after mount requires either calling `calendarRef.current?.getApi().changeView(name)` or remounting the calendar with a `key` prop. We use `key={viewMode}` on `MarketingMonthCalendar` — the simplest approach. FullCalendar remounts and fires `datesSet` with the new view's range, which updates `visibleRange` automatically.

---

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import MarketingCalendarPage from '../MarketingCalendarPage';

// FullCalendar mocked so tests run without browser APIs
jest.mock('@fullcalendar/react', () => {
  const Mock = React.forwardRef((props: any, _ref: any) => (
    <div data-testid="fullcalendar" data-initial-view={props.initialView} />
  ));
  Mock.displayName = 'MockFullCalendar';
  return Mock;
});
jest.mock('@fullcalendar/daygrid', () => ({}));
jest.mock('@fullcalendar/interaction', () => ({}));
jest.mock('@fullcalendar/core/locales/cs', () => ({}));

// API hooks return empty data so the calendar renders without errors
jest.mock('../../../../api/hooks/useMarketingCalendar', () => ({
  useMarketingCalendar: () => ({ data: { actions: [] }, isLoading: false, error: null }),
  useMarketingActions: () => ({ data: { actions: [], totalPages: 1 }, isLoading: false }),
  useMarketingAction: () => ({ data: null }),
  useUpdateMarketingAction: () => ({ mutate: jest.fn() }),
}));

jest.mock('../../../manufacture/calendar/CalendarNavigation', () =>
  function MockCalendarNavigation() {
    return <div data-testid="calendar-nav" />;
  },
);

jest.mock('../../../../auth/useAuth', () => ({
  useAuth: () => ({ getUserInfo: () => ({ roles: [] }) }),
}));

jest.mock('../../../../constants/layout', () => ({
  PAGE_CONTAINER_HEIGHT: '100vh',
}));

describe('MarketingCalendarPage — view toolbar', () => {
  it('renders all three view buttons', () => {
    render(<MarketingCalendarPage />);
    expect(screen.getByRole('button', { name: /5 týdnů/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /14 dní/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /seznam/i })).toBeInTheDocument();
  });

  it('shows calendar on initial render (5 týdnů is default)', () => {
    render(<MarketingCalendarPage />);
    expect(screen.getByTestId('fullcalendar')).toBeInTheDocument();
  });

  it('hides calendar and shows list when Seznam is clicked', () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole('button', { name: /seznam/i }));
    expect(screen.queryByTestId('fullcalendar')).not.toBeInTheDocument();
    expect(screen.queryByTestId('calendar-nav')).not.toBeInTheDocument();
  });

  it('shows calendar when 14 dní is clicked', () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole('button', { name: /seznam/i }));
    fireEvent.click(screen.getByRole('button', { name: /14 dní/i }));
    expect(screen.getByTestId('fullcalendar')).toBeInTheDocument();
  });

  it('shows calendar when 5 týdnů is clicked after list', () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole('button', { name: /seznam/i }));
    fireEvent.click(screen.getByRole('button', { name: /5 týdnů/i }));
    expect(screen.getByTestId('fullcalendar')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd frontend && npm test -- MarketingCalendarPage.test --watchAll=false
```

Expected: FAIL — "14 dní" button not found (doesn't exist yet).

- [ ] **Step 3: Update MarketingCalendarPage.tsx**

Apply the following changes to `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`:

**3a — Change the ViewMode type (line 43):**
```ts
// Before:
type ViewMode = 'calendar' | 'list';

// After:
type ViewMode = 'fiveWeeks' | 'twoWeeks' | 'list';
```

**3b — Change the initial state (line 46):**
```ts
// Before:
const [viewMode, setViewMode] = useState<ViewMode>('calendar');

// After:
const [viewMode, setViewMode] = useState<ViewMode>('fiveWeeks');
```

**3c — Add a handler that also resets visibleRange on view switch (add after line 46):**

Add after the `const [visibleRange, ...]` declaration (around line 48):

```ts
const handleViewModeChange = (mode: ViewMode) => {
  setViewMode(mode);
  if (mode !== 'list') {
    setVisibleRange(null);
  }
};
```

**3d — Update the fallback endDate calculation (lines 64-72):**

```ts
// Before:
const { startDate, endDate } = useMemo(() => {
    if (visibleRange) {
      return { startDate: visibleRange.start, endDate: visibleRange.end };
    }
    const start = getCalendarStartForToday();
    const end = new Date(start);
    end.setDate(start.getDate() + 35);
    return { startDate: start, endDate: end };
  }, [visibleRange]);

// After:
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

**3e — Replace the toolbar view toggle (lines 229-252):**

```tsx
// Before (2-button toggle):
<div className="flex border border-gray-200 rounded-lg overflow-hidden">
  <button
    onClick={() => setViewMode('calendar')}
    className={`px-3 py-2 text-sm flex items-center gap-1.5 transition-colors ${
      viewMode === 'calendar'
        ? 'bg-indigo-600 text-white'
        : 'text-gray-600 hover:bg-gray-50'
    }`}
  >
    <Calendar className="h-4 w-4" />
    Kalendář
  </button>
  <button
    onClick={() => setViewMode('list')}
    className={`px-3 py-2 text-sm flex items-center gap-1.5 transition-colors ${
      viewMode === 'list'
        ? 'bg-indigo-600 text-white'
        : 'text-gray-600 hover:bg-gray-50'
    }`}
  >
    <List className="h-4 w-4" />
    Seznam
  </button>
</div>

// After (3-button toggle):
<div className="flex border border-gray-200 rounded-lg overflow-hidden">
  <button
    onClick={() => handleViewModeChange('fiveWeeks')}
    className={`px-3 py-2 text-sm flex items-center gap-1.5 transition-colors ${
      viewMode === 'fiveWeeks'
        ? 'bg-indigo-600 text-white'
        : 'text-gray-600 hover:bg-gray-50'
    }`}
  >
    <Calendar className="h-4 w-4" />
    5 týdnů
  </button>
  <button
    onClick={() => handleViewModeChange('twoWeeks')}
    className={`px-3 py-2 text-sm flex items-center gap-1.5 transition-colors ${
      viewMode === 'twoWeeks'
        ? 'bg-indigo-600 text-white'
        : 'text-gray-600 hover:bg-gray-50'
    }`}
  >
    <Calendar className="h-4 w-4" />
    14 dní
  </button>
  <button
    onClick={() => handleViewModeChange('list')}
    className={`px-3 py-2 text-sm flex items-center gap-1.5 transition-colors ${
      viewMode === 'list'
        ? 'bg-indigo-600 text-white'
        : 'text-gray-600 hover:bg-gray-50'
    }`}
  >
    <List className="h-4 w-4" />
    Seznam
  </button>
</div>
```

**3f — Update the content section conditional (line 274):**

```tsx
// Before:
{viewMode === 'calendar' ? (

// After:
{viewMode !== 'list' ? (
```

**3g — Pass `viewName` prop and add `key` to MarketingMonthCalendar (lines 294-304):**

```tsx
// Before:
<MarketingMonthCalendar
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

// After:
<MarketingMonthCalendar
  key={viewMode}
  events={calendarEvents}
  initialDate={currentDate}
  viewName={viewMode}
  onEventClick={openEdit}
  onEventMove={handleEventMove}
  onEventResize={handleEventResize}
  onDateRangeSelect={handleDateRangeSelect}
  onDatesSet={handleDatesSet}
  calendarRef={calendarRef}
  className="h-full"
/>
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd frontend && npm test -- MarketingCalendarPage.test --watchAll=false
```

Expected: All 5 tests PASS.

- [ ] **Step 5: Run the full marketing test suite**

```bash
cd frontend && npm test -- --testPathPattern="marketing" --watchAll=false
```

Expected: All tests PASS (fullcalendarAdapters, MarketingActionGrid, modals, plus the two new suites).

- [ ] **Step 6: TypeScript check**

```bash
cd frontend && npx tsc --noEmit
```

Expected: No errors.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/components/marketing/pages/MarketingCalendarPage.tsx \
        frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx
git commit -m "feat(calendar): add 14-day view with 3-way toolbar toggle"
```

---

### Task 3: CSS adjustments for 14-day view

**Files:**
- Modify: `frontend/src/components/marketing/calendar/marketingCalendar.css`

The 14-day view uses `height="auto"` so FullCalendar expands cells vertically to show all events. We need a minimum row height so days with zero events don't collapse to nothing, and a visible scrollbar when the expanded calendar exceeds the viewport.

---

- [ ] **Step 1: Add 2-week view styles**

Append to `frontend/src/components/marketing/calendar/marketingCalendar.css`:

```css
/* 14-day view: ensure day cells have a useful minimum height */
.marketing-calendar.two-weeks .fc-daygrid-day-frame {
  min-height: 80px;
}

/* 14-day view: remove the fixed-row overflow clipping that month view uses */
.marketing-calendar.two-weeks .fc-daygrid-day-events {
  overflow: visible;
}
```

- [ ] **Step 2: Add the `two-weeks` CSS class to the calendar wrapper when in twoWeeks mode**

In `frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx`, update the wrapper div:

```tsx
// Before:
<div className={`marketing-calendar h-full${className ? ` ${className}` : ''}`}>

// After:
<div
  className={`marketing-calendar${viewName === 'twoWeeks' ? ' two-weeks' : ''} h-full${className ? ` ${className}` : ''}`}
>
```

- [ ] **Step 3: Rebuild and verify no lint errors**

```bash
cd frontend && npm run build && npm run lint
```

Expected: Build succeeds, no lint warnings for the changed files.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/marketing/calendar/marketingCalendar.css \
        frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx
git commit -m "feat(calendar): add CSS min-height for 14-day view cells"
```

---

## Verification Checklist

- [ ] `npm run build` passes (TypeScript + bundler).
- [ ] `npm run lint` passes — no new warnings.
- [ ] `npm test -- --testPathPattern="marketing" --watchAll=false` — all tests green.
- [ ] **Manual smoke test** (`npm start` → http://localhost:3001/marketing-calendar):
  - [ ] Default view is **5 týdnů**; today is in week 2 of the grid.
  - [ ] Clicking **14 dní** switches to a 2-row grid (2 weeks), all events show — **no "+N more" links**.
  - [ ] Clicking **Seznam** shows the list view; FullCalendar is unmounted.
  - [ ] Clicking **5 týdnů** from list re-mounts the 5-week calendar.
  - [ ] In 14-day view: `<` / `>` nav buttons jump by 14 days. `Today` button resets the window (today in week 2).
  - [ ] Clicking an event in 14-day view opens `MarketingActionModal` (same as 5-week).
  - [ ] Drag-drop and resize in 14-day view still fires `useUpdateMarketingAction`.
  - [ ] Network tab: switching views fires a fresh `GET /api/MarketingCalendar/calendar` with a 14-day date range.

## Out of Scope

- Per-view URL routing (still pure client state).
- Mobile-specific layout for 14-day view.
- Renaming `MarketingMonthCalendar` (imprecise but not blocking; leave for cleanup).
- Backend changes.
- Outlook import flow.
---

# Expedition list print — layout tweaks

## Context

The warehouse-picking PDF (`Objednávky k expedici – {Carrier}`) is generated server-side with QuestPDF. Two cosmetic problems on the per-order pages (the "first" portion of the document):

1. **Customer / internal notes are at the top of each order block** (right under the customer line, above the items table). The picker has to read them, then look down at the items, then scroll back up to confirm — confusing flow.
2. **The per-order items table has a `Pozice` column.** The position is already shown on the summary "second page" which is the actual picking guide. On the per-order page the picker would rather see `Cena` (item price) — useful for verifying the order value at a glance.

The aggregated summary page (after the explicit `PageBreak()`) must remain unchanged: it keeps `Pozice` because that's where the picker walks the warehouse.

User-confirmed decisions:
- Use existing `ExpeditionOrderItem.UnitPrice` (already populated from Shoptet `itemPriceWithVat`). No data-layer change.
- Format: `"299 Kč"` — cs-CZ culture, whole koruna, no decimals.
- Set components (`UnitPrice == 0`, hard-coded for set sub-items): render an empty cell.

## Scope

Single-file change. No DTO change, no data source change, no migrations, no API change, no frontend change.

**File to modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs`

## Implementation

### 1. Move notes below the items table

In `ComposeOrderBlock` (currently lines 68–115):

Current order inside the order box:
1. Heading (`Objednávka {Code}`)
2. Barcode
3. Customer line (right-aligned)
4. **Notes** ← currently here (lines 93–107)
5. Items table

New order:
1. Heading
2. Barcode
3. Customer line
4. Items table
5. **Notes** ← move here, with a small `PaddingTop(2)` separator above

Notes block stays conditional on at least one of `CustomerRemark` / `EshopRemark` being non-empty. Italic, font 8 — unchanged.

### 2. Swap `Pozice` → `Cena` on the per-order items table only

In `BuildItemsTable` (lines 151–211):

- Replace the `PoziceCol` reference in `ColumnsDefinition` with a new `CenaCol = 2f` constant declared next to the other column constants (lines 19–23). Width 2f is enough for `"1 999 Kč"`.
- Header cell text: `"Pozice"` → `"Cena"` (line 170). Keep `HeaderCellCenter` style.
- Regular item cell (lines 186–187): replace `item.WarehousePosition ?? string.Empty` with `FormatPrice(item.UnitPrice)`.
- Set component cell (lines 204–205): replace `item.WarehousePosition ?? string.Empty` with `FormatPrice(item.UnitPrice)` — for set sub-items `UnitPrice == 0m`, so the helper returns empty (matches user choice).

`BuildSummaryTable` (lines 213–261) is **untouched** — the summary page keeps `Pozice`.

### 3. New `FormatPrice` helper

Add next to `FormatAmount` / `FormatVariant`:

```csharp
private static readonly CultureInfo CzechCulture = CultureInfo.GetCultureInfo("cs-CZ");

private static string FormatPrice(decimal price) =>
    price == 0m ? string.Empty : $"{price.ToString("N0", CzechCulture)} Kč";
```

`"N0"` gives whole-koruna grouping using cs-CZ separators (`1 234 Kč`). Empty string for zero handles set sub-items per the agreed UX.

`using System.Globalization;` is already implicitly available via existing `decimal.TryParse` usages elsewhere — add the directive at the top of the file if missing.

## Reuse / non-changes

- `ExpeditionOrderItem.UnitPrice` already exists and is already populated from `itemPriceWithVat` (`ShoptetApiExpeditionListSource.cs:267`). No mapping changes.
- Set sub-items already get `UnitPrice = 0m` (`ShoptetApiExpeditionListSource.cs:286`). No special branching needed in the document — the formatter handles the zero case.
- The summary page renders position from `SummaryRow.WarehousePosition` (lines 244, 255) — left alone. `SummaryRow` does not need a price field.

## Verification

1. **Build & format:** `dotnet build` and `dotnet format` from repo root must succeed.
2. **Visual smoke test of the PDF** (no E2E, no unit tests for QuestPDF rendering exist):
   - Trigger the existing print pipeline. Easiest path: run the `PrintPickingListJob` once locally, or hit the endpoint that calls `ExpeditionListService.PrintPickingListAsync` (the file-system sink at `FileSystemPrintQueueSink.cs` writes the PDF to disk for inspection).
   - Open the resulting PDF and confirm on the **per-order pages**:
     - Items table column header now reads `Cena`.
     - Regular rows show prices like `299 Kč`; set sub-rows show empty `Cena` cell.
     - `Poznámka zákazníka` / `Interní poznámka` blocks (when present) appear **below** the items table, not above.
     - Notes block is omitted when both remarks are empty.
   - Confirm on the **summary page** (after the page break, titled `Položky objednávek`):
     - Column header is still `Pozice`.
     - Position values render as before.
3. **Regression checks:** existing test suite — no test currently covers `ExpeditionProtocolDocument`, but `dotnet test` should still pass (sanity check).

## Out of scope

- Adding a price column to the summary page.
- Sorting the per-order rows (currently iteration order — unchanged).
- Catalog selling-price enrichment (would only matter if we wanted set-component prices; user agreed empty cell is correct).
- Frontend changes (the React archive page only lists previously-generated PDFs).
