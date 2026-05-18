# Mobile Marketing Calendar – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a purpose-built vertical agenda view to the marketing calendar that replaces the desktop grid on screens ≤767 px, with sticky date headers, full-width event cards, 14-day window navigation, and full create/edit via the existing modal.

**Architecture:** `MobileAgendaView` is self-contained — it owns its 14-day window state, fetches via `useMarketingCalendar`, maps events with `groupEventsByDay`, and opens the existing `MarketingActionModal`. `AgendaDayGroup` + `AgendaEventCard` are pure presentational components. `MarketingCalendarPage` calls `useIsMobile()` (already in `useMediaQuery.ts`) and renders `<MobileAgendaView />` in place of the desktop calendar when on mobile.

**Tech Stack:** React 18, TypeScript, React Query (`useMarketingCalendar` / `useMarketingAction`), Tailwind CSS + BEM CSS file (`mobileAgenda.css`), Jest + React Testing Library, Playwright E2E.

**Pre-existing reuse (no new files for these):**
- `useIsMobile()` — already in `frontend/src/hooks/useMediaQuery.ts`
- `formatDateStr(date: Date): string` — already in `fullcalendarAdapters.ts`
- `CalendarEvent`, `ACTION_TYPE_COLORS` — already in `fullcalendarAdapters.ts`
- `CalendarNavigation` — already in `components/manufacture/calendar/CalendarNavigation.tsx`
- `MarketingActionModal` — already in `components/marketing/detail/MarketingActionModal.tsx`
- `MarketingActionDto` — already in `components/marketing/list/MarketingActionGrid.tsx`
- `useMarketingCalendar`, `useMarketingAction` — already in `api/hooks/useMarketingCalendar.ts`

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `frontend/src/components/marketing/calendar/agendaGrouping.ts` | Create | Pure `groupEventsByDay` function + `AgendaDay` type |
| `frontend/src/components/marketing/calendar/__tests__/agendaGrouping.test.ts` | Create | Unit tests for grouping logic |
| `frontend/src/components/marketing/calendar/AgendaEventCard.tsx` | Create | Single event card (colored border, badge, meta) |
| `frontend/src/components/marketing/calendar/__tests__/AgendaEventCard.test.tsx` | Create | Unit tests for event card |
| `frontend/src/components/marketing/calendar/AgendaDayGroup.tsx` | Create | Sticky header + event cards for one day |
| `frontend/src/components/marketing/calendar/__tests__/AgendaDayGroup.test.tsx` | Create | Unit tests for day group |
| `frontend/src/components/marketing/calendar/mobileAgenda.css` | Create | All agenda-specific styles (touch targets ≥44px) |
| `frontend/src/components/marketing/calendar/MobileAgendaView.tsx` | Create | Agenda container with state, data, navigation |
| `frontend/src/components/marketing/calendar/__tests__/MobileAgendaView.test.tsx` | Create | Component tests for agenda view |
| `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` | Modify | Add `useIsMobile` + conditional render of `MobileAgendaView` |
| `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx` | Modify | Add `useIsMobile` mock + mobile rendering tests |
| `frontend/test/e2e/marketing/mobile-agenda.spec.ts` | Create | E2E test at 375px viewport |

---

## Task 1: groupEventsByDay pure function + unit tests

**Files:**
- Create: `frontend/src/components/marketing/calendar/agendaGrouping.ts`
- Create: `frontend/src/components/marketing/calendar/__tests__/agendaGrouping.test.ts`

- [ ] **Step 1.1: Write the failing test file**

Create `frontend/src/components/marketing/calendar/__tests__/agendaGrouping.test.ts`:

```typescript
import { groupEventsByDay } from '../agendaGrouping';
import type { CalendarEvent } from '../fullcalendarAdapters';

const makeEvent = (overrides: Partial<CalendarEvent> = {}): CalendarEvent => ({
  id: 1,
  title: 'Test',
  actionType: 'SocialMedia',
  dateFrom: '2026-05-18',
  dateTo: '2026-05-18',
  associatedProducts: [],
  ...overrides,
});

describe('groupEventsByDay', () => {
  it('places a single-day event on its date', () => {
    const result = groupEventsByDay(
      [makeEvent({ id: 1, dateFrom: '2026-05-18', dateTo: '2026-05-18' })],
      '2026-05-18',
      '2026-05-18',
    );
    expect(result).toHaveLength(1);
    expect(result[0].date).toBe('2026-05-18');
    expect(result[0].events).toHaveLength(1);
    expect(result[0].events[0].id).toBe(1);
  });

  it('places a multi-day event on every day it covers', () => {
    const result = groupEventsByDay(
      [makeEvent({ dateFrom: '2026-05-17', dateTo: '2026-05-19' })],
      '2026-05-17',
      '2026-05-19',
    );
    expect(result).toHaveLength(3);
    expect(result.every((d) => d.events.length === 1)).toBe(true);
  });

  it('emits empty-event days for days with no matching events', () => {
    const result = groupEventsByDay([], '2026-05-18', '2026-05-19');
    expect(result).toHaveLength(2);
    expect(result[0].events).toHaveLength(0);
    expect(result[1].events).toHaveLength(0);
  });

  it('includes an event that starts before the window but ends within it', () => {
    const result = groupEventsByDay(
      [makeEvent({ dateFrom: '2026-05-15', dateTo: '2026-05-20' })],
      '2026-05-18',
      '2026-05-20',
    );
    expect(result).toHaveLength(3);
    expect(result.every((d) => d.events.length === 1)).toBe(true);
  });

  it('includes an event that starts within the window but ends after it', () => {
    const result = groupEventsByDay(
      [makeEvent({ dateFrom: '2026-05-18', dateTo: '2026-05-25' })],
      '2026-05-18',
      '2026-05-20',
    );
    expect(result).toHaveLength(3);
    expect(result.every((d) => d.events.length === 1)).toBe(true);
  });

  it('excludes an event entirely outside the window', () => {
    const result = groupEventsByDay(
      [makeEvent({ dateFrom: '2026-05-10', dateTo: '2026-05-12' })],
      '2026-05-18',
      '2026-05-20',
    );
    expect(result).toHaveLength(3);
    expect(result.every((d) => d.events.length === 0)).toBe(true);
  });

  it('emits exactly 14 days for a two-week window', () => {
    const result = groupEventsByDay([], '2026-05-18', '2026-05-31');
    expect(result).toHaveLength(14);
    expect(result[0].date).toBe('2026-05-18');
    expect(result[13].date).toBe('2026-05-31');
  });

  it('places multiple events on the same day when they both cover it', () => {
    const result = groupEventsByDay(
      [
        makeEvent({ id: 1, dateFrom: '2026-05-18', dateTo: '2026-05-18' }),
        makeEvent({ id: 2, dateFrom: '2026-05-18', dateTo: '2026-05-20' }),
      ],
      '2026-05-18',
      '2026-05-18',
    );
    expect(result[0].events).toHaveLength(2);
  });
});
```

- [ ] **Step 1.2: Run test — expect FAIL ("Cannot find module")**

```bash
cd frontend && react-scripts test agendaGrouping.test.ts --watchAll=false 2>&1 | tail -20
```

Expected: `Cannot find module '../agendaGrouping'`

- [ ] **Step 1.3: Implement agendaGrouping.ts**

Create `frontend/src/components/marketing/calendar/agendaGrouping.ts`:

```typescript
import type { CalendarEvent } from './fullcalendarAdapters';

export interface AgendaDay {
  date: string;
  events: CalendarEvent[];
}

export function groupEventsByDay(
  events: CalendarEvent[],
  rangeStart: string,
  rangeEnd: string,
): AgendaDay[] {
  const days: AgendaDay[] = [];
  const current = new Date(`${rangeStart}T00:00:00`);
  const end = new Date(`${rangeEnd}T00:00:00`);

  while (current <= end) {
    const dateStr = toDateStr(current);
    days.push({
      date: dateStr,
      events: events.filter((e) => e.dateFrom <= dateStr && e.dateTo >= dateStr),
    });
    current.setDate(current.getDate() + 1);
  }

  return days;
}

function toDateStr(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}
```

- [ ] **Step 1.4: Run test — expect PASS**

```bash
cd frontend && react-scripts test agendaGrouping.test.ts --watchAll=false 2>&1 | tail -10
```

Expected: `Tests: 8 passed`

- [ ] **Step 1.5: Commit**

```bash
git add frontend/src/components/marketing/calendar/agendaGrouping.ts \
        frontend/src/components/marketing/calendar/__tests__/agendaGrouping.test.ts
git commit -m "feat: add groupEventsByDay pure function for mobile agenda"
```

---

## Task 2: AgendaEventCard component + unit tests

**Files:**
- Create: `frontend/src/components/marketing/calendar/AgendaEventCard.tsx`
- Create: `frontend/src/components/marketing/calendar/__tests__/AgendaEventCard.test.tsx`

- [ ] **Step 2.1: Write the failing test**

Create `frontend/src/components/marketing/calendar/__tests__/AgendaEventCard.test.tsx`:

```tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AgendaEventCard } from '../AgendaEventCard';
import type { CalendarEvent } from '../fullcalendarAdapters';

const makeEvent = (overrides: Partial<CalendarEvent> = {}): CalendarEvent => ({
  id: 1,
  title: 'Letní kampaň',
  actionType: 'SocialMedia',
  dateFrom: '2026-05-18',
  dateTo: '2026-05-18',
  associatedProducts: [],
  ...overrides,
});

describe('AgendaEventCard', () => {
  it('renders the event title', () => {
    render(<AgendaEventCard event={makeEvent({ title: 'Letní kampaň' })} onClick={jest.fn()} />);
    expect(screen.getByText('Letní kampaň')).toBeInTheDocument();
  });

  it('renders Czech action type label for SocialMedia', () => {
    render(<AgendaEventCard event={makeEvent({ actionType: 'SocialMedia' })} onClick={jest.fn()} />);
    expect(screen.getByText('Sociální sítě')).toBeInTheDocument();
  });

  it('renders Czech label for Event', () => {
    render(<AgendaEventCard event={makeEvent({ actionType: 'Event' })} onClick={jest.fn()} />);
    expect(screen.getByText('Akce')).toBeInTheDocument();
  });

  it('renders Czech label for Meeting', () => {
    render(<AgendaEventCard event={makeEvent({ actionType: 'Meeting' })} onClick={jest.fn()} />);
    expect(screen.getByText('Porada')).toBeInTheDocument();
  });

  it('calls onClick when the button is pressed', async () => {
    const onClick = jest.fn();
    render(<AgendaEventCard event={makeEvent()} onClick={onClick} />);
    await userEvent.click(screen.getByRole('button'));
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('shows product count when event has associated products', () => {
    render(
      <AgendaEventCard
        event={makeEvent({ associatedProducts: ['p1', 'p2', 'p3'] })}
        onClick={jest.fn()}
      />,
    );
    expect(screen.getByText('3 produktů')).toBeInTheDocument();
  });

  it('does not show product count when no products', () => {
    render(<AgendaEventCard event={makeEvent({ associatedProducts: [] })} onClick={jest.fn()} />);
    expect(screen.queryByText(/produktů/)).not.toBeInTheDocument();
  });

  it('shows date range for multi-day events', () => {
    render(
      <AgendaEventCard
        event={makeEvent({ dateFrom: '2026-05-18', dateTo: '2026-05-22' })}
        onClick={jest.fn()}
      />,
    );
    expect(screen.getByText('2026-05-18 – 2026-05-22')).toBeInTheDocument();
  });

  it('does not show date range for single-day events', () => {
    render(
      <AgendaEventCard
        event={makeEvent({ dateFrom: '2026-05-18', dateTo: '2026-05-18' })}
        onClick={jest.fn()}
      />,
    );
    expect(screen.queryByText(/–/)).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2.2: Run test — expect FAIL**

```bash
cd frontend && react-scripts test AgendaEventCard.test.tsx --watchAll=false 2>&1 | tail -10
```

Expected: `Cannot find module '../AgendaEventCard'`

- [ ] **Step 2.3: Implement AgendaEventCard.tsx**

Create `frontend/src/components/marketing/calendar/AgendaEventCard.tsx`:

```tsx
import React from 'react';
import { ACTION_TYPE_COLORS } from './fullcalendarAdapters';
import type { CalendarEvent } from './fullcalendarAdapters';

const ACTION_TYPE_LABELS: Record<string, string> = {
  SocialMedia: 'Sociální sítě',
  Blog: 'Blog',
  Newsletter: 'Newsletter',
  PR: 'PR',
  Event: 'Akce',
  Meeting: 'Porada',
};

interface AgendaEventCardProps {
  event: CalendarEvent;
  onClick: () => void;
}

export function AgendaEventCard({ event, onClick }: AgendaEventCardProps) {
  const colors = ACTION_TYPE_COLORS[event.actionType] ?? { bg: '#6b7280', text: '#ffffff' };
  const label = ACTION_TYPE_LABELS[event.actionType] ?? event.actionType;
  const isMultiDay = event.dateFrom !== event.dateTo;

  return (
    <button
      type="button"
      className="agenda-event-card"
      style={{ borderLeftColor: colors.bg }}
      onClick={onClick}
    >
      <div className="agenda-event-card__header">
        <span className="agenda-event-card__title">{event.title}</span>
        <span
          className="agenda-event-card__badge"
          style={{ backgroundColor: colors.bg, color: colors.text }}
        >
          {label}
        </span>
      </div>
      <div className="agenda-event-card__meta">
        {event.associatedProducts.length > 0 && (
          <span>{event.associatedProducts.length} produktů</span>
        )}
        {isMultiDay && <span>{event.dateFrom} – {event.dateTo}</span>}
      </div>
    </button>
  );
}
```

- [ ] **Step 2.4: Run test — expect PASS**

```bash
cd frontend && react-scripts test AgendaEventCard.test.tsx --watchAll=false 2>&1 | tail -10
```

Expected: `Tests: 9 passed`

- [ ] **Step 2.5: Commit**

```bash
git add frontend/src/components/marketing/calendar/AgendaEventCard.tsx \
        frontend/src/components/marketing/calendar/__tests__/AgendaEventCard.test.tsx
git commit -m "feat: add AgendaEventCard component for mobile agenda"
```

---

## Task 3: AgendaDayGroup component + unit tests

**Files:**
- Create: `frontend/src/components/marketing/calendar/AgendaDayGroup.tsx`
- Create: `frontend/src/components/marketing/calendar/__tests__/AgendaDayGroup.test.tsx`

- [ ] **Step 3.1: Write the failing test**

Create `frontend/src/components/marketing/calendar/__tests__/AgendaDayGroup.test.tsx`:

```tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AgendaDayGroup } from '../AgendaDayGroup';
import type { AgendaDay } from '../agendaGrouping';
import type { CalendarEvent } from '../fullcalendarAdapters';

const makeEvent = (overrides: Partial<CalendarEvent> = {}): CalendarEvent => ({
  id: 1,
  title: 'Test akce',
  actionType: 'SocialMedia',
  dateFrom: '2026-05-18',
  dateTo: '2026-05-18',
  associatedProducts: [],
  ...overrides,
});

// 2026-05-18 is a Monday (Po 18. května)
const mondayDay: AgendaDay = { date: '2026-05-18', events: [] };

describe('AgendaDayGroup', () => {
  it('renders a date header containing day number and month', () => {
    render(<AgendaDayGroup day={mondayDay} isToday={false} onEventClick={jest.fn()} />);
    expect(screen.getByText(/18\. května/)).toBeInTheDocument();
  });

  it('shows "Žádné akce" for an empty day', () => {
    render(<AgendaDayGroup day={mondayDay} isToday={false} onEventClick={jest.fn()} />);
    expect(screen.getByText('Žádné akce')).toBeInTheDocument();
  });

  it('renders a card for each event on the day', () => {
    const day: AgendaDay = {
      date: '2026-05-18',
      events: [
        makeEvent({ id: 1, title: 'Akce A' }),
        makeEvent({ id: 2, title: 'Akce B' }),
      ],
    };
    render(<AgendaDayGroup day={day} isToday={false} onEventClick={jest.fn()} />);
    expect(screen.getByText('Akce A')).toBeInTheDocument();
    expect(screen.getByText('Akce B')).toBeInTheDocument();
    expect(screen.queryByText('Žádné akce')).not.toBeInTheDocument();
  });

  it('applies today modifier class when isToday is true', () => {
    render(<AgendaDayGroup day={mondayDay} isToday={true} onEventClick={jest.fn()} />);
    const header = screen.getByText(/18\. května/).closest('[class*="agenda-day-group__header"]');
    expect(header?.className).toContain('agenda-day-group__header--today');
  });

  it('does not apply today modifier when isToday is false', () => {
    render(<AgendaDayGroup day={mondayDay} isToday={false} onEventClick={jest.fn()} />);
    const header = screen.getByText(/18\. května/).closest('[class*="agenda-day-group__header"]');
    expect(header?.className).not.toContain('agenda-day-group__header--today');
  });

  it('calls onEventClick with the event id when a card is tapped', async () => {
    const onEventClick = jest.fn();
    const day: AgendaDay = {
      date: '2026-05-18',
      events: [makeEvent({ id: 42, title: 'Klikni mě' })],
    };
    render(<AgendaDayGroup day={day} isToday={false} onEventClick={onEventClick} />);
    await userEvent.click(screen.getByRole('button'));
    expect(onEventClick).toHaveBeenCalledWith(42);
  });
});
```

- [ ] **Step 3.2: Run test — expect FAIL**

```bash
cd frontend && react-scripts test AgendaDayGroup.test.tsx --watchAll=false 2>&1 | tail -10
```

Expected: `Cannot find module '../AgendaDayGroup'`

- [ ] **Step 3.3: Implement AgendaDayGroup.tsx**

Create `frontend/src/components/marketing/calendar/AgendaDayGroup.tsx`:

```tsx
import React from 'react';
import { AgendaEventCard } from './AgendaEventCard';
import type { AgendaDay } from './agendaGrouping';

const CZECH_WEEKDAYS = ['Ne', 'Po', 'Út', 'St', 'Čt', 'Pá', 'So'];
const CZECH_MONTHS_GENITIVE = [
  'ledna', 'února', 'března', 'dubna', 'května', 'června',
  'července', 'srpna', 'září', 'října', 'listopadu', 'prosince',
];

function buildDayLabel(dateStr: string): string {
  const [y, m, d] = dateStr.split('-').map(Number);
  const date = new Date(y, m - 1, d);
  const weekday = CZECH_WEEKDAYS[date.getDay()];
  const month = CZECH_MONTHS_GENITIVE[m - 1];
  return `${weekday} ${d}. ${month}`;
}

interface AgendaDayGroupProps {
  day: AgendaDay;
  isToday: boolean;
  onEventClick: (id: number) => void;
}

export function AgendaDayGroup({ day, isToday, onEventClick }: AgendaDayGroupProps) {
  const headerClass = [
    'agenda-day-group__header',
    isToday ? 'agenda-day-group__header--today' : '',
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <div className="agenda-day-group">
      <div className={headerClass}>{buildDayLabel(day.date)}</div>
      <div className="agenda-day-group__events">
        {day.events.length === 0 ? (
          <p className="agenda-day-group__empty">Žádné akce</p>
        ) : (
          day.events.map((event) => (
            <AgendaEventCard
              key={event.id}
              event={event}
              onClick={() => onEventClick(event.id)}
            />
          ))
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 3.4: Run test — expect PASS**

```bash
cd frontend && react-scripts test AgendaDayGroup.test.tsx --watchAll=false 2>&1 | tail -10
```

Expected: `Tests: 6 passed`

- [ ] **Step 3.5: Commit**

```bash
git add frontend/src/components/marketing/calendar/AgendaDayGroup.tsx \
        frontend/src/components/marketing/calendar/__tests__/AgendaDayGroup.test.tsx
git commit -m "feat: add AgendaDayGroup component for mobile agenda"
```

---

## Task 4: mobileAgenda.css styles

**Files:**
- Create: `frontend/src/components/marketing/calendar/mobileAgenda.css`

- [ ] **Step 4.1: Create mobileAgenda.css**

Create `frontend/src/components/marketing/calendar/mobileAgenda.css`:

```css
/* MobileAgendaView */

.mobile-agenda {
  display: flex;
  flex-direction: column;
  height: 100%;
  background-color: #f9fafb;
}

.mobile-agenda__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  background-color: white;
  border-bottom: 1px solid #e5e7eb;
  flex-shrink: 0;
}

.mobile-agenda__title {
  font-size: 1.125rem;
  font-weight: 600;
  color: #111827;
}

.mobile-agenda__create-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 44px;
  min-height: 44px;
  background-color: #4f46e5;
  color: white;
  border-radius: 8px;
  transition: background-color 0.15s;
}

.mobile-agenda__create-btn:hover,
.mobile-agenda__create-btn:active {
  background-color: #4338ca;
}

.mobile-agenda__nav {
  padding: 6px 16px;
  background-color: white;
  border-bottom: 1px solid #e5e7eb;
  flex-shrink: 0;
}

.mobile-agenda__scroll {
  flex: 1;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
  padding-bottom: 80px;
}

.mobile-agenda__loading,
.mobile-agenda__error {
  text-align: center;
  padding: 48px 16px;
  color: #6b7280;
  font-size: 0.875rem;
}

.mobile-agenda__error {
  color: #ef4444;
}

.mobile-agenda__retry-btn {
  display: inline-block;
  margin-top: 12px;
  padding: 8px 16px;
  min-height: 44px;
  background-color: #f3f4f6;
  color: #374151;
  border-radius: 6px;
  font-size: 0.875rem;
  transition: background-color 0.15s;
}

.mobile-agenda__retry-btn:hover {
  background-color: #e5e7eb;
}

/* AgendaDayGroup */

.agenda-day-group {
  border-bottom: 1px solid #e5e7eb;
}

.agenda-day-group__header {
  position: sticky;
  top: 0;
  z-index: 10;
  padding: 6px 16px;
  background-color: #f3f4f6;
  font-size: 0.6875rem;
  font-weight: 600;
  color: #6b7280;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.agenda-day-group__header--today {
  background-color: #eff6ff;
  color: #1d4ed8;
}

.agenda-day-group__events {
  padding: 2px 0;
}

.agenda-day-group__empty {
  padding: 8px 16px;
  font-size: 0.875rem;
  color: #9ca3af;
}

/* AgendaEventCard */

.agenda-event-card {
  display: flex;
  flex-direction: column;
  gap: 3px;
  width: 100%;
  min-height: 44px;
  padding: 10px 16px;
  background-color: white;
  border-left: 4px solid transparent;
  border-bottom: 1px solid #f3f4f6;
  text-align: left;
  transition: background-color 0.15s;
}

.agenda-event-card:hover,
.agenda-event-card:active {
  background-color: #f9fafb;
}

.agenda-event-card__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.agenda-event-card__title {
  font-size: 0.875rem;
  font-weight: 500;
  color: #111827;
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.agenda-event-card__badge {
  flex-shrink: 0;
  padding: 2px 8px;
  border-radius: 9999px;
  font-size: 0.6875rem;
  font-weight: 500;
  white-space: nowrap;
}

.agenda-event-card__meta {
  display: flex;
  gap: 8px;
  font-size: 0.75rem;
  color: #6b7280;
  flex-wrap: wrap;
}
```

- [ ] **Step 4.2: Commit**

```bash
git add frontend/src/components/marketing/calendar/mobileAgenda.css
git commit -m "feat: add mobile agenda CSS styles"
```

---

## Task 5: MobileAgendaView component + unit tests

**Files:**
- Create: `frontend/src/components/marketing/calendar/MobileAgendaView.tsx`
- Create: `frontend/src/components/marketing/calendar/__tests__/MobileAgendaView.test.tsx`

- [ ] **Step 5.1: Write the failing test**

Create `frontend/src/components/marketing/calendar/__tests__/MobileAgendaView.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { MobileAgendaView } from '../MobileAgendaView';

// Mocked AgendaDayGroup renders a simple div to let us count day groups
jest.mock('../AgendaDayGroup', () => ({
  AgendaDayGroup: ({ day, isToday, onEventClick }: any) => (
    <div
      data-testid={`day-group-${day.date}`}
      data-is-today={String(isToday)}
    >
      {day.events.map((e: any) => (
        <button key={e.id} onClick={() => onEventClick(e.id)}>
          {e.title}
        </button>
      ))}
    </div>
  ),
}));

jest.mock('../../detail/MarketingActionModal', () => ({
  __esModule: true,
  default: ({ isOpen, existingAction, prefillDates, onClose }: any) =>
    isOpen ? (
      <div data-testid="marketing-modal">
        <span data-testid="modal-existing-id">{existingAction?.id ?? ''}</span>
        <span data-testid="modal-prefill-from">{prefillDates?.dateFrom ?? ''}</span>
        <button onClick={onClose}>Zrušit</button>
      </div>
    ) : null,
}));

jest.mock('../../manufacture/calendar/CalendarNavigation', () => ({
  __esModule: true,
  default: ({ onPrevious, onNext, onToday }: any) => (
    <div data-testid="calendar-navigation">
      <button data-testid="nav-prev" onClick={onPrevious}>Prev</button>
      <button data-testid="nav-today" onClick={onToday}>Dnes</button>
      <button data-testid="nav-next" onClick={onNext}>Next</button>
    </div>
  ),
}));

// Module-level controls so individual tests can override behaviour
let mockIsLoading = false;
let mockError: Error | null = null;
const mockRefetch = jest.fn();
let mockDetailData: any = null;

jest.mock('../../../../api/hooks/useMarketingCalendar', () => ({
  useMarketingCalendar: () => ({
    data: { actions: [] },
    isLoading: mockIsLoading,
    error: mockError,
    refetch: mockRefetch,
  }),
  useMarketingAction: () => ({ data: mockDetailData }),
}));

beforeEach(() => {
  mockIsLoading = false;
  mockError = null;
  mockDetailData = null;
  mockRefetch.mockClear();
});

describe('MobileAgendaView', () => {
  it('renders the "Kalendář" heading', () => {
    render(<MobileAgendaView />);
    expect(screen.getByText('Kalendář')).toBeInTheDocument();
  });

  it('renders exactly 14 day groups', () => {
    render(<MobileAgendaView />);
    expect(screen.getAllByTestId(/^day-group-/)).toHaveLength(14);
  });

  it('renders CalendarNavigation', () => {
    render(<MobileAgendaView />);
    expect(screen.getByTestId('calendar-navigation')).toBeInTheDocument();
  });

  it('shows loading state while fetching', () => {
    mockIsLoading = true;
    render(<MobileAgendaView />);
    expect(screen.getByText('Načítání...')).toBeInTheDocument();
    expect(screen.queryByTestId(/^day-group-/)).not.toBeInTheDocument();
  });

  it('shows inline error message and retry button on fetch failure', () => {
    mockError = new Error('network failure');
    render(<MobileAgendaView />);
    expect(screen.getByText('Chyba při načítání akcí.')).toBeInTheDocument();
    expect(screen.getByText('Zkusit znovu')).toBeInTheDocument();
    expect(screen.queryByTestId(/^day-group-/)).not.toBeInTheDocument();
  });

  it('retry button calls refetch', () => {
    mockError = new Error('fail');
    render(<MobileAgendaView />);
    fireEvent.click(screen.getByText('Zkusit znovu'));
    expect(mockRefetch).toHaveBeenCalledTimes(1);
  });

  it('+ button opens the create modal with today as prefill date', () => {
    render(<MobileAgendaView />);
    fireEvent.click(screen.getByLabelText('Nová akce'));
    expect(screen.getByTestId('marketing-modal')).toBeInTheDocument();
    const today = new Date();
    const todayStr = [
      today.getFullYear(),
      String(today.getMonth() + 1).padStart(2, '0'),
      String(today.getDate()).padStart(2, '0'),
    ].join('-');
    expect(screen.getByTestId('modal-prefill-from')).toHaveTextContent(todayStr);
    expect(screen.getByTestId('modal-existing-id')).toHaveTextContent('');
  });

  it('Zrušit closes the modal', () => {
    render(<MobileAgendaView />);
    fireEvent.click(screen.getByLabelText('Nová akce'));
    expect(screen.getByTestId('marketing-modal')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Zrušit'));
    expect(screen.queryByTestId('marketing-modal')).not.toBeInTheDocument();
  });

  it('prev button shifts the window back by 14 days', () => {
    render(<MobileAgendaView />);
    const before = screen.getAllByTestId(/^day-group-/).map((el) => el.dataset.testid!);
    fireEvent.click(screen.getByTestId('nav-prev'));
    const after = screen.getAllByTestId(/^day-group-/).map((el) => el.dataset.testid!);
    expect(after).toHaveLength(14);
    expect(after).not.toEqual(before);
  });

  it('next button shifts the window forward by 14 days', () => {
    render(<MobileAgendaView />);
    const before = screen.getAllByTestId(/^day-group-/).map((el) => el.dataset.testid!);
    fireEvent.click(screen.getByTestId('nav-next'));
    const after = screen.getAllByTestId(/^day-group-/).map((el) => el.dataset.testid!);
    expect(after).toHaveLength(14);
    expect(after).not.toEqual(before);
  });

  it('today button resets the window to current week', () => {
    render(<MobileAgendaView />);
    fireEvent.click(screen.getByTestId('nav-prev')); // navigate away
    const displaced = screen.getAllByTestId(/^day-group-/).map((el) => el.dataset.testid!);
    fireEvent.click(screen.getByTestId('nav-today'));
    const reset = screen.getAllByTestId(/^day-group-/).map((el) => el.dataset.testid!);
    expect(reset).not.toEqual(displaced);
  });
});
```

- [ ] **Step 5.2: Run test — expect FAIL**

```bash
cd frontend && react-scripts test MobileAgendaView.test.tsx --watchAll=false 2>&1 | tail -10
```

Expected: `Cannot find module '../MobileAgendaView'`

- [ ] **Step 5.3: Implement MobileAgendaView.tsx**

Create `frontend/src/components/marketing/calendar/MobileAgendaView.tsx`:

```tsx
import React, { useState, useMemo, useEffect } from 'react';
import { Plus } from 'lucide-react';
import CalendarNavigation from '../../manufacture/calendar/CalendarNavigation';
import MarketingActionModal from '../detail/MarketingActionModal';
import { useMarketingCalendar, useMarketingAction } from '../../../api/hooks/useMarketingCalendar';
import { formatDateStr } from './fullcalendarAdapters';
import type { CalendarEvent } from './fullcalendarAdapters';
import { groupEventsByDay } from './agendaGrouping';
import { AgendaDayGroup } from './AgendaDayGroup';
import type { MarketingActionDto } from '../list/MarketingActionGrid';
import './mobileAgenda.css';

const CZECH_MONTHS = [
  'Leden', 'Únor', 'Březen', 'Duben', 'Květen', 'Červen',
  'Červenec', 'Srpen', 'Září', 'Říjen', 'Listopad', 'Prosinec',
];

function startOfWeekMonday(date: Date): Date {
  const clone = new Date(date);
  const dow = clone.getDay();
  const daysToMonday = dow === 0 ? 6 : dow - 1;
  clone.setHours(0, 0, 0, 0);
  clone.setDate(clone.getDate() - daysToMonday);
  clone.setHours(0, 0, 0, 0);
  return clone;
}

function addDays(date: Date, n: number): Date {
  const clone = new Date(date);
  clone.setDate(clone.getDate() + n);
  return clone;
}

function buildPeriodLabel(start: Date, end: Date): string {
  const sm = start.getMonth();
  const sy = start.getFullYear();
  const em = end.getMonth();
  const ey = end.getFullYear();
  if (sy === ey && sm === em) return `${CZECH_MONTHS[sm]} ${sy}`;
  if (sy === ey) return `${CZECH_MONTHS[sm]} – ${CZECH_MONTHS[em]} ${sy}`;
  return `${CZECH_MONTHS[sm]} ${sy} – ${CZECH_MONTHS[em]} ${ey}`;
}

export function MobileAgendaView() {
  const [windowStart, setWindowStart] = useState(() => startOfWeekMonday(new Date()));
  const windowEnd = useMemo(() => addDays(windowStart, 13), [windowStart]);

  const [selectedActionId, setSelectedActionId] = useState<number | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingAction, setEditingAction] = useState<MarketingActionDto | null>(null);
  const [prefillDates, setPrefillDates] = useState<{ dateFrom: string; dateTo: string } | null>(null);

  const { data, isLoading, error, refetch } = useMarketingCalendar({
    startDate: windowStart,
    endDate: windowEnd,
  });

  const detailQuery = useMarketingAction(selectedActionId ?? 0);

  useEffect(() => {
    if ((detailQuery.data as any)?.action) {
      const a = (detailQuery.data as any).action;
      setEditingAction({
        id: a.id,
        title: a.title,
        detail: a.description ?? a.detail,
        actionType: a.actionType,
        dateFrom: a.startDate ?? a.dateFrom,
        dateTo: a.endDate ?? a.dateTo,
        associatedProducts: a.associatedProducts,
        folderLinks: a.folderLinks,
      });
    }
  }, [detailQuery.data]);

  const calendarEvents: CalendarEvent[] = useMemo(
    () =>
      ((data as any)?.actions ?? []).map((a: any) => ({
        id: a.id!,
        title: a.title ?? '',
        actionType: a.actionType ?? 'Other',
        dateFrom: a.startDate instanceof Date ? formatDateStr(a.startDate) : (a.dateFrom ?? ''),
        dateTo: a.endDate instanceof Date ? formatDateStr(a.endDate) : (a.dateTo ?? ''),
        associatedProducts: a.associatedProducts ?? [],
        outlookSyncStatus: a.outlookSyncStatus,
      })),
    [data],
  );

  const startStr = formatDateStr(windowStart);
  const endStr = formatDateStr(windowEnd);

  const agendaDays = useMemo(
    () => groupEventsByDay(calendarEvents, startStr, endStr),
    [calendarEvents, startStr, endStr],
  );

  const todayStr = formatDateStr(new Date());

  const openCreate = () => {
    setPrefillDates({ dateFrom: todayStr, dateTo: todayStr });
    setEditingAction(null);
    setSelectedActionId(null);
    setIsModalOpen(true);
  };

  const openEdit = (id: number) => {
    setSelectedActionId(id);
    setIsModalOpen(true);
  };

  const closeModal = () => {
    setIsModalOpen(false);
    setEditingAction(null);
    setSelectedActionId(null);
    setPrefillDates(null);
  };

  const periodLabel = buildPeriodLabel(windowStart, windowEnd);

  return (
    <div className="mobile-agenda">
      <div className="mobile-agenda__header">
        <h1 className="mobile-agenda__title">Kalendář</h1>
        <button
          type="button"
          className="mobile-agenda__create-btn"
          onClick={openCreate}
          aria-label="Nová akce"
        >
          <Plus className="h-5 w-5" />
        </button>
      </div>

      <div className="mobile-agenda__nav">
        <CalendarNavigation
          onPrevious={() => setWindowStart((d) => addDays(d, -14))}
          onNext={() => setWindowStart((d) => addDays(d, 14))}
          onToday={() => setWindowStart(startOfWeekMonday(new Date()))}
          currentPeriodLabel={periodLabel}
          size="sm"
        />
      </div>

      <div className="mobile-agenda__scroll">
        {isLoading ? (
          <div className="mobile-agenda__loading">Načítání...</div>
        ) : error ? (
          <div className="mobile-agenda__error">
            <p>Chyba při načítání akcí.</p>
            <button
              type="button"
              className="mobile-agenda__retry-btn"
              onClick={() => refetch()}
            >
              Zkusit znovu
            </button>
          </div>
        ) : (
          agendaDays.map((day) => (
            <AgendaDayGroup
              key={day.date}
              day={day}
              isToday={day.date === todayStr}
              onEventClick={openEdit}
            />
          ))
        )}
      </div>

      <MarketingActionModal
        isOpen={isModalOpen}
        onClose={closeModal}
        existingAction={editingAction}
        prefillDates={prefillDates}
      />
    </div>
  );
}
```

- [ ] **Step 5.4: Run test — expect PASS**

```bash
cd frontend && react-scripts test MobileAgendaView.test.tsx --watchAll=false 2>&1 | tail -10
```

Expected: `Tests: 11 passed`

- [ ] **Step 5.5: Commit**

```bash
git add frontend/src/components/marketing/calendar/MobileAgendaView.tsx \
        frontend/src/components/marketing/calendar/__tests__/MobileAgendaView.test.tsx
git commit -m "feat: add MobileAgendaView with 14-day window and navigation"
```

---

## Task 6: Wire MobileAgendaView into MarketingCalendarPage

**Files:**
- Modify: `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`
- Modify: `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`

- [ ] **Step 6.1: Add tests for mobile rendering to MarketingCalendarPage.test.tsx**

Add a `useIsMobile` mock and a mobile describe block to the **top of the existing test file**, right after the existing `jest.mock(...)` calls (before `beforeEach`):

Add after the last existing `jest.mock(...)` block (line ~130, after the `useAuth` mock):

```typescript
// Track what useIsMobile returns — toggled per test
let mockIsMobile = false;
jest.mock('../../../hooks/useMediaQuery', () => ({
  useIsMobile: () => mockIsMobile,
}));

jest.mock('../calendar/MobileAgendaView', () => ({
  __esModule: true,
  MobileAgendaView: () => React.createElement('div', { 'data-testid': 'mobile-agenda-view' }),
}));
```

And add in `beforeEach`:

```typescript
mockIsMobile = false; // default to desktop for all existing tests
```

Then add a new describe block at the bottom of the file:

```typescript
describe('MarketingCalendarPage — mobile layout', () => {
  it('renders MobileAgendaView instead of the calendar grid on mobile', () => {
    mockIsMobile = true;
    render(<MarketingCalendarPage />);
    expect(screen.getByTestId('mobile-agenda-view')).toBeInTheDocument();
    expect(screen.queryByTestId('marketing-month-calendar')).not.toBeInTheDocument();
  });

  it('hides the view-mode toggle group on mobile', () => {
    mockIsMobile = true;
    render(<MarketingCalendarPage />);
    expect(screen.queryByRole('button', { name: /5 týdnů/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /14 dní/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Seznam/ })).not.toBeInTheDocument();
  });

  it('still renders the desktop calendar on viewport ≥768px', () => {
    mockIsMobile = false;
    render(<MarketingCalendarPage />);
    expect(screen.getByTestId('marketing-month-calendar')).toBeInTheDocument();
    expect(screen.queryByTestId('mobile-agenda-view')).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 6.2: Run existing tests — confirm they pass with new mock declarations**

```bash
cd frontend && react-scripts test MarketingCalendarPage.test.tsx --watchAll=false 2>&1 | tail -15
```

Expected: all pre-existing tests still pass; new mobile tests FAIL with "Cannot find module ../hooks/useMediaQuery" or component render errors.

- [ ] **Step 6.3: Modify MarketingCalendarPage.tsx**

Add these two imports after the existing imports (after line 23 in the file):

```typescript
import { useIsMobile } from '../../../hooks/useMediaQuery';
import { MobileAgendaView } from '../calendar/MobileAgendaView';
```

Add `useIsMobile()` call as the first hook inside the component (after line 53, after the `useState` for `viewMode`):

```typescript
const isMobile = useIsMobile();
```

Add early return just before the final `return (` statement (after line 232):

```typescript
if (isMobile) {
  return <MobileAgendaView />;
}
```

The final shape of the component function will be:

```typescript
const MarketingCalendarPage: React.FC = () => {
  const [viewMode, setViewMode] = useState<ViewMode>('fiveWeeks');
  // ... all existing state declarations unchanged ...

  const isMobile = useIsMobile(); // ← ADD THIS (first hook after state)

  // ... all existing hook calls unchanged ...

  if (isMobile) {              // ← ADD THIS BLOCK
    return <MobileAgendaView />;
  }

  return (
    <div className="flex flex-col" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      {/* existing JSX unchanged */}
    </div>
  );
};
```

Exact diff to apply:

1. After the last existing `import` line (line 23), insert:
```typescript
import { useIsMobile } from '../../../hooks/useMediaQuery';
import { MobileAgendaView } from '../calendar/MobileAgendaView';
```

2. After `const [isImportModalOpen, setIsImportModalOpen] = useState(false);` (line 62), insert:
```typescript

  const isMobile = useIsMobile();
```

3. Before `return (` at line 233, insert:
```typescript

  if (isMobile) {
    return <MobileAgendaView />;
  }

```

- [ ] **Step 6.4: Run tests — expect all PASS**

```bash
cd frontend && react-scripts test MarketingCalendarPage.test.tsx --watchAll=false 2>&1 | tail -15
```

Expected: all tests pass including the three new mobile tests.

- [ ] **Step 6.5: Run full frontend build**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: `Compiled successfully.`

- [ ] **Step 6.6: Run lint**

```bash
cd frontend && npm run lint 2>&1 | tail -10
```

Expected: no errors.

- [ ] **Step 6.7: Commit**

```bash
git add frontend/src/components/marketing/pages/MarketingCalendarPage.tsx \
        frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx
git commit -m "feat: wire MobileAgendaView into MarketingCalendarPage on mobile viewports"
```

---

## Task 7: E2E test — mobile agenda at 375px

**Files:**
- Create: `frontend/test/e2e/marketing/mobile-agenda.spec.ts`

- [ ] **Step 7.1: Write the E2E test**

Create `frontend/test/e2e/marketing/mobile-agenda.spec.ts`:

```typescript
import { test, expect } from '@playwright/test';
import { navigateToMarketingCalendar } from '../helpers/e2e-auth-helper';

test.describe('Marketing Calendar — Mobile Agenda View', () => {
  test.use({ viewport: { width: 375, height: 812 } });

  test.beforeEach(async ({ page }) => {
    await navigateToMarketingCalendar(page);
  });

  test('renders the mobile agenda view, not the desktop calendar grid', async ({ page }) => {
    await expect(page.locator('h1').filter({ hasText: 'Kalendář' })).toBeVisible({ timeout: 10000 });
    // Desktop month calendar must NOT be present
    await expect(page.locator('[data-testid="marketing-month-calendar"]')).not.toBeVisible();
    // Desktop view toggle buttons must NOT be present
    await expect(page.locator('button').filter({ hasText: '5 týdnů' })).not.toBeVisible();
  });

  test('renders at least one day section header', async ({ page }) => {
    await expect(page.locator('.agenda-day-group__header').first()).toBeVisible({ timeout: 10000 });
  });

  test('loading spinner resolves and 14 day sections appear', async ({ page }) => {
    await expect(page.locator('text=Načítání...').first()).not.toBeVisible({ timeout: 15000 });
    const dayGroups = page.locator('.agenda-day-group');
    await expect(dayGroups).toHaveCount(14, { timeout: 10000 });
  });

  test('prev navigation shifts the window back', async ({ page }) => {
    await expect(page.locator('text=Načítání...').first()).not.toBeVisible({ timeout: 15000 });

    const firstHeaderBefore = await page.locator('.agenda-day-group__header').first().textContent();

    // CalendarNavigation prev button is the first icon-only button in the nav bar
    const navButtons = page.locator('.mobile-agenda__nav button').filter({ hasText: /^$/ });
    await navButtons.first().click();

    await expect(page.locator('text=Načítání...').first()).not.toBeVisible({ timeout: 10000 });

    const firstHeaderAfter = await page.locator('.agenda-day-group__header').first().textContent();
    expect(firstHeaderAfter).not.toBe(firstHeaderBefore);
  });

  test('+ button opens create modal with Vytvořit submit', async ({ page }) => {
    await page.locator('button[aria-label="Nová akce"]').click();
    await expect(page.locator('text=Nová marketingová akce')).toBeVisible({ timeout: 5000 });
    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).toBeVisible();
    await expect(page.locator('button').filter({ hasText: 'Smazat' })).not.toBeVisible();
  });

  test('create modal dismisses via Zrušit', async ({ page }) => {
    await page.locator('button[aria-label="Nová akce"]').click();
    await expect(page.locator('text=Nová marketingová akce')).toBeVisible({ timeout: 5000 });
    await page.locator('button').filter({ hasText: 'Zrušit' }).first().click();
    await expect(page.locator('text=Nová marketingová akce')).not.toBeVisible({ timeout: 3000 });
  });

  test('tapping an event card opens the edit modal', async ({ page }) => {
    await expect(page.locator('text=Načítání...').first()).not.toBeVisible({ timeout: 15000 });

    const cards = page.locator('.agenda-event-card');
    const count = await cards.count();

    if (count === 0) {
      console.log('No events in current 14-day window — skipping card tap test');
      return;
    }

    await cards.first().click();
    await expect(page.locator('button').filter({ hasText: 'Uložit' }).first()).toBeVisible({ timeout: 5000 });
    await expect(page.locator('button').filter({ hasText: 'Smazat' }).first()).toBeVisible();
  });

  test('resizing to desktop width shows the grid, not the agenda', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    // Wait for React to re-render
    await page.waitForTimeout(300);
    // Desktop calendar grid present; mobile agenda heading gone
    await expect(page.locator('button').filter({ hasText: '5 týdnů' })).toBeVisible({ timeout: 5000 });
    await expect(page.locator('h1').filter({ hasText: 'Marketingový kalendář' })).toBeVisible();
  });
});
```

- [ ] **Step 7.2: Commit**

```bash
git add frontend/test/e2e/marketing/mobile-agenda.spec.ts
git commit -m "test(e2e): add mobile agenda Playwright spec at 375px viewport"
```

---

## Verification checklist

- [ ] `cd frontend && npm run build` — no errors
- [ ] `cd frontend && npm run lint` — no warnings on new files
- [ ] `cd frontend && react-scripts test --watchAll=false 2>&1 | grep -E "Tests:|Test Suites:"` — all pass, coverage ≥80% on new files
- [ ] Manual: DevTools device toolbar at 375px — agenda renders, sticky headers visible, prev/next paginates, `+` opens create modal, event card tap opens edit modal
- [ ] Manual: Resize across 768px boundary — views swap reactively
- [ ] `./scripts/run-playwright-tests.sh` against staging — new E2E spec passes
