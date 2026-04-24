import { computePreviewEvents } from '../useCalendarDnd';
import type { CalendarEvent } from '../useCalendarLayout';

const EVENT: CalendarEvent = {
  id: 1,
  title: 'Test Event',
  actionType: 'SocialMedia',
  dateFrom: '2026-04-10',
  dateTo: '2026-04-15',
  associatedProducts: [],
};
const EVENTS: CalendarEvent[] = [
  EVENT,
  {
    id: 2,
    title: 'Other',
    actionType: 'Email',
    dateFrom: '2026-04-12',
    dateTo: '2026-04-14',
    associatedProducts: [],
  },
];

describe('computePreviewEvents', () => {
  test('move shifts both dates by delta', () => {
    const result = computePreviewEvents(EVENTS, { type: 'move', eventId: 1, dateDelta: 3 });
    const moved = result.find((e) => e.id === 1)!;
    expect(moved.dateFrom).toBe('2026-04-13');
    expect(moved.dateTo).toBe('2026-04-18');
  });

  test('move does not affect other events', () => {
    const result = computePreviewEvents(EVENTS, { type: 'move', eventId: 1, dateDelta: 3 });
    const other = result.find((e) => e.id === 2)!;
    expect(other.dateFrom).toBe('2026-04-12');
    expect(other.dateTo).toBe('2026-04-14');
  });

  test('resize-start adjusts dateFrom only', () => {
    const result = computePreviewEvents(EVENTS, { type: 'resize-start', eventId: 1, dateDelta: 2 });
    const resized = result.find((e) => e.id === 1)!;
    expect(resized.dateFrom).toBe('2026-04-12');
    expect(resized.dateTo).toBe('2026-04-15');
  });

  test('resize-start clamps to not exceed dateTo', () => {
    const result = computePreviewEvents(EVENTS, { type: 'resize-start', eventId: 1, dateDelta: 10 });
    const resized = result.find((e) => e.id === 1)!;
    expect(resized.dateFrom).toBe('2026-04-15');
    expect(resized.dateTo).toBe('2026-04-15');
  });

  test('resize-end adjusts dateTo only', () => {
    const result = computePreviewEvents(EVENTS, { type: 'resize-end', eventId: 1, dateDelta: -2 });
    const resized = result.find((e) => e.id === 1)!;
    expect(resized.dateFrom).toBe('2026-04-10');
    expect(resized.dateTo).toBe('2026-04-13');
  });

  test('resize-end clamps to not go before dateFrom', () => {
    const result = computePreviewEvents(EVENTS, { type: 'resize-end', eventId: 1, dateDelta: -10 });
    const resized = result.find((e) => e.id === 1)!;
    expect(resized.dateFrom).toBe('2026-04-10');
    expect(resized.dateTo).toBe('2026-04-10');
  });

  test('returns unchanged events when no drag state', () => {
    const result = computePreviewEvents(EVENTS, null);
    expect(result).toBe(EVENTS);
  });
});
