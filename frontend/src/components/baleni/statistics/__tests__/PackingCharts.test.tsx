import React from "react";
import { render, waitFor } from "@testing-library/react";
import { CarrierMix } from "../../../../api/hooks/usePackingStatistics";
import {
  buildCarrierSlices,
  MAX_CARRIERS,
  OTHER_KEY,
  OTHER_LABEL,
  ThroughputChart,
  CarrierMixChart,
  PackerLeaderboard,
} from "../PackingCharts";
import { GRAPHITE } from "../../../common/reactSelectDarkStyles";

// jsdom reports 0 width/height for the container, so recharts' ResponsiveContainer
// never renders its children. Bypass it with a fixed-size wrapper so the SVG
// primitives under test actually mount. Mirrors BankStatementImportChart.test.tsx.
jest.mock("recharts", () => {
  const actual = jest.requireActual("recharts");
  const ReactLib = require("react");
  return {
    ...actual,
    ResponsiveContainer: ({ children }: any) =>
      ReactLib.createElement(
        "div",
        { style: { width: 800, height: 400 } },
        ReactLib.cloneElement(children, { width: 800, height: 400 }),
      ),
  };
});

jest.mock("../../../../contexts/ThemeContext", () => ({
  useTheme: jest.fn(),
}));

const { useTheme } = jest.requireMock("../../../../contexts/ThemeContext") as {
  useTheme: jest.Mock;
};

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

describe("ThroughputChart theme-aware colors", () => {
  const data = [{ date: "2026-07-17", orderCount: 3, packageCount: 6 }];

  it("renders light-mode colors when theme is light", async () => {
    useTheme.mockReturnValue({ theme: "light", toggle: jest.fn() });
    const { container } = render(<ThroughputChart data={data} />);

    expect(
      container.querySelector(".recharts-cartesian-grid-horizontal line")?.getAttribute("stroke"),
    ).toBe("#f0f0f0");
    expect(
      container.querySelector(".recharts-xAxis .recharts-cartesian-axis-line")?.getAttribute("stroke"),
    ).toBe("#6b7280");
    expect(container.querySelector(".recharts-default-tooltip")).toHaveStyle({
      backgroundColor: "rgb(255, 255, 255)",
      border: "1px solid #ccc",
    });

    await waitFor(() =>
      expect(container.querySelector(".recharts-rectangle")).not.toBeNull(),
    );
    expect(container.querySelector(".recharts-rectangle")?.getAttribute("fill")).toBe("#2563eb");
  });

  it("switches to dark-mode colors when theme is dark", async () => {
    useTheme.mockReturnValue({ theme: "dark", toggle: jest.fn() });
    const { container } = render(<ThroughputChart data={data} />);

    expect(
      container.querySelector(".recharts-cartesian-grid-horizontal line")?.getAttribute("stroke"),
    ).toBe(GRAPHITE.border);
    expect(
      container.querySelector(".recharts-xAxis .recharts-cartesian-axis-line")?.getAttribute("stroke"),
    ).toBe(GRAPHITE.muted);
    expect(container.querySelector(".recharts-default-tooltip")).toHaveStyle({
      backgroundColor: "rgb(32, 35, 39)",
      border: `1px solid ${GRAPHITE.border.toLowerCase()}`,
    });
    expect(container.querySelector(".recharts-tooltip-label")).toHaveStyle({
      color: "rgb(154, 160, 170)",
    });

    await waitFor(() =>
      expect(container.querySelector(".recharts-rectangle")).not.toBeNull(),
    );
    expect(container.querySelector(".recharts-rectangle")?.getAttribute("fill")).toBe(
      GRAPHITE.accent,
    );
  });
});

describe("CarrierMixChart theme-aware tooltip", () => {
  const data: CarrierMix[] = [{ code: "DPD", name: "DPD", packageCount: 3 }];

  it("uses the plain recharts tooltip style in light mode", () => {
    useTheme.mockReturnValue({ theme: "light", toggle: jest.fn() });
    const { container } = render(<CarrierMixChart data={data} />);

    expect(container.querySelector(".recharts-default-tooltip")).toHaveStyle({
      backgroundColor: "rgb(255, 255, 255)",
      border: "1px solid #ccc",
    });
  });

  it("uses Graphite tooltip colors in dark mode", () => {
    useTheme.mockReturnValue({ theme: "dark", toggle: jest.fn() });
    const { container } = render(<CarrierMixChart data={data} />);

    expect(container.querySelector(".recharts-default-tooltip")).toHaveStyle({
      backgroundColor: "rgb(32, 35, 39)",
      border: `1px solid ${GRAPHITE.border.toLowerCase()}`,
    });
    expect(container.querySelector(".recharts-tooltip-label")).toHaveStyle({
      color: "rgb(154, 160, 170)",
    });
  });
});

describe("PackerLeaderboard theme-aware colors", () => {
  const data = [{ packerId: "1", packerName: "Petr", orderCount: 3, packageCount: 6 }];

  it("renders light-mode bar/grid/axis colors", async () => {
    useTheme.mockReturnValue({ theme: "light", toggle: jest.fn() });
    const { container } = render(<PackerLeaderboard data={data} />);

    expect(
      container.querySelector(".recharts-cartesian-grid-vertical line")?.getAttribute("stroke"),
    ).toBe("#f0f0f0");
    expect(
      container.querySelector(".recharts-xAxis .recharts-cartesian-axis-line")?.getAttribute("stroke"),
    ).toBe("#6b7280");

    await waitFor(() =>
      expect(container.querySelector(".recharts-rectangle")).not.toBeNull(),
    );
    expect(container.querySelector(".recharts-rectangle")?.getAttribute("fill")).toBe("#2563eb");
  });

  it("switches to Graphite bar/grid/axis colors in dark mode", async () => {
    useTheme.mockReturnValue({ theme: "dark", toggle: jest.fn() });
    const { container } = render(<PackerLeaderboard data={data} />);

    expect(
      container.querySelector(".recharts-cartesian-grid-vertical line")?.getAttribute("stroke"),
    ).toBe(GRAPHITE.border);
    expect(
      container.querySelector(".recharts-xAxis .recharts-cartesian-axis-line")?.getAttribute("stroke"),
    ).toBe(GRAPHITE.muted);

    await waitFor(() =>
      expect(container.querySelector(".recharts-rectangle")).not.toBeNull(),
    );
    expect(container.querySelector(".recharts-rectangle")?.getAttribute("fill")).toBe(
      GRAPHITE.accent,
    );
  });
});
