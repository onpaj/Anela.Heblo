import { useState, useCallback } from "react";

interface SelectionRange {
  from: string; // "YYYY-MM-DD", always <= to
  to: string;
}

export function useCreateByDrag(
  onCreateRange: (dateFrom: string, dateTo: string) => void,
) {
  const [startDate, setStartDate] = useState<string | null>(null);
  const [selectionRange, setSelectionRange] = useState<SelectionRange | null>(null);

  const sortDates = (a: string, b: string): [string, string] =>
    a <= b ? [a, b] : [b, a];

  const handleMouseDown = useCallback((dateStr: string) => {
    setStartDate(dateStr);
    setSelectionRange({ from: dateStr, to: dateStr });
  }, []);

  const handleMouseEnter = useCallback(
    (dateStr: string) => {
      if (!startDate) return;
      const [from, to] = sortDates(startDate, dateStr);
      setSelectionRange({ from, to });
    },
    [startDate],
  );

  const handleMouseUp = useCallback(() => {
    if (startDate && selectionRange) {
      onCreateRange(selectionRange.from, selectionRange.to);
    }
    setStartDate(null);
    setSelectionRange(null);
  }, [startDate, selectionRange, onCreateRange]);

  return { selectionRange, handleMouseDown, handleMouseEnter, handleMouseUp };
}
