/**
 * Fixed palette for multi-product chart series. Colors are held per product code rather
 * than per position: removing a product must not recolor the ones left behind, and the
 * backend silently skips codes it cannot resolve, which shifts every position after the gap.
 */
const SERIES_COLORS: ReadonlyArray<{ border: string; background: string }> = [
  { border: "rgba(59, 130, 246, 1)", background: "rgba(59, 130, 246, 0.15)" }, // blue
  { border: "rgba(34, 197, 94, 1)", background: "rgba(34, 197, 94, 0.15)" }, // green
  { border: "rgba(168, 85, 247, 1)", background: "rgba(168, 85, 247, 0.15)" }, // purple
  { border: "rgba(251, 146, 60, 1)", background: "rgba(251, 146, 60, 0.15)" }, // orange
  { border: "rgba(236, 72, 153, 1)", background: "rgba(236, 72, 153, 0.15)" }, // pink
  { border: "rgba(20, 184, 166, 1)", background: "rgba(20, 184, 166, 0.15)" }, // teal
  { border: "rgba(234, 179, 8, 1)", background: "rgba(234, 179, 8, 0.15)" }, // yellow
  { border: "rgba(99, 102, 241, 1)", background: "rgba(99, 102, 241, 0.15)" }, // indigo
  { border: "rgba(239, 68, 68, 1)", background: "rgba(239, 68, 68, 0.15)" }, // red
  { border: "rgba(107, 114, 128, 1)", background: "rgba(107, 114, 128, 0.15)" }, // gray
];

export const SERIES_COLOR_COUNT = SERIES_COLORS.length;

export function getSeriesColor(index: number): {
  border: string;
  background: string;
} {
  return SERIES_COLORS[index % SERIES_COLORS.length];
}

/**
 * Reassigns palette slots for the current selection, keeping every product that survived
 * on the color it already had and giving each newcomer the lowest slot nobody holds.
 * Returns a new map — the previous one is never mutated.
 */
export function assignSeriesColors(
  productCodes: readonly string[],
  previous: ReadonlyMap<string, number>,
): Map<string, number> {
  const assigned = new Map<string, number>();
  const taken = new Set<number>();

  for (const code of productCodes) {
    const existing = previous.get(code);
    if (existing !== undefined && !taken.has(existing)) {
      assigned.set(code, existing);
      taken.add(existing);
    }
  }

  let nextFree = 0;
  for (const code of productCodes) {
    if (assigned.has(code)) {
      continue;
    }
    while (taken.has(nextFree % SERIES_COLOR_COUNT) && taken.size < SERIES_COLOR_COUNT) {
      nextFree += 1;
    }
    const slot = nextFree % SERIES_COLOR_COUNT;
    assigned.set(code, slot);
    taken.add(slot);
    nextFree += 1;
  }

  return assigned;
}
