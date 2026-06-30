import React from "react";
import { render, screen } from "@testing-library/react";
import { PackingStatisticsResponse } from "../../../../api/hooks/usePackingStatistics";
import BaleniStatistics from "../BaleniStatistics";

jest.mock("../../../../telemetry/useScreenView", () => ({
  useScreenView: jest.fn(),
}));

const mockUsePackingStatistics = jest.fn();
jest.mock("../../../../api/hooks/usePackingStatistics", () => ({
  ...jest.requireActual("../../../../api/hooks/usePackingStatistics"),
  usePackingStatistics: (params: unknown) => mockUsePackingStatistics(params),
}));

const sampleData: PackingStatisticsResponse = {
  fromDate: "2026-05-27",
  toDate: "2026-06-25",
  packerAttributionSince: "2026-06-09",
  summary: {
    totalPackages: 120,
    totalOrders: 80,
    distinctPackers: 3,
    averagePackagesPerOrder: 1.5,
    trackingCoveragePercent: 92.5,
    busiestDay: { date: "2026-06-10", orderCount: 12, packageCount: 18 },
    busiestHour: { dayOfWeek: 3, hour: 10, packageCount: 9 },
  },
  throughputDaily: [{ date: "2026-06-10", orderCount: 12, packageCount: 18 }],
  hourHeatmap: [{ dayOfWeek: 3, hour: 10, packageCount: 9 }],
  byPacker: [{ packerId: "p1", packerName: "Alice", orderCount: 40, packageCount: 60 }],
  byCarrier: [{ code: "DPD", name: "DPD", packageCount: 120 }],
  packagesPerOrder: [{ packageCount: 1, orderCount: 50 }],
};

const setHookResult = (overrides: Record<string, unknown>) => {
  mockUsePackingStatistics.mockReturnValue({
    data: undefined,
    isLoading: false,
    error: null,
    refetch: jest.fn(),
    isFetching: false,
    ...overrides,
  });
};

describe("BaleniStatistics", () => {
  beforeEach(() => mockUsePackingStatistics.mockReset());

  it("shows a loading state while fetching", () => {
    setHookResult({ isLoading: true });
    render(<BaleniStatistics />);
    expect(screen.getByText("Načítání dat...")).toBeInTheDocument();
  });

  it("shows an error state with a retry button", () => {
    setHookResult({ error: new Error("boom") });
    render(<BaleniStatistics />);
    expect(screen.getByText("Chyba při načítání statistik")).toBeInTheDocument();
    expect(screen.getByText("Zkusit znovu")).toBeInTheDocument();
  });

  it("renders KPI values and all panels when data is present", () => {
    setHookResult({ data: sampleData });
    render(<BaleniStatistics />);

    expect(screen.getByTestId("baleni-statistics")).toBeInTheDocument();
    expect(screen.getByText("120")).toBeInTheDocument(); // total packages
    expect(screen.getByText("80")).toBeInTheDocument(); // total orders
    expect(screen.getByText("92.5 %")).toBeInTheDocument(); // tracking coverage
    expect(screen.getByText("Průběh balení v čase")).toBeInTheDocument();
    expect(screen.getByText("Vytížení podle hodin")).toBeInTheDocument();
    expect(screen.getByTestId("packing-hour-heatmap")).toBeInTheDocument();
    expect(screen.getByText("Dopravci")).toBeInTheDocument();
  });

  it("shows the packer attribution hint when attribution starts after the window", () => {
    setHookResult({ data: sampleData });
    render(<BaleniStatistics />);
    expect(screen.getByText(/Evidence baličů je dostupná až od/)).toBeInTheDocument();
  });

  it("omits the attribution hint when attribution covers the whole window", () => {
    setHookResult({
      data: { ...sampleData, packerAttributionSince: "2026-05-20" },
    });
    render(<BaleniStatistics />);
    expect(screen.queryByText(/Evidence baličů je dostupná až od/)).not.toBeInTheDocument();
  });
});
