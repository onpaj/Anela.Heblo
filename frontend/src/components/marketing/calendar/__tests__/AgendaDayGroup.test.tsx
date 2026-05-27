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
    expect(screen.getByText(/Po 18\. května/)).toBeInTheDocument();
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
    const header = screen.getByText(/Po 18\. května/).closest('[class*="agenda-day-group__header"]');
    expect(header?.className).toContain('agenda-day-group__header--today');
  });

  it('does not apply today modifier when isToday is false', () => {
    render(<AgendaDayGroup day={mondayDay} isToday={false} onEventClick={jest.fn()} />);
    const header = screen.getByText(/Po 18\. května/).closest('[class*="agenda-day-group__header"]');
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
