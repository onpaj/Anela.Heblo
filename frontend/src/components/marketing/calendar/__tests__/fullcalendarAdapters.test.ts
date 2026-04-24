import {
  toFcEvent,
  fromFcDates,
  formatDateStr,
  ACTION_TYPE_TO_INT,
  ACTION_TYPE_COLORS,
} from '../fullcalendarAdapters';
import type { CalendarEvent } from '../fullcalendarAdapters';

const makeEvent = (overrides: Partial<CalendarEvent> = {}): CalendarEvent => ({
  id: 1,
  title: 'Test event',
  actionType: 'SocialMedia',
  dateFrom: '2026-04-20',
  dateTo: '2026-04-24',
  associatedProducts: ['product-1'],
  ...overrides,
});

describe('toFcEvent', () => {
  it('adds 1 day to dateTo for exclusive FullCalendar end', () => {
    const fc = toFcEvent(makeEvent({ dateFrom: '2026-04-20', dateTo: '2026-04-24' }));
    expect(fc.end).toBe('2026-04-25');
  });

  it('sets start to dateFrom unchanged', () => {
    const fc = toFcEvent(makeEvent({ dateFrom: '2026-04-20' }));
    expect(fc.start).toBe('2026-04-20');
  });

  it('sets id as string', () => {
    const fc = toFcEvent(makeEvent({ id: 42 }));
    expect(fc.id).toBe('42');
  });

  it('sets title', () => {
    const fc = toFcEvent(makeEvent({ title: 'My Campaign' }));
    expect(fc.title).toBe('My Campaign');
  });

  it('sets backgroundColor from action type colors', () => {
    const fc = toFcEvent(makeEvent({ actionType: 'Email' }));
    expect(fc.backgroundColor).toBe(ACTION_TYPE_COLORS.Email.bg);
  });

  it('sets textColor from action type colors', () => {
    const fc = toFcEvent(makeEvent({ actionType: 'PR' }));
    expect(fc.textColor).toBe(ACTION_TYPE_COLORS.PR.text);
  });

  it('falls back to Other colors for unknown action type', () => {
    const fc = toFcEvent(makeEvent({ actionType: 'Unknown' }));
    expect(fc.backgroundColor).toBe(ACTION_TYPE_COLORS.Other.bg);
  });

  it('puts actionType in extendedProps', () => {
    const fc = toFcEvent(makeEvent({ actionType: 'Photoshoot' }));
    expect((fc.extendedProps as any).actionType).toBe('Photoshoot');
  });

  it('puts associatedProducts in extendedProps', () => {
    const fc = toFcEvent(makeEvent({ associatedProducts: ['p1', 'p2'] }));
    expect((fc.extendedProps as any).associatedProducts).toEqual(['p1', 'p2']);
  });

  it('handles single-day event (dateFrom === dateTo)', () => {
    const fc = toFcEvent(makeEvent({ dateFrom: '2026-04-20', dateTo: '2026-04-20' }));
    expect(fc.start).toBe('2026-04-20');
    expect(fc.end).toBe('2026-04-21');
  });
});

describe('fromFcDates', () => {
  it('subtracts 1 day from end for inclusive dateTo', () => {
    const result = fromFcDates(new Date('2026-04-20'), new Date('2026-04-25'));
    expect(result.dateTo).toBe('2026-04-24');
  });

  it('sets dateFrom from start unchanged', () => {
    const result = fromFcDates(new Date('2026-04-20'), new Date('2026-04-25'));
    expect(result.dateFrom).toBe('2026-04-20');
  });

  it('handles null end by using start as dateTo', () => {
    const result = fromFcDates(new Date('2026-04-20'), null);
    expect(result.dateFrom).toBe('2026-04-20');
    expect(result.dateTo).toBe('2026-04-20');
  });

  it('formats dates as YYYY-MM-DD', () => {
    const result = fromFcDates(new Date('2026-01-05'), new Date('2026-01-10'));
    expect(result.dateFrom).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(result.dateTo).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});

describe('formatDateStr', () => {
  it('formats a date as YYYY-MM-DD', () => {
    expect(formatDateStr(new Date(2026, 3, 5))).toBe('2026-04-05'); // April is month 3
  });

  it('zero-pads month and day', () => {
    expect(formatDateStr(new Date(2026, 0, 1))).toBe('2026-01-01');
  });
});

describe('ACTION_TYPE_TO_INT', () => {
  it('maps SocialMedia to 0', () => {
    expect(ACTION_TYPE_TO_INT['SocialMedia']).toBe(0);
  });

  it('maps General to 0', () => {
    expect(ACTION_TYPE_TO_INT['General']).toBe(0);
  });

  it('maps Email to 2', () => {
    expect(ACTION_TYPE_TO_INT['Email']).toBe(2);
  });

  it('maps PR to 3', () => {
    expect(ACTION_TYPE_TO_INT['PR']).toBe(3);
  });

  it('maps Event to 4', () => {
    expect(ACTION_TYPE_TO_INT['Event']).toBe(4);
  });

  it('maps Photoshoot to 4', () => {
    expect(ACTION_TYPE_TO_INT['Photoshoot']).toBe(4);
  });

  it('maps Other to 99', () => {
    expect(ACTION_TYPE_TO_INT['Other']).toBe(99);
  });
});

describe('ACTION_TYPE_COLORS', () => {
  it('has entries for all known action types', () => {
    const expected = ['SocialMedia', 'Event', 'Email', 'PR', 'Photoshoot', 'Other'];
    for (const type of expected) {
      expect(ACTION_TYPE_COLORS[type]).toBeDefined();
      expect(ACTION_TYPE_COLORS[type].bg).toBeTruthy();
      expect(ACTION_TYPE_COLORS[type].text).toBeTruthy();
    }
  });
});
