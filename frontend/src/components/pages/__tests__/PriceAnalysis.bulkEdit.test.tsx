import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import { toast } from "react-hot-toast";
import PriceAnalysis from "../PriceAnalysis";
import * as usePricingSimulatorHook from "../../../api/hooks/usePricingSimulator";
import { PricingRowDto, PricingTotalsDto } from "../../../api/generated/api-client";
import { PRICING_WORK_GROUP_STORAGE_KEY } from "../../pricing/pricingWorkGroup";

// Bulk editing from the column headers: one relative change applied to every product
// the grid lists, through the same recalculation a single cell edit uses. Narrowing
// the grid -- by filter or by the work group switch -- is how the user picks targets.
jest.mock("../../../api/hooks/usePricingSimulator");
jest.mock("react-hot-toast", () => ({
  __esModule: true,
  toast: Object.assign(jest.fn(), { success: jest.fn(), error: jest.fn() }),
}));

jest.mock("../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: () => true,
  }),
}));

jest.mock("../CatalogDetail", () => ({
  __esModule: true,
  default: () => null,
}));

const mockUsePricingBaselineQuery =
  usePricingSimulatorHook.usePricingBaselineQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingBaselineQuery
  >;
const mockUseRecalculatePricingMutation =
  usePricingSimulatorHook.useRecalculatePricingMutation as jest.MockedFunction<
    typeof usePricingSimulatorHook.useRecalculatePricingMutation
  >;
const mockUsePricingScenariosQuery =
  usePricingSimulatorHook.usePricingScenariosQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingScenariosQuery
  >;
const mockUsePricingScenarioQuery =
  usePricingSimulatorHook.usePricingScenarioQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingScenarioQuery
  >;
const mockUseSavePricingScenarioMutation =
  usePricingSimulatorHook.useSavePricingScenarioMutation as jest.MockedFunction<
    typeof usePricingSimulatorHook.useSavePricingScenarioMutation
  >;
const mockUseDeletePricingScenarioMutation =
  usePricingSimulatorHook.useDeletePricingScenarioMutation as jest.MockedFunction<
    typeof usePricingSimulatorHook.useDeletePricingScenarioMutation
  >;
const mockUsePricingSummaryQuery =
  usePricingSimulatorHook.usePricingSummaryQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingSummaryQuery
  >;

const mockMutateAsync = jest.fn();

const buildRow = (overrides: Partial<PricingRowDto> = {}): PricingRowDto =>
  ({
    productCode: "PROD001",
    productName: "Test Product 1",
    baselinePrice: 100,
    baselineMaterialCost: 30,
    baselineManufacturingCost: 20,
    baselineQuantity: 10,
    price: 100,
    materialCost: 30,
    manufacturingCost: 20,
    forecastQuantity: 10,
    m0Amount: 70,
    m0Percentage: 70,
    m1Amount: 50,
    m1Percentage: 50,
    isEdited: false,
    isExcluded: false,
    baselineDrifted: false,
    ...overrides,
  }) as PricingRowDto;

const rows = [
  buildRow(),
  buildRow({ productCode: "PROD002", productName: "Test Product 2", baselinePrice: 200, price: 200 }),
  buildRow({ productCode: "PROD003", productName: "Test Product 3", baselinePrice: 300, price: 300 }),
];

const buildTotals = (): PricingTotalsDto =>
  ({
    revenueBefore: 6000,
    revenueAfter: 6000,
    revenueDelta: 0,
    revenueDeltaPercentage: 0,
    m0Before: 4200,
    m0After: 4200,
    m0Delta: 0,
    m0DeltaPercentage: 0,
    m1Before: 3000,
    m1After: 3000,
    m1Delta: 0,
    m1DeltaPercentage: 0,
    editedProductCount: 0,
    excludedProductCount: 0,
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

const renderPage = () => render(<PriceAnalysis />, { wrapper: createWrapper() });

const openBulkEditor = () =>
  fireEvent.click(screen.getByTestId("pricing-bulk-edit-Price"));

describe("PriceAnalysis bulk edit", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    window.localStorage.clear();
    // Echo the overrides back the way the server does, so a second bulk edit in the
    // same test runs against what the first one left behind.
    mockMutateAsync.mockImplementation(async (payload: any) => ({
      rows,
      totals: buildTotals(),
      overrides: payload.overrides,
    }));
    mockUsePricingBaselineQuery.mockReturnValue({
      data: { rows, totals: buildTotals() },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);
    mockUseRecalculatePricingMutation.mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: false,
    } as any);
    mockUsePricingScenariosQuery.mockReturnValue({
      data: { scenarios: [] },
      isLoading: false,
      error: null,
    } as any);
    mockUsePricingScenarioQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: null,
    } as any);
    mockUseSavePricingScenarioMutation.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    } as any);
    mockUseDeletePricingScenarioMutation.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    } as any);
    mockUsePricingSummaryQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: null,
    } as any);
  });

  it("raises the price of every listed product by the typed percentage", async () => {
    // Arrange: nothing is ticked -- the work group plays no part in what a bulk edit
    // hits, only in what the grid lists.
    renderPage();

    // Act
    openBulkEditor();
    fireEvent.change(screen.getByTestId("pricing-bulk-editor-amount"), {
      target: { value: "10" },
    });
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));

    // Assert: one recalculation for the whole grid.
    await waitFor(() => expect(mockMutateAsync).toHaveBeenCalledTimes(1));
    const payload = mockMutateAsync.mock.calls[0][0];
    expect(payload.edit).toBeUndefined();
    expect(payload.overrides).toEqual([
      expect.objectContaining({ productCode: "PROD001", price: 110 }),
      expect.objectContaining({ productCode: "PROD002", price: 220 }),
      expect.objectContaining({ productCode: "PROD003", price: 330 }),
    ]);
  });

  it("cuts prices on a negative percentage", async () => {
    // Arrange
    renderPage();

    // Act
    openBulkEditor();
    fireEvent.change(screen.getByTestId("pricing-bulk-editor-amount"), {
      target: { value: "-20" },
    });
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));

    // Assert
    await waitFor(() => expect(mockMutateAsync).toHaveBeenCalledTimes(1));
    expect(mockMutateAsync.mock.calls[0][0].overrides).toEqual([
      expect.objectContaining({ productCode: "PROD001", price: 80 }),
      expect.objectContaining({ productCode: "PROD002", price: 160 }),
      expect.objectContaining({ productCode: "PROD003", price: 240 }),
    ]);
  });

  it("puts the column back to its original values on zero percent", async () => {
    // Arrange: every listed product first goes up 10 %.
    renderPage();
    openBulkEditor();
    fireEvent.change(screen.getByTestId("pricing-bulk-editor-amount"), {
      target: { value: "10" },
    });
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));
    await waitFor(() => expect(mockMutateAsync).toHaveBeenCalledTimes(1));

    // Act
    openBulkEditor();
    fireEvent.change(screen.getByTestId("pricing-bulk-editor-amount"), {
      target: { value: "0" },
    });
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));

    // Assert: the pinned prices are gone rather than pinned at the original value.
    await waitFor(() => expect(mockMutateAsync).toHaveBeenCalledTimes(2));
    expect(mockMutateAsync.mock.calls[1][0].overrides).toEqual([]);
  });

  it("still resets the column when the reset is queued behind an edit in flight", async () => {
    // Arrange: hold the first recalculation open. The client's override set is only
    // ever written from a RESPONSE, so while that request is unanswered it still
    // reads empty -- which is exactly the state a reset used to be measured against
    // before being discarded as "nothing to clear".
    let releaseFirstCall: () => void = () => {};
    const firstCallHeld = new Promise<void>((resolve) => {
      releaseFirstCall = resolve;
    });
    let callIndex = 0;
    mockMutateAsync.mockImplementation(async (payload: any) => {
      if (callIndex++ === 0) {
        await firstCallHeld;
      }
      return { rows, totals: buildTotals(), overrides: payload.overrides };
    });

    renderPage();
    openBulkEditor();
    fireEvent.change(screen.getByTestId("pricing-bulk-editor-amount"), {
      target: { value: "10" },
    });
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));
    await waitFor(() => expect(mockMutateAsync).toHaveBeenCalledTimes(1));

    // Act: reset the column before that first request has been answered.
    openBulkEditor();
    fireEvent.change(screen.getByTestId("pricing-bulk-editor-amount"), {
      target: { value: "0" },
    });
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));
    releaseFirstCall();

    // Assert: the reset is built against what the first call left behind, so it
    // reaches the server and clears the pinned prices instead of vanishing.
    await waitFor(() => expect(mockMutateAsync).toHaveBeenCalledTimes(2));
    expect(mockMutateAsync.mock.calls[1][0].overrides).toEqual([]);
  });

  it("says so and posts nothing when no product can take the change", async () => {
    // Arrange
    renderPage();

    // Act: -100 % would wipe the price out, which the server would reject anyway.
    openBulkEditor();
    fireEvent.change(screen.getByTestId("pricing-bulk-editor-amount"), {
      target: { value: "-100" },
    });
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));

    // Assert
    expect(toast.error).toHaveBeenCalled();
    expect(mockMutateAsync).not.toHaveBeenCalled();
  });

  it("acts only on the products the grid currently lists", async () => {
    // Arrange: the filter hides PROD003, so the mass change must leave it alone.
    mockUsePricingBaselineQuery.mockImplementation(
      (filter?: { productName?: string }) =>
        ({
          data: {
            rows: filter?.productName ? rows.slice(0, 2) : rows,
            totals: buildTotals(),
          },
          isLoading: false,
          error: null,
          refetch: jest.fn(),
        }) as any,
    );
    renderPage();
    fireEvent.change(screen.getByPlaceholderText("Název produktu..."), {
      target: { value: "Test" },
    });
    fireEvent.click(screen.getByText("Filtrovat"));
    await waitFor(() =>
      expect(screen.queryByTestId("pricing-row-PROD003")).not.toBeInTheDocument(),
    );

    // Act
    openBulkEditor();
    fireEvent.change(screen.getByTestId("pricing-bulk-editor-amount"), {
      target: { value: "10" },
    });
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));

    // Assert
    await waitFor(() => expect(mockMutateAsync).toHaveBeenCalledTimes(1));
    expect(mockMutateAsync.mock.calls[0][0].overrides).toEqual([
      expect.objectContaining({ productCode: "PROD001", price: 110 }),
      expect.objectContaining({ productCode: "PROD002", price: 220 }),
    ]);
  });

  it("follows the grid's work group filter when one is switched on", async () => {
    // Arrange: narrowing the grid to the work group is how a mass change is aimed at
    // exactly those products.
    window.localStorage.setItem(
      PRICING_WORK_GROUP_STORAGE_KEY,
      JSON.stringify(["PROD002", "PROD003"]),
    );
    renderPage();
    fireEvent.click(screen.getByTestId("pricing-work-group-filter"));

    // Act
    openBulkEditor();
    fireEvent.change(screen.getByTestId("pricing-bulk-editor-amount"), {
      target: { value: "10" },
    });
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));

    // Assert
    await waitFor(() => expect(mockMutateAsync).toHaveBeenCalledTimes(1));
    expect(mockMutateAsync.mock.calls[0][0].overrides).toEqual([
      expect.objectContaining({ productCode: "PROD002", price: 220 }),
      expect.objectContaining({ productCode: "PROD003", price: 330 }),
    ]);
  });
});
