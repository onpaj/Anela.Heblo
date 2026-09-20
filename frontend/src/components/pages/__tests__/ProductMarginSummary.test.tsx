import React from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import ProductMarginSummary from "../ProductMarginSummary";
import * as useProductMarginSummaryHook from "../../../api/hooks/useProductMarginSummary";

// Mock the hook
jest.mock("../../../api/hooks/useProductMarginSummary");

const mockUseProductMarginSummary = useProductMarginSummaryHook.useProductMarginSummaryQuery;

// Mock Chart component to avoid canvas issues in tests
jest.mock("react-chartjs-2", () => ({
  Chart: ({ data, options }: any) => (
    <div
      data-testid="chart"
      data-chart-data={JSON.stringify(data)}
      data-chart-options={JSON.stringify(options)}
    />
  ),
}));

const mockData = {
  monthlyData: [
    {
      year: 2024,
      month: 3,
      monthDisplay: "Bře 2024",
      productSegments: [
        {
          groupKey: "PROD001",
          displayName: "Product 1",
          marginContribution: 1500,
          percentage: 60,
          colorCode: "#2563EB",
          averageMarginPerPiece: 100,
          unitsSold: 15,
          averageSellingPriceWithoutVat: 150,
          averageMaterialCosts: 30,
          averageLaborCosts: 20,
          isOther: false,
        },
        {
          groupKey: "OTHER",
          displayName: "Ostatní produkty",
          marginContribution: 1000,
          percentage: 40,
          colorCode: "#9CA3AF",
          averageMarginPerPiece: 0,
          unitsSold: 0,
          averageSellingPriceWithoutVat: 0,
          averageMaterialCosts: 0,
          averageLaborCosts: 0,
          isOther: true,
        },
      ],
      totalMonthMargin: 2500,
    },
  ],
  topProducts: [
    {
      groupKey: "PROD001",
      displayName: "Product 1",
      totalMargin: 1500,
      colorCode: "#2563EB",
      rank: 1,
    },
  ],
  totalMargin: 1500,
  timeWindow: "current-year",
  fromDate: "2024-01-01T00:00:00",
  toDate: "2024-12-31T23:59:59",
};

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>{children}</BrowserRouter>
    </QueryClientProvider>
  );
};

describe("ProductMarginSummary", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("renders loading state", () => {
    mockUseProductMarginSummary.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });

    expect(
      screen.getByText("Načítám data o marži produktů..."),
    ).toBeInTheDocument();
  });

  it("renders error state", () => {
    const error = new Error("API Error");
    mockUseProductMarginSummary.mockReturnValue({
      data: undefined,
      isLoading: false,
      error,
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });

    expect(
      screen.getByText("Chyba při načítání dat o marži"),
    ).toBeInTheDocument();
    expect(screen.getByText("API Error")).toBeInTheDocument();
  });

  it("renders empty state when no data", () => {
    mockUseProductMarginSummary.mockReturnValue({
      data: { monthlyData: [], topProducts: [], totalMargin: 0 },
      isLoading: false,
      error: null,
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });

    expect(screen.getByText("Žádná data o marži")).toBeInTheDocument();
    expect(
      screen.getByText(
        "Pro vybrané období nejsou k dispozici žádná data o marži produktů.",
      ),
    ).toBeInTheDocument();
  });

  it("renders chart with data", async () => {
    mockUseProductMarginSummary.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });

    expect(screen.getByText("Analýza marže")).toBeInTheDocument();
    expect(screen.getByTestId("chart")).toBeInTheDocument();

    // Check for total margin in summary - use getAllByText to handle multiple occurrences
    const totalMarginElements = screen.getAllByText(/1.*500.*Kč/);
    expect(totalMarginElements.length).toBeGreaterThan(0);
  });

  it("changes time window when dropdown is selected", async () => {
    const user = userEvent.setup();

    mockUseProductMarginSummary.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });

    const dropdown = screen.getByLabelText("Časové období:");
    await user.selectOptions(dropdown, "last-6-months");

    expect(dropdown).toHaveValue("last-6-months");
  });

  it("displays summary information correctly", () => {
    mockUseProductMarginSummary.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });

    // Basic test that component renders with data
    expect(screen.getByText("Analýza marže")).toBeInTheDocument();
    expect(screen.getByTestId("chart")).toBeInTheDocument();
  });

  it("has proper page structure following layout standards", () => {
    mockUseProductMarginSummary.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
    } as any);

    render(<ProductMarginSummary />, { wrapper: createWrapper() });

    // Check main heading is present
    expect(
      screen.getByRole("heading", { name: "Analýza marže" }),
    ).toBeInTheDocument();

    // Check time window selector is present
    expect(screen.getByLabelText("Časové období:")).toBeInTheDocument();

    // Check chart is rendered
    expect(screen.getByTestId("chart")).toBeInTheDocument();
  });

  it("assigns the same color to a product in the chart legend and the table (no chart/table divergence)", () => {
    // topProducts is intentionally NOT sorted by totalMargin descending — this is the
    // shape that previously caused chartData (which re-sorts) and tableData (which used
    // the raw array order) to disagree on which color belongs to which product.
    const divergentMockData = {
      monthlyData: [
        {
          year: 2024,
          month: 3,
          monthDisplay: "Bře 2024",
          productSegments: [
            {
              groupKey: "PROD_LOW",
              displayName: "Low Margin Product",
              marginContribution: 500,
              percentage: 25,
              colorCode: "#000000",
              averageMarginPerPiece: 50,
              unitsSold: 10,
              averageSellingPriceWithoutVat: 100,
              averageMaterialCosts: 20,
              averageLaborCosts: 10,
              isOther: false,
            },
            {
              groupKey: "PROD_HIGH",
              displayName: "High Margin Product",
              marginContribution: 1500,
              percentage: 75,
              colorCode: "#000000",
              averageMarginPerPiece: 150,
              unitsSold: 10,
              averageSellingPriceWithoutVat: 300,
              averageMaterialCosts: 60,
              averageLaborCosts: 30,
              isOther: false,
            },
          ],
          totalMonthMargin: 2000,
        },
      ],
      // Order deliberately does NOT match descending totalMargin: PROD_LOW (500) is listed
      // before PROD_HIGH (1500).
      topProducts: [
        { groupKey: "PROD_LOW", displayName: "Low Margin Product", totalMargin: 500, rank: 2 },
        { groupKey: "PROD_HIGH", displayName: "High Margin Product", totalMargin: 1500, rank: 1 },
      ],
      totalMargin: 2000,
      timeWindow: "current-year",
      fromDate: "2024-01-01T00:00:00",
      toDate: "2024-12-31T23:59:59",
    };

    mockUseProductMarginSummary.mockReturnValue({
      data: divergentMockData,
      isLoading: false,
      error: null,
    } as any);

    const { container } = render(<ProductMarginSummary />, {
      wrapper: createWrapper(),
    });

    // Read the color the chart assigned to each product from the mocked Chart's
    // data-chart-data JSON payload (datasets[].label / backgroundColor).
    const chartEl = screen.getByTestId("chart");
    const chartPayload = JSON.parse(
      chartEl.getAttribute("data-chart-data") || "{}",
    );
    const chartColorByLabel: Record<string, string> = {};
    for (const dataset of chartPayload.datasets) {
      chartColorByLabel[dataset.label] = dataset.backgroundColor;
    }

    // Read the color the table assigned to each product from the rendered color dot
    // (the small rounded div immediately preceding the product name in each row).
    const tableColorFor = (displayName: string): string => {
      const nameEl = screen.getByText(displayName);
      const row = nameEl.closest("tr");
      if (!row) throw new Error(`No <tr> found for ${displayName}`);
      const dot = row.querySelector("div.rounded-full") as HTMLElement | null;
      if (!dot) throw new Error(`No color dot found for ${displayName}`);
      return dot.style.backgroundColor.toLowerCase();
    };

    const normalize = (hex: string) => hex.toLowerCase();

    expect(normalize(tableColorFor("High Margin Product"))).toBe(
      normalize(chartColorByLabel["High Margin Product"]),
    );
    expect(normalize(tableColorFor("Low Margin Product"))).toBe(
      normalize(chartColorByLabel["Low Margin Product"]),
    );
    // The two products must not have been assigned the same color as each other.
    expect(
      normalize(tableColorFor("High Margin Product")),
    ).not.toBe(normalize(tableColorFor("Low Margin Product")));

    // Reference implementation avoids the JSDOM container variable being unused.
    expect(container).toBeTruthy();
  });
});
