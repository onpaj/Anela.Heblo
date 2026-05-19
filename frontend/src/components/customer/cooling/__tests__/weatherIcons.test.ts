import { getWeatherIcon } from '../weatherIcons';

describe('getWeatherIcon', () => {
  it('returns sun emoji for code 0 (clear sky)', () => {
    expect(getWeatherIcon(0)).toBe('☀️');
  });

  it('returns partly sunny emoji for codes 1-2 (mainly/partly clear)', () => {
    expect(getWeatherIcon(1)).toBe('🌤️');
    expect(getWeatherIcon(2)).toBe('🌤️');
  });

  it('returns cloud emoji for code 3 (overcast)', () => {
    expect(getWeatherIcon(3)).toBe('☁️');
  });

  it('returns fog emoji for fog codes 45 and 48', () => {
    expect(getWeatherIcon(45)).toBe('🌫️');
    expect(getWeatherIcon(48)).toBe('🌫️');
  });

  it('returns rain emoji for drizzle/rain codes 51-67', () => {
    expect(getWeatherIcon(51)).toBe('🌧️');
    expect(getWeatherIcon(61)).toBe('🌧️');
    expect(getWeatherIcon(67)).toBe('🌧️');
  });

  it('returns snow emoji for snow codes 71-77', () => {
    expect(getWeatherIcon(71)).toBe('❄️');
    expect(getWeatherIcon(75)).toBe('❄️');
  });

  it('returns rain shower emoji for rain shower codes 80-82', () => {
    expect(getWeatherIcon(80)).toBe('🌦️');
    expect(getWeatherIcon(82)).toBe('🌦️');
  });

  it('returns thunderstorm emoji for thunderstorm codes 95-99', () => {
    expect(getWeatherIcon(95)).toBe('⛈️');
    expect(getWeatherIcon(99)).toBe('⛈️');
  });

  it('returns cloud emoji for unknown codes', () => {
    expect(getWeatherIcon(999)).toBe('☁️');
  });
});
