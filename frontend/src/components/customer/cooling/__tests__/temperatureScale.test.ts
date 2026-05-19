import {
  TEMP_SCALE_MIN,
  TEMP_SCALE_MAX,
  COOL_THRESHOLD,
  WARM_THRESHOLD,
  HOT_THRESHOLD,
  VERY_HOT_THRESHOLD,
  getTemperatureBarPercent,
  getTemperatureColor,
  getTemperatureRangeBar,
} from '../temperatureScale';

describe('getTemperatureBarPercent', () => {
  it('returns 0 for temperature at or below minimum', () => {
    expect(getTemperatureBarPercent(TEMP_SCALE_MIN)).toBe(0);
    expect(getTemperatureBarPercent(-5)).toBe(0);
  });

  it('returns 100 for temperature at or above maximum', () => {
    expect(getTemperatureBarPercent(TEMP_SCALE_MAX)).toBe(100);
    expect(getTemperatureBarPercent(50)).toBe(100);
  });

  it('returns 50 for the midpoint (20 °C on a 0–40 scale)', () => {
    expect(getTemperatureBarPercent(20)).toBe(50);
  });

  it('returns a proportional value for an arbitrary temperature', () => {
    expect(getTemperatureBarPercent(10)).toBe(25);
    expect(getTemperatureBarPercent(30)).toBe(75);
  });
});

describe('getTemperatureColor', () => {
  it('returns a cool class for temperatures below 15 °C', () => {
    expect(getTemperatureColor(10)).toBe('bg-sky-400');
    expect(getTemperatureColor(0)).toBe('bg-sky-400');
  });

  it('returns a hot class for temperatures above 34 °C', () => {
    expect(getTemperatureColor(35)).toBe('bg-red-500');
    expect(getTemperatureColor(40)).toBe('bg-red-500');
  });

  it('returns different classes for cool vs hot temperatures', () => {
    const cool = getTemperatureColor(5);
    const hot = getTemperatureColor(38);
    expect(cool).not.toBe(hot);
  });

  it('returns emerald for temperatures in [15, 22)', () => {
    expect(getTemperatureColor(15)).toBe('bg-emerald-400');
    expect(getTemperatureColor(21)).toBe('bg-emerald-400');
  });

  it('returns amber for temperatures in [22, 28)', () => {
    expect(getTemperatureColor(22)).toBe('bg-amber-400');
    expect(getTemperatureColor(27)).toBe('bg-amber-400');
  });

  it('returns orange for temperatures in [28, 34)', () => {
    expect(getTemperatureColor(28)).toBe('bg-orange-500');
    expect(getTemperatureColor(33)).toBe('bg-orange-500');
  });
});

describe('getTemperatureRangeBar', () => {
  it('returns left=0 and width=100 for the full scale range (0–40 °C)', () => {
    const result = getTemperatureRangeBar(TEMP_SCALE_MIN, TEMP_SCALE_MAX);
    expect(result.left).toBe(0);
    expect(result.width).toBe(100);
  });

  it('returns correct left and width for a mid-range day (10–30 °C)', () => {
    const result = getTemperatureRangeBar(10, 30);
    expect(result.left).toBe(25);   // (10 - 0) / (40 - 0) * 100
    expect(result.width).toBe(50);  // (30 - 10) / (40 - 0) * 100
  });

  it('clamps both min and max outside the scale (-5–50 °C)', () => {
    const result = getTemperatureRangeBar(-5, 50);
    expect(result.left).toBe(0);
    expect(result.width).toBe(100);
  });

  it('returns width=0 when min equals max', () => {
    const result = getTemperatureRangeBar(20, 20);
    expect(result.left).toBe(50);
    expect(result.width).toBe(0);
  });

  it('returns width=0 when min exceeds max (inverted bad data)', () => {
    const result = getTemperatureRangeBar(25, 15);
    expect(result.left).toBe(getTemperatureBarPercent(25));
    expect(result.width).toBe(0);
  });
});
