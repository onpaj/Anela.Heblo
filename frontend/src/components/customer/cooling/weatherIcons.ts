export function getWeatherIcon(weatherCode: number): string {
  if (weatherCode === 0) return '☀️';
  if (weatherCode <= 2) return '🌤️';
  if (weatherCode === 3) return '☁️';
  if (weatherCode === 45 || weatherCode === 48) return '🌫️';
  if (weatherCode >= 51 && weatherCode <= 67) return '🌧️';
  if (weatherCode >= 71 && weatherCode <= 77) return '❄️';
  if (weatherCode >= 80 && weatherCode <= 82) return '🌦️';
  if (weatherCode >= 95 && weatherCode <= 99) return '⛈️';
  return '☁️';
}
