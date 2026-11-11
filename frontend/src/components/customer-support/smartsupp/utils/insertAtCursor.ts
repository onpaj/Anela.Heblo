export function insertAtCursor(
  value: string,
  start: number,
  end: number,
  insert: string
): { value: string; caret: number } {
  const from = Math.max(0, Math.min(start, end, value.length));
  const to = Math.min(value.length, Math.max(start, end));
  return {
    value: value.slice(0, from) + insert + value.slice(to),
    caret: from + insert.length,
  };
}
