import { useCallback, useState } from "react";

const readStoredNumber = (key: string, fallback: number): number => {
  try {
    const raw = window.localStorage.getItem(key);
    if (raw === null) return fallback;
    const parsed = Number(raw);
    return Number.isFinite(parsed) ? parsed : fallback;
  } catch {
    return fallback;
  }
};

/** useState for a number that is remembered in the browser's localStorage. */
export const usePersistedNumber = (
  key: string,
  defaultValue: number
): [number, (value: number) => void] => {
  const [value, setValue] = useState<number>(() => readStoredNumber(key, defaultValue));

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
