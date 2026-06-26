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
