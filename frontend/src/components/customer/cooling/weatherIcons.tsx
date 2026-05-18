import { Sun, CloudSun, Cloud, CloudFog, CloudRain, CloudSnow, CloudLightning, type LucideIcon } from 'lucide-react';

export function getWeatherIcon(weatherCode: number): LucideIcon {
  if (weatherCode === 0) return Sun;
  if (weatherCode <= 2) return CloudSun;
  if (weatherCode === 3) return Cloud;
  if (weatherCode === 45 || weatherCode === 48) return CloudFog;
  if (weatherCode >= 51 && weatherCode <= 67) return CloudRain;
  if (weatherCode >= 71 && weatherCode <= 77) return CloudSnow;
  if (weatherCode >= 80 && weatherCode <= 82) return CloudRain;
  if (weatherCode >= 95 && weatherCode <= 99) return CloudLightning;
  return Cloud;
}
