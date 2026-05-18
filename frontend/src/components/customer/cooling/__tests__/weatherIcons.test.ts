import { Sun, CloudSun, Cloud, CloudFog, CloudRain, CloudSnow, CloudLightning } from 'lucide-react';
import { getWeatherIcon } from '../weatherIcons';

describe('getWeatherIcon', () => {
  it('returns Sun for code 0 (clear sky)', () => {
    expect(getWeatherIcon(0)).toBe(Sun);
  });

  it('returns CloudSun for codes 1-2 (mainly/partly clear)', () => {
    expect(getWeatherIcon(1)).toBe(CloudSun);
    expect(getWeatherIcon(2)).toBe(CloudSun);
  });

  it('returns Cloud for code 3 (overcast)', () => {
    expect(getWeatherIcon(3)).toBe(Cloud);
  });

  it('returns CloudFog for fog codes 45 and 48', () => {
    expect(getWeatherIcon(45)).toBe(CloudFog);
    expect(getWeatherIcon(48)).toBe(CloudFog);
  });

  it('returns CloudRain for drizzle/rain codes 51-67', () => {
    expect(getWeatherIcon(51)).toBe(CloudRain);
    expect(getWeatherIcon(61)).toBe(CloudRain);
    expect(getWeatherIcon(67)).toBe(CloudRain);
  });

  it('returns CloudSnow for snow codes 71-77', () => {
    expect(getWeatherIcon(71)).toBe(CloudSnow);
    expect(getWeatherIcon(75)).toBe(CloudSnow);
  });

  it('returns CloudRain for rain shower codes 80-82', () => {
    expect(getWeatherIcon(80)).toBe(CloudRain);
    expect(getWeatherIcon(82)).toBe(CloudRain);
  });

  it('returns CloudLightning for thunderstorm codes 95-99', () => {
    expect(getWeatherIcon(95)).toBe(CloudLightning);
    expect(getWeatherIcon(99)).toBe(CloudLightning);
  });

  it('returns Cloud for unknown codes', () => {
    const icon = getWeatherIcon(999);
    expect(icon.displayName).toBe('Cloud');
  });
});
