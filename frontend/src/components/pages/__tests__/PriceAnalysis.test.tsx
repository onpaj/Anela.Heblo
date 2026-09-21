import React from "react";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import PriceAnalysis from "../PriceAnalysis";
import * as usePricingSimulatorHook from "../../../api/hooks/usePricingSimulator";
import { PricingRowDto, PricingTotalsDto } from "../../../api/generated/api-client";
import { formatCurrency, formatPercentage } from "../../../utils/formatters";

// Mock the pricing simulator hooks. PriceAnalysis (Task 8) only consumes
// usePricingBaselineQuery; the other exports are auto-mocked as jest.fn()
// and are not exercised until Task 9.
jest.mock("../../../api/hooks/usePricingSimulator");

const mockUsePricingBaselineQuery =
  usePricingSimulatorHook.usePricingBaselineQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingBaselineQuery
  >;

// Plain object literals, not `new PricingRowDto(...)`: the generated client's
// Response subclasses lose fields passed to their constructor (Babel's class-field
// semantics re-initialize declared fields to `undefined` right after `super()`
// returns, overwriting what BaseResponse's constructor just copied from `data`).
// Test fixtures across this codebase use plain objects for exactly this reason
// (see ProductMarginsList.test.tsx's mockData).
const buildRow = (overrides: Partial<PricingRowDto> = {}): PricingRowDto =>
  ({
    productCode: "PROD001",
    productName: "Test Product 1",
    baselinePrice: 150,
    baselineMaterialCost: 30,
    baselineManufacturingCost: 20,
    baselineQuantity: 100,
    price: 150,
    materialCost: 30,
    manufacturingCost: 20,
    forecastQuantity: 100,
    m0Amount: 120,
    m0Percentage: 80,
    m1Amount: 100,
    m1Percentage: 66.67,
    isEdited: false,
    isExcluded: false,
    baselineDrifted: false,
    ...overrides,
  }) as PricingRowDto;

const buildTotals = (overrides: Partial<PricingTotalsDto> = {}): PricingTotalsDto =>
  ({
    revenueBefore: 15000,
    revenueAfter: 15000,
    revenueDelta: 0,
    revenueDeltaPercentage: 0,
    m0Before: 12000,
    m0After: 12000,
    m0Delta: 0,
    m0DeltaPercentage: 0,
    m1Before: 10000,
    m1After: 10000,
    m1Delta: 0,
    m1DeltaPercentage: 0,
    editedProductCount: 0,
    excludedProductCount: 0,
    ...overrides,
  }) as PricingTotalsDto;

const mockData = {
  rows: [
    buildRow(),
    buildRow({ productCode: "PROD002", productName: "Test Product 2" }),
  ],
  totals: buildTotals(),
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

describe("PriceAnalysis", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("shows a loading state while the query is pending", () => {
    mockUsePricingBaselineQuery.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    expect(screen.getByText(/Načítání/i)).toBeInTheDocument();
  });

  it("renders one row per product, including excluded rows (never hidden)", () => {
    mockUsePricingBaselineQuery.mockReturnValue({
      data: {
        rows: [
          buildRow(),
          buildRow({
            productCode: "PROD002",
            productName: "Test Product 2",
            isExcluded: true,
          }),
        ],
        totals: buildTotals(),
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    expect(screen.getByText("Test Product 1")).toBeInTheDocument();
    expect(screen.getByText("Test Product 2")).toBeInTheDocument();
    // 2 data rows (one excluded) + header row -- the excluded row must still
    // be counted, not dropped from the table.
    expect(screen.getAllByRole("row")).toHaveLength(2 + 1);

    // The excluded row is displayed and flagged, not hidden: it must carry
    // its explanatory title rather than disappear, so a total is never
    // silently built on partial data.
    const excludedRow = screen.getByText("Test Product 2").closest("tr");
    expect(excludedRow).not.toBeNull();
    expect(excludedRow).toHaveAttribute("title");
    expect(excludedRow?.getAttribute("title")).toMatch(/vylouč/i);
  });

  it("renders the three totals lines with distinct before, after and delta % values", () => {
    // Before/after are deliberately far apart (and every delta is non-zero) so
    // a test that would pass if the "Po:" column -- or the Δ% -- were dropped
    // entirely is impossible: each value below is unique within its line.
    const totals = buildTotals({
      revenueBefore: 2840120,
      revenueAfter: 3102400,
      revenueDelta: 262280,
      revenueDeltaPercentage: 9.23,
      m0Before: 1200550,
      m0After: 1340800,
      m0Delta: 140250,
      m0DeltaPercentage: 11.68,
      m1Before: 980300,
      m1After: 1055600,
      m1Delta: 75300,
      m1DeltaPercentage: 7.68,
    });

    mockUsePricingBaselineQuery.mockReturnValue({
      data: { rows: mockData.rows, totals },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    // Assert against text produced by the exact same formatCurrency/formatPercentage
    // helpers the component uses, rather than hand-typed strings: formatCurrency
    // renders Czech thousands separators as non-breaking spaces (U+00A0), and a
    // literal " " in a hand-written expectation would silently never match.
    const assertLine = (
      label: string,
      before: number,
      after: number,
      deltaPercentage: number,
    ) => {
      // The label span's own parent is that totals line's row container (see
      // PricingTotalsBar's TotalsLine markup), so scoping to its textContent
      // means a number appearing elsewhere on the page (another line, or the
      // grid below) cannot satisfy these assertions.
      const labelElement = screen.getByText(label);
      const lineContainer = labelElement.parentElement;
      expect(lineContainer).not.toBeNull();
      const lineText = lineContainer?.textContent ?? "";

      const beforeText = formatCurrency(before);
      const afterText = formatCurrency(after);
      const deltaPercentageText = formatPercentage(deltaPercentage);

      // Sanity: the fixture itself must exercise genuinely different values.
      expect(beforeText).not.toEqual(afterText);

      expect(lineText).toContain(beforeText);
      expect(lineText).toContain(afterText);
      expect(lineText).toContain(deltaPercentageText);
    };

    assertLine("Obrat", 2840120, 3102400, 9.23);
    assertLine("M0", 1200550, 1340800, 11.68);
    assertLine("M1", 980300, 1055600, 7.68);
  });

  it("shows the excluded-product count when excludedProductCount is greater than zero", () => {
    mockUsePricingBaselineQuery.mockReturnValue({
      data: {
        rows: mockData.rows,
        totals: buildTotals({ excludedProductCount: 3 }),
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    expect(screen.getByTestId("excluded-count")).toBeInTheDocument();
    expect(screen.getByTestId("excluded-count")).toHaveTextContent("3");
  });

  it("hides the excluded-product count when excludedProductCount is zero", () => {
    mockUsePricingBaselineQuery.mockReturnValue({
      data: mockData, // excludedProductCount: 0
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    expect(screen.queryByTestId("excluded-count")).not.toBeInTheDocument();
  });

  it("shows an empty state when rows is empty", () => {
    mockUsePricingBaselineQuery.mockReturnValue({
      data: {
        rows: [],
        totals: buildTotals(),
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    expect(screen.getByText(/žádné produkty/i)).toBeInTheDocument();
  });
});
