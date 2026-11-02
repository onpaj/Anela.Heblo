import { useCallback, useState } from "react";

export interface NumberBounds {
  min?: number;
  max?: number;
}

const isWithinBounds = (value: number, { min = -Infinity, max = Infinity }: NumberBounds): boolean =>
  Number.isFinite(value) && value >= min && value <= max;

const readStoredNumber = (key: string, fallback: number, bounds: NumberBounds): number => {
  try {
    const raw = window.localStorage.getItem(key);
    if (raw === null) return fallback;
    const parsed = Number(raw);
    return isWithinBounds(parsed, bounds) ? parsed : fallback;
  } catch {
    return fallback;
  }
};

/** useState for a number that is remembered in the browser's localStorage. */
export const usePersistedNumber = (
  key: string,
  defaultValue: number,
  bounds: NumberBounds = {}
): [number, (value: number) => void] => {
  const [value, setValue] = useState<number>(() => readStoredNumber(key, defaultValue, bounds));

  const update = useCallback(
    (next: number) => {
      setValue(next);
      try {
        window.localStorage.setItem(key, String(next));
      } catch {
        // storage unavailable (private mode, blocked) - value still works for this session
      }
    },
    [key]
  );

  return [value, update];
};
