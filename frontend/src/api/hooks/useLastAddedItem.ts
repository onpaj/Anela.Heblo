import { useState, useEffect } from "react";

export interface LastAddedItem {
  productCode: string;
  productName: string;
  amount: number;
  timestamp: number;
}

const STORAGE_KEY = "transport-box-last-added-item";
const MAX_AGE_HOURS = 1; // Last item expires after 24 hours

export const useLastAddedItem = () => {
  const [lastAddedItem, setLastAddedItem] = useState<LastAddedItem | null>(
    null,
  );

  // Load from localStorage on mount
  useEffect(() => {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) {
      try {
        const parsed: LastAddedItem = JSON.parse(stored);
        const ageHours = (Date.now() - parsed.timestamp) / (1000 * 60 * 60);

        if (ageHours < MAX_AGE_HOURS) {
          setLastAddedItem(parsed);
        } else {
          // Remove expired item
          localStorage.removeItem(STORAGE_KEY);
        }
      } catch (error) {
        console.warn(
          "Failed to parse last added item from localStorage:",
          error,
        );
        localStorage.removeItem(STORAGE_KEY);
      }
    }
  }, []);

  const saveLastAddedItem = (item: Omit<LastAddedItem, "timestamp">) => {
    const itemWithTimestamp: LastAddedItem = {
      ...item,
      timestamp: Date.now(),
    };

    setLastAddedItem(itemWithTimestamp);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(itemWithTimestamp));
  };

  const clearLastAddedItem = () => {
    setLastAddedItem(null);
    localStorage.removeItem(STORAGE_KEY);
  };

  return {
    lastAddedItem,
    saveLastAddedItem,
    clearLastAddedItem,
  };
};
