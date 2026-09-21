import React from "react";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import PriceAnalysis from "../PriceAnalysis";
import * as usePricingSimulatorHook from "../../../api/hooks/usePricingSimulator";
import { PricingRowDto, PricingTotalsDto } from "../../../api/generated/api-client";

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

  it("renders one row per product", () => {
    mockUsePricingBaselineQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    expect(screen.getByText("Test Product 1")).toBeInTheDocument();
    expect(screen.getByText("Test Product 2")).toBeInTheDocument();
    expect(screen.getAllByRole("row")).toHaveLength(2 + 1); // 2 data rows + header row
  });

  it("renders the three totals lines with before, after and delta", () => {
    mockUsePricingBaselineQuery.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    expect(screen.getByText("Obrat")).toBeInTheDocument();
    expect(screen.getByText("M0")).toBeInTheDocument();
    expect(screen.getByText("M1")).toBeInTheDocument();

    // Revenue before/after (formatCurrency renders "15 000,00 Kč" in cs-CZ)
    expect(screen.getAllByText(/15.*000.*Kč/).length).toBeGreaterThan(0);
    // M0 before/after
    expect(screen.getAllByText(/12.*000.*Kč/).length).toBeGreaterThan(0);
    // M1 before/after
    expect(screen.getAllByText(/10.*000.*Kč/).length).toBeGreaterThan(0);
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
