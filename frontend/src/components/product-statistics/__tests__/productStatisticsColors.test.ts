import {
  assignSeriesColors,
  getSeriesColor,
  SERIES_COLOR_COUNT,
} from "../productStatisticsColors";

describe("assignSeriesColors", () => {
  test("assigns consecutive slots to a fresh selection", () => {
    const assigned = assignSeriesColors(["A", "B", "C"], new Map());

    expect(assigned.get("A")).toBe(0);
    expect(assigned.get("B")).toBe(1);
    expect(assigned.get("C")).toBe(2);
  });

  test("keeps the survivors' colors when a product is removed", () => {
    // The whole point: removing A must not turn B blue and C green mid-analysis.
    const initial = assignSeriesColors(["A", "B", "C"], new Map());
    const afterRemoval = assignSeriesColors(["B", "C"], initial);

    expect(afterRemoval.get("B")).toBe(initial.get("B"));
    expect(afterRemoval.get("C")).toBe(initial.get("C"));
  });

  test("gives a newcomer the slot freed by a removal", () => {
    const initial = assignSeriesColors(["A", "B"], new Map());
    const afterSwap = assignSeriesColors(["B", "D"], initial);

    expect(afterSwap.get("B")).toBe(1);
    expect(afterSwap.get("D")).toBe(0);
  });

  test("does not mutate the previous assignment", () => {
    const initial = assignSeriesColors(["A", "B"], new Map());
    const snapshot = new Map(initial);

    assignSeriesColors(["B", "C"], initial);

    expect([...initial.entries()]).toEqual([...snapshot.entries()]);
  });

  test("assigns a distinct slot to every product up to the palette size", () => {
    const codes = Array.from({ length: SERIES_COLOR_COUNT }, (_, i) => `P${i}`);

    const assigned = assignSeriesColors(codes, new Map());

    expect(new Set(assigned.values()).size).toBe(SERIES_COLOR_COUNT);
  });

  test("wraps around when there are more products than palette slots", () => {
    const codes = Array.from(
      { length: SERIES_COLOR_COUNT + 2 },
      (_, i) => `P${i}`,
    );

    const assigned = assignSeriesColors(codes, new Map());

    expect(assigned.size).toBe(codes.length);
    for (const slot of assigned.values()) {
      expect(slot).toBeGreaterThanOrEqual(0);
      expect(slot).toBeLessThan(SERIES_COLOR_COUNT);
    }
  });

  test("getSeriesColor wraps past the end of the palette", () => {
    expect(getSeriesColor(SERIES_COLOR_COUNT)).toEqual(getSeriesColor(0));
  });
});
