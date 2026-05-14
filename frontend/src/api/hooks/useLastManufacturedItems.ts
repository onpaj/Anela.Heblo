import { useState, useEffect } from "react";

export interface LastManufacturedEntry {
  productCode: string;
  productName: string;
  lotNumber?: string;
  expirationDate?: string;
  addedAmount: number;
  timestamp: number;
}

const STORAGE_KEY = "transport-box-last-manufactured-items";
const MAX_AGE_HOURS = 24;

export const useLastManufacturedItems = () => {
  const [items, setItems] = useState<LastManufacturedEntry[]>([]);

  useEffect(() => {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (!stored) return;
    try {
      const parsed: LastManufacturedEntry[] = JSON.parse(stored);
      const now = Date.now();
      const fresh = parsed.filter(
        (e) => (now - e.timestamp) / (1000 * 60 * 60) < MAX_AGE_HOURS,
      );
      if (fresh.length > 0) {
        setItems(fresh);
      } else {
        localStorage.removeItem(STORAGE_KEY);
      }
    } catch {
      localStorage.removeItem(STORAGE_KEY);
    }
  }, []);

  const saveItem = (entry: Omit<LastManufacturedEntry, "timestamp">) => {
    const key = `${entry.productCode}|${entry.lotNumber ?? ""}`;
    setItems((prev) => {
      const filtered = prev.filter(
        (e) => `${e.productCode}|${e.lotNumber ?? ""}` !== key,
      );
      const updated = [...filtered, { ...entry, timestamp: Date.now() }];
      localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
      return updated;
    });
  };

  return { lastManufacturedItems: items, saveLastManufacturedItem: saveItem };
};
