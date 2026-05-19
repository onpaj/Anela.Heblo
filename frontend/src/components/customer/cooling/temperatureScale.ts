export const TEMP_SCALE_MIN = 0;
export const TEMP_SCALE_MAX = 40;

const COOL_THRESHOLD = 15;
const WARM_THRESHOLD = 22;
const HOT_THRESHOLD = 28;
const VERY_HOT_THRESHOLD = 34;

export function getTemperatureBarPercent(temp: number): number {
  const clamped = Math.max(TEMP_SCALE_MIN, Math.min(TEMP_SCALE_MAX, temp));
  return ((clamped - TEMP_SCALE_MIN) / (TEMP_SCALE_MAX - TEMP_SCALE_MIN)) * 100;
}

export function getTemperatureColor(temp: number): string {
  if (temp < COOL_THRESHOLD) return 'bg-sky-400';
  if (temp < WARM_THRESHOLD) return 'bg-emerald-400';
  if (temp < HOT_THRESHOLD) return 'bg-amber-400';
  if (temp < VERY_HOT_THRESHOLD) return 'bg-orange-500';
  return 'bg-red-500';
}
