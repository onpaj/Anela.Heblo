import {
  clampMm,
  loadLabelOffsets,
  saveLabelOffset,
  toOffsetsQuery,
} from '../labelOffsetStorage';

const STORAGE_KEY = 'heblo.baleni.labelOffsets';

const realStorage = window.localStorage;

const replaceStorage = (storage: Partial<Storage>) =>
  Object.defineProperty(window, 'localStorage', { value: storage, configurable: true });

beforeEach(() => {
  replaceStorage(realStorage);
  window.localStorage.clear();
});

afterAll(() => replaceStorage(realStorage));

describe('labelOffsetStorage', () => {
  it('returns no offsets when nothing is stored', () => {
    expect(loadLabelOffsets()).toEqual({});
  });

  it('saves one carrier without touching the others', () => {
    saveLabelOffset('Zasilkovna', { xMm: 0, yMm: -5 });
    saveLabelOffset('PPL', { xMm: 2, yMm: 1 });

    expect(loadLabelOffsets()).toEqual({
      Zasilkovna: { xMm: 0, yMm: -5 },
      PPL: { xMm: 2, yMm: 1 },
    });
  });

  it('clamps saved values to the allowed range', () => {
    saveLabelOffset('GLS', { xMm: 99, yMm: -99 });
    expect(loadLabelOffsets().GLS).toEqual({ xMm: 30, yMm: -30 });
  });

  it('drops malformed, out-of-range and unknown entries', () => {
    window.localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        Zasilkovna: { xMm: 1, yMm: 2 },
        PPL: { xMm: 'a', yMm: 0 },
        GLS: { xMm: 31, yMm: 0 },
        Osobak: { xMm: 1, yMm: 1 },
      }),
    );

    expect(loadLabelOffsets()).toEqual({ Zasilkovna: { xMm: 1, yMm: 2 } });
  });

  it('ignores stored data that is not JSON', () => {
    window.localStorage.setItem(STORAGE_KEY, '{not json');
    expect(loadLabelOffsets()).toEqual({});
  });

  it('returns no offsets when storage is unavailable', () => {
    saveLabelOffset('PPL', { xMm: 1, yMm: 1 });
    replaceStorage({
      getItem: () => {
        throw new Error('blocked');
      },
    });
    expect(loadLabelOffsets()).toEqual({});
  });

  it('reports failure when storage refuses to save', () => {
    replaceStorage({
      getItem: () => null,
      setItem: () => {
        throw new Error('quota');
      },
    });
    expect(saveLabelOffset('PPL', { xMm: 1, yMm: 1 })).toBe(false);
  });

  it('serialises non-zero offsets for the label endpoint', () => {
    expect(
      toOffsetsQuery({ Zasilkovna: { xMm: 0, yMm: -5 }, PPL: { xMm: 0, yMm: 0 }, GLS: { xMm: 3, yMm: 1 } }),
    ).toBe('Zasilkovna:0:-5,GLS:3:1');
    expect(toOffsetsQuery({})).toBe('');
  });

  it('clamps and rounds millimetres', () => {
    expect(clampMm(31)).toBe(30);
    expect(clampMm(-31)).toBe(-30);
    expect(clampMm(2.4)).toBe(2);
  });
});
