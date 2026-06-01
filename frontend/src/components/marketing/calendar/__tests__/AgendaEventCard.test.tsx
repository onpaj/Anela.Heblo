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
        event={makeEvent({ associatedProducts: ['p1', 'p2', 'p3', 'p4', 'p5'] })}
        onClick={jest.fn()}
      />,
    );
    expect(screen.getByText('5 produktů')).toBeInTheDocument();
  });

  it('shows singular "produkt" for 1 product', () => {
    render(
      <AgendaEventCard
        event={makeEvent({ associatedProducts: ['p1'] })}
        onClick={jest.fn()}
      />,
    );
    expect(screen.getByText('1 produkt')).toBeInTheDocument();
  });

  it('shows "produkty" for 2–4 products', () => {
    render(
      <AgendaEventCard
        event={makeEvent({ associatedProducts: ['p1', 'p2', 'p3'] })}
        onClick={jest.fn()}
      />,
    );
    expect(screen.getByText('3 produkty')).toBeInTheDocument();
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
