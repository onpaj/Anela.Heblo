import { CarrierMix } from "../../../../api/hooks/usePackingStatistics";
import { buildCarrierSlices, MAX_CARRIERS, OTHER_KEY, OTHER_LABEL } from "../PackingCharts";

const carrier = (code: string, name: string, packageCount: number): CarrierMix => ({
  code,
  name,
  packageCount,
});

describe("buildCarrierSlices", () => {
  it("returns an empty array for empty input", () => {
    expect(buildCarrierSlices([])).toEqual([]);
  });

  it("merges entries that share the same display name", () => {
    const result = buildCarrierSlices([
      carrier("ZAS-BOX", "Zásilkovna", 10),
      carrier("ZAS-PP", "Zásilkovna", 5),
      carrier("DPD", "DPD", 3),
    ]);

    expect(result).toHaveLength(2);
    const zasilkovna = result.find((s) => s.name === "Zásilkovna");
    expect(zasilkovna?.packageCount).toBe(15);
    expect(zasilkovna?.key).toBe("Zásilkovna");
  });

  it("does not mutate the input array or its elements", () => {
    const input = [carrier("DPD", "DPD", 3)];
    const snapshot = JSON.parse(JSON.stringify(input));
    buildCarrierSlices(input);
    expect(input).toEqual(snapshot);
  });

  it("sorts slices descending by packageCount", () => {
    const result = buildCarrierSlices([
      carrier("A", "A", 1),
      carrier("B", "B", 9),
      carrier("C", "C", 4),
    ]);
    expect(result.map((s) => s.name)).toEqual(["B", "C", "A"]);
  });

  it("keeps top MAX_CARRIERS and rolls the rest into an Ostatní bucket", () => {
    // 8 distinct names (strictly descending) -> 6 kept + 1 bucket summing the last 2.
    const input = Array.from({ length: 8 }, (_, i) =>
      carrier(`C${i}`, `Carrier ${i}`, 100 - i),
    );
    const result = buildCarrierSlices(input);

    expect(result).toHaveLength(MAX_CARRIERS + 1);
    const bucket = result[result.length - 1];
    expect(bucket.name).toBe(OTHER_LABEL);
    expect(bucket.key).toBe(OTHER_KEY);
    // last two carriers: (100-6) + (100-7) = 94 + 93
    expect(bucket.packageCount).toBe(187);
  });

  it("adds no bucket when there are MAX_CARRIERS or fewer distinct names", () => {
    const input = Array.from({ length: MAX_CARRIERS }, (_, i) =>
      carrier(`C${i}`, `Carrier ${i}`, 10 + i),
    );
    const result = buildCarrierSlices(input);

    expect(result).toHaveLength(MAX_CARRIERS);
    expect(result.some((s) => s.name === OTHER_LABEL)).toBe(false);
  });

  it("uses a sentinel key for the bucket even when a carrier is named Ostatní", () => {
    // A carrier named exactly OTHER_LABEL should never produce a key collision with the bucket.
    const input = Array.from({ length: MAX_CARRIERS + 1 }, (_, i) =>
      carrier(`C${i}`, i === 0 ? OTHER_LABEL : `Carrier ${i}`, 100 - i),
    );
    const result = buildCarrierSlices(input);

    const keys = result.map((s) => s.key);
    expect(new Set(keys).size).toBe(keys.length); // all keys are unique
    const bucket = result[result.length - 1];
    expect(bucket.key).toBe(OTHER_KEY);
    expect(bucket.name).toBe(OTHER_LABEL);
  });

  it("merges first, then buckets (dedup can drop below the threshold)", () => {
    // 8 rows but only 6 distinct names -> no bucket.
    const result = buildCarrierSlices([
      carrier("A1", "A", 5),
      carrier("A2", "A", 5),
      carrier("B", "B", 4),
      carrier("C", "C", 3),
      carrier("D", "D", 2),
      carrier("E", "E", 1),
      carrier("F1", "F", 1),
      carrier("F2", "F", 1),
    ]);

    expect(result).toHaveLength(6);
    expect(result.some((s) => s.name === OTHER_LABEL)).toBe(false);
  });
});
