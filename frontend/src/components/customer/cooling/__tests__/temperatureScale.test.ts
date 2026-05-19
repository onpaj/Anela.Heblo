import {
  TEMP_SCALE_MIN,
  TEMP_SCALE_MAX,
  getTemperatureBarPercent,
  getTemperatureColor,
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
});
