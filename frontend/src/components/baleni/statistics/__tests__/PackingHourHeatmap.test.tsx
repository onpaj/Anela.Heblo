import React from "react";
import { render } from "@testing-library/react";
import PackingHourHeatmap from "../PackingHourHeatmap";
import { GRAPHITE } from "../../../common/reactSelectDarkStyles";
import { HourBucket } from "../../../../api/hooks/usePackingStatistics";

jest.mock("../../../../contexts/ThemeContext", () => ({
  useTheme: jest.fn(),
}));

const { useTheme } = jest.requireMock("../../../../contexts/ThemeContext") as {
  useTheme: jest.Mock;
};

// Single occupied cell (Monday 10:00, max count => full intensity) so the
// remaining Monday-10:00 column cells across the other six weekdays are
// exercised as empty (count === 0) cells for the same rendered hour.
const buildData = (): HourBucket[] => [{ dayOfWeek: 1, hour: 10, packageCount: 5 }];

describe("PackingHourHeatmap theme-aware cell colors", () => {
  it("renders light-mode occupied and empty cell colors", () => {
    useTheme.mockReturnValue({ theme: "light", toggle: jest.fn() });
    const { container } = render(<PackingHourHeatmap data={buildData()} />);

    const occupied = container.querySelector('td[title="Po 10:00 — 5 balíků"]');
    expect(occupied).toHaveStyle({ backgroundColor: "rgba(37, 99, 235, 1)" });

    const empty = container.querySelector('td[title="Út 10:00 — 0 balíků"]');
    expect(empty).toHaveStyle({ backgroundColor: "rgb(241, 245, 249)" });
  });

  it("switches to Graphite/cyan colors in dark mode", () => {
    useTheme.mockReturnValue({ theme: "dark", toggle: jest.fn() });
    const { container } = render(<PackingHourHeatmap data={buildData()} />);

    const occupied = container.querySelector('td[title="Po 10:00 — 5 balíků"]');
    expect(occupied).toHaveStyle({ backgroundColor: "rgba(56, 189, 248, 1)" });

    const empty = container.querySelector('td[title="Út 10:00 — 0 balíků"]');
    expect(empty).toHaveStyle({ backgroundColor: GRAPHITE.surface2 });
  });
});
