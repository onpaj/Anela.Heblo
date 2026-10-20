import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import PriceAnalysis from "../PriceAnalysis";
import * as usePricingSimulatorHook from "../../../api/hooks/usePricingSimulator";
import { PricingRowDto, PricingTotalsDto } from "../../../api/generated/api-client";

jest.mock("../../../api/hooks/usePricingSimulator");

jest.mock("../../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: () => true,
  }),
}));

// CatalogDetail pulls in the whole catalog detail stack (charts, tabs, its own
// queries); this suite only cares about which product it is opened for, so it is
// stubbed down to the props PriceAnalysis passes.
jest.mock("../CatalogDetail", () => ({
  __esModule: true,
  default: ({
    productCode,
    isOpen,
    onClose,
  }: {
    productCode: string | null;
    isOpen: boolean;
    onClose: () => void;
  }) =>
    isOpen ? (
      <div data-testid="catalog-detail-modal">
        <span data-testid="catalog-detail-product">{productCode}</span>
        <button type="button" onClick={onClose}>
          Zavřít detail
        </button>
      </div>
    ) : null,
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
// The summary band's scope selector can reach past the current filter, which needs a
// second, unfiltered calculation. PriceAnalysis calls the hook on every render (it is
// simply disabled while the scope stays on the filter), so it needs a return value
// here even though no test in this suite changes the scope.
const mockUsePricingSummaryQuery =
  usePricingSimulatorHook.usePricingSummaryQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingSummaryQuery
  >;

// Plain object literals rather than `new PricingRowDto(...)`: the generated
// client's Response subclasses drop fields passed to their constructor.
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

const buildTotals = (): PricingTotalsDto =>
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

describe("PriceAnalysis product detail", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUsePricingBaselineQuery.mockReturnValue({
      data: {
        rows: [
          buildRow(),
          buildRow({ productCode: "PROD002", productName: "Test Product 2" }),
        ],
        totals: buildTotals(),
      },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);
    mockUseRecalculatePricingMutation.mockReturnValue({
      mutateAsync: jest.fn(),
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

  it("opens the product detail for the clicked product code", () => {
    // Arrange
    render(<PriceAnalysis />, { wrapper: createWrapper() });
    expect(screen.queryByTestId("catalog-detail-modal")).not.toBeInTheDocument();

    // Act
    fireEvent.click(screen.getByTestId("pricing-row-detail-code-PROD002"));

    // Assert
    expect(screen.getByTestId("catalog-detail-product")).toHaveTextContent(
      "PROD002",
    );
  });

  it("opens the product detail for the clicked product name", () => {
    // Arrange
    render(<PriceAnalysis />, { wrapper: createWrapper() });

    // Act
    fireEvent.click(screen.getByTestId("pricing-row-detail-name-PROD001"));

    // Assert
    expect(screen.getByTestId("catalog-detail-product")).toHaveTextContent(
      "PROD001",
    );
  });

  it("closes the product detail again", () => {
    // Arrange
    render(<PriceAnalysis />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByTestId("pricing-row-detail-code-PROD001"));

    // Act
    fireEvent.click(screen.getByText("Zavřít detail"));

    // Assert
    expect(screen.queryByTestId("catalog-detail-modal")).not.toBeInTheDocument();
  });
});
