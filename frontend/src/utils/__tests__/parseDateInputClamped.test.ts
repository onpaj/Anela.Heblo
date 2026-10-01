import { parseDateInputClamped } from '../dateUtils';

describe('parseDateInputClamped', () => {
  it.each([
    ['15.3.2027', new Date(2027, 2, 15)],
    ['15. 03. 2027', new Date(2027, 2, 15)],
    ['15/03/2027', new Date(2027, 2, 15)],
    ['15-03-2027', new Date(2027, 2, 15)],
    ['15.3.27', new Date(2027, 2, 15)],
    ['2027-03-15', new Date(2027, 2, 15)],
  ])('parses "%s"', (text, expected) => {
    expect(parseDateInputClamped(text)).toEqual(expected);
  });

  it.each([
    ['31.11.2026', new Date(2026, 10, 30)],
    ['31.4.2027', new Date(2027, 3, 30)],
    ['30.2.2027', new Date(2027, 1, 28)],
    ['30.2.2028', new Date(2028, 1, 29)],
    ['2026-11-31', new Date(2026, 10, 30)],
  ])('clamps the day of "%s" to the last day of its month', (text, expected) => {
    expect(parseDateInputClamped(text)).toEqual(expected);
  });

  it.each(['', '   ', 'abc', '15.13.2027', '0.3.2027', '15.0.2027', '15.3', '1.2.3.4'])(
    'returns null for "%s"',
    (text) => {
      expect(parseDateInputClamped(text)).toBeNull();
    },
  );
});
