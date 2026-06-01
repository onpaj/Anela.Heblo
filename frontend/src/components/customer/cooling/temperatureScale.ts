export const TEMP_SCALE_MIN = 0;
export const TEMP_SCALE_MAX = 40;

export const COOL_THRESHOLD = 10;
export const WARM_THRESHOLD = 16;
export const HOT_THRESHOLD = 21;
export const VERY_HOT_THRESHOLD = 26;

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

export interface TemperatureRangeBar {
  left: number;
  width: number;
}

export function getTemperatureRangeBar(min: number, max: number): TemperatureRangeBar {
  const safeMax = Math.max(min, max);
  const left = getTemperatureBarPercent(min);
  const right = getTemperatureBarPercent(safeMax);
  return { left, width: right - left };
}
