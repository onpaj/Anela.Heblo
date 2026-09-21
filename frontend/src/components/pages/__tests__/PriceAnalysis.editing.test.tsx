import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import PriceAnalysis from "../PriceAnalysis";
import * as usePricingSimulatorHook from "../../../api/hooks/usePricingSimulator";
import {
  PricingRowDto,
  PricingTotalsDto,
  PricingEditField,
  SwaggerException,
} from "../../../api/generated/api-client";
import { formatCurrency } from "../../../utils/formatters";
import { toast } from "react-hot-toast";

// Editing/recalculation behaviours added in Task 9. The read-only rendering
// (filters, totals lines, excluded rows) is already covered by PriceAnalysis.test.tsx;
// this file is scoped to: commit-on-blur, Enter/Escape, rejected vs network failures,
// and per-row/global reset.
jest.mock("../../../api/hooks/usePricingSimulator");
jest.mock("react-hot-toast", () => ({
  __esModule: true,
  toast: { success: jest.fn(), error: jest.fn() },
}));

const mockUsePricingBaselineQuery =
  usePricingSimulatorHook.usePricingBaselineQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingBaselineQuery
  >;
const mockUseRecalculatePricingMutation =
  usePricingSimulatorHook.useRecalculatePricingMutation as jest.MockedFunction<
    typeof usePricingSimulatorHook.useRecalculatePricingMutation
  >;

// Plain object literals -- see PriceAnalysis.test.tsx for why (Babel class-field
// re-initialization drops constructor-assigned fields on the generated Response classes).
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

describe("PriceAnalysis editing", () => {
  let mockMutateAsync: jest.Mock;

  const priceInput = () =>
    screen.getByTestId(`pricing-cell-PROD001-${PricingEditField.Price}`) as HTMLInputElement;

  beforeEach(() => {
    jest.clearAllMocks();
    mockMutateAsync = jest.fn();
    mockUseRecalculatePricingMutation.mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: false,
    } as any);
    mockUsePricingBaselineQuery.mockReturnValue({
      data: { rows: [buildRow()], totals: buildTotals() },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);
  });

  it("does not call the mutation while typing, only on blur", async () => {
    mockMutateAsync.mockResolvedValue({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals(),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    expect(mockMutateAsync).not.toHaveBeenCalled();

    fireEvent.blur(priceInput());
    expect(mockMutateAsync).toHaveBeenCalledTimes(1);

    // Wait for the full round trip (including the reset-token bump) to settle so
    // no state update from this commit leaks, unflushed, into the next test.
    expect(await screen.findByTestId("pricing-row-reset-PROD001")).toBeInTheDocument();
  });

  it("posts the accumulated overrides and the single edit gesture on commit", async () => {
    mockMutateAsync.mockResolvedValue({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals(),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());

    expect(mockMutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({
        overrides: [],
        edit: { productCode: "PROD001", field: PricingEditField.Price, value: 175 },
      }),
    );

    expect(await screen.findByTestId("pricing-row-reset-PROD001")).toBeInTheDocument();
  });

  it("replaces rows, totals and overrides from a successful recalculate", async () => {
    mockMutateAsync.mockResolvedValue({
      rows: [buildRow({ price: 175, m0Percentage: 82 })],
      totals: buildTotals({ revenueAfter: 17500, revenueDelta: 2500 }),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());

    await waitFor(() => {
      const lineText = screen.getByTestId("totals-line-Obrat").textContent ?? "";
      // Read raw textContent and compare with .toContain rather than jest-dom's
      // toHaveTextContent: its default normalizer collapses formatCurrency's
      // non-breaking spaces to plain ASCII ones, which would silently break this
      // match (see PriceAnalysis.test.tsx's assertLine for the same pattern).
      expect(lineText).toContain(formatCurrency(17500));
    });

    const m0Input = screen.getByTestId(
      `pricing-cell-PROD001-${PricingEditField.M0Percentage}`,
    ) as HTMLInputElement;
    expect(m0Input.value).toBe("82");
  });

  it("shows the Czech message inline on a rejected edit, keeps the prior value, and leaves totals unchanged", async () => {
    const body = JSON.stringify({
      success: false,
      errorCode: "PricingNegativeMaterialCost",
      params: null,
    });
    mockMutateAsync.mockRejectedValue(new SwaggerException("Bad Request", 400, body, {}, null));

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "999" } });
    fireEvent.blur(priceInput());

    expect(
      await screen.findByText(
        "Marže M0 je vyšší než cena — materiálové náklady by byly záporné",
      ),
    ).toBeInTheDocument();

    expect(priceInput().value).toBe("150");
    expect(screen.getByTestId("totals-line-Obrat").textContent ?? "").toContain(
      formatCurrency(15000),
    );
  });

  it("reverts the cell and badges totals as stale on a network failure", async () => {
    mockMutateAsync.mockRejectedValue(new Error("Network Error"));

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "999" } });
    fireEvent.blur(priceInput());

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalled();
    });

    expect(priceInput().value).toBe("150");
    expect(screen.getByTestId("totals-stale-badge")).toBeInTheDocument();
  });

  it("keeps the previous totals rendered and shows a spinner while a recalculate is in flight", () => {
    mockUseRecalculatePricingMutation.mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: true,
    } as any);

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    // Previous totals must still be on screen -- never blanked during a request.
    expect(screen.getByTestId("totals-line-Obrat").textContent ?? "").toContain(
      formatCurrency(15000),
    );
    expect(screen.getByText(/Přepočítávám/i)).toBeInTheDocument();
  });

  it("clears a row's override and recalculates on per-row reset", async () => {
    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals({ revenueAfter: 17500 }),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());

    const resetButton = await screen.findByTestId("pricing-row-reset-PROD001");

    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 150, isEdited: false })],
      totals: buildTotals(),
      overrides: [],
    });

    fireEvent.click(resetButton);

    expect(mockMutateAsync).toHaveBeenLastCalledWith(
      expect.objectContaining({ overrides: [], edit: undefined }),
    );

    // The row's own reset control disappears once the server confirms it is
    // no longer edited -- proof the full round trip (not just the call) landed.
    await waitFor(() => {
      expect(screen.queryByTestId("pricing-row-reset-PROD001")).not.toBeInTheDocument();
    });
  });

  it("clears every override on global reset", async () => {
    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 175, isEdited: true })],
      totals: buildTotals({ revenueAfter: 17500 }),
      overrides: [{ productCode: "PROD001", price: 175 }],
    });

    render(<PriceAnalysis />, { wrapper: createWrapper() });

    fireEvent.change(priceInput(), { target: { value: "175" } });
    fireEvent.blur(priceInput());

    const resetAllButton = await screen.findByTestId("pricing-reset-all");

    mockMutateAsync.mockResolvedValueOnce({
      rows: [buildRow({ price: 150, isEdited: false })],
      totals: buildTotals(),
      overrides: [],
    });

    fireEvent.click(resetAllButton);

    expect(mockMutateAsync).toHaveBeenLastCalledWith(
      expect.objectContaining({ overrides: [], edit: undefined }),
    );

    // The global reset control disappears once overrides comes back empty --
    // proof the full round trip (not just the call) landed.
    await waitFor(() => {
      expect(screen.queryByTestId("pricing-reset-all")).not.toBeInTheDocument();
    });
  });
});
