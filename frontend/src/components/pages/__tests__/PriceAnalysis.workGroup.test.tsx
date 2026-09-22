import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import PriceAnalysis from "../PriceAnalysis";
import * as usePricingSimulatorHook from "../../../api/hooks/usePricingSimulator";
import { PricingRowDto, PricingTotalsDto } from "../../../api/generated/api-client";
import { PRICING_WORK_GROUP_STORAGE_KEY } from "../../pricing/pricingWorkGroup";
import { formatCurrency } from "../../../utils/formatters";

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

// Stubbed for the same reason as in PriceAnalysis.productDetail.test.tsx: the real
// modal pulls in the whole catalog detail stack and this suite never opens it.
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
// The summary can cover more products than the grid does (all products, or a work
// group whose members the current filter hides), which the filtered baseline call
// simply does not contain -- hence a second, unfiltered calculation.
const mockUsePricingSummaryQuery =
  usePricingSimulatorHook.usePricingSummaryQuery as jest.MockedFunction<
    typeof usePricingSimulatorHook.usePricingSummaryQuery
  >;

// Every row is worth 1 000 Kč of baseline revenue (100 Kč x 10 ks), so a summary over
// a subset of them is readable straight off the assertion.
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

// The server's totals cover all three rows; the work-group summary must differ from
// them, which is what makes the scope switch observable.
const serverTotals = (): PricingTotalsDto =>
  ({
    revenueBefore: 3000,
    revenueAfter: 3000,
    revenueDelta: 0,
    revenueDeltaPercentage: 0,
    m0Before: 2100,
    m0After: 2100,
    m0Delta: 0,
    m0DeltaPercentage: 0,
    m1Before: 1500,
    m1After: 1500,
    m1Delta: 0,
    m1DeltaPercentage: 0,
    editedProductCount: 1,
    excludedProductCount: 0,
  }) as PricingTotalsDto;

// What the grid shows: the current filter matched three products, 3 000 Kč in total.
const rows = [
  buildRow({ isEdited: true }),
  buildRow({ productCode: "PROD002", productName: "Test Product 2" }),
  buildRow({ productCode: "PROD003", productName: "Test Product 3" }),
];

// What the catalogue holds: the same three plus two the filter hides. PROD005 sells
// 20 pieces (2 000 Kč) so that each scope lands on a distinct number:
// filter = 3 000 Kč, all products = 6 000 Kč, work group = 4 000 Kč.
const allRows = [
  ...rows,
  buildRow({ productCode: "PROD004", productName: "Test Product 4" }),
  buildRow({
    productCode: "PROD005",
    productName: "Test Product 5",
    baselineQuantity: 20,
    forecastQuantity: 20,
  }),
];

const allTotals = (): PricingTotalsDto =>
  ({
    ...serverTotals(),
    revenueBefore: 6000,
    revenueAfter: 6000,
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

// formatCurrency renders Czech thousands separators as non-breaking spaces, so the
// summary is asserted against the helper's own output over the line's textContent --
// the same approach as PriceAnalysis.test.tsx.
const revenueLineText = (): string =>
  screen.getByTestId("totals-line-Obrat").textContent ?? "";

const selectSummaryScope = (scope: string) =>
  fireEvent.click(screen.getByTestId(`pricing-summary-scope-${scope}`));

// Without a name/code filter the grid already shows the whole catalogue, so nothing
// reaches past it. Applying one is what puts products outside the grid's reach.
const applyProductNameFilter = async () => {
  fireEvent.change(screen.getByPlaceholderText("Název produktu..."), {
    target: { value: "Test" },
  });
  fireEvent.click(screen.getByText("Filtrovat"));
  await waitFor(() =>
    expect(mockUsePricingSummaryQuery).toHaveBeenLastCalledWith(
      [],
      undefined,
      true,
    ),
  );
};

describe("PriceAnalysis work group", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    window.localStorage.clear();
    mockUsePricingBaselineQuery.mockReturnValue({
      data: { rows, totals: serverTotals() },
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
      data: { rows: allRows, totals: allTotals() },
      isLoading: false,
      isFetching: false,
      error: null,
    } as any);
  });

  it("shows every product until the work group filter is switched on", () => {
    // Arrange
    window.localStorage.setItem(
      PRICING_WORK_GROUP_STORAGE_KEY,
      JSON.stringify(["PROD003"]),
    );
    renderPage();

    // Act
    fireEvent.click(screen.getByTestId("pricing-work-group-filter"));

    // Assert: the edited row and the pinned row survive, the untouched one does not.
    expect(screen.getByTestId("pricing-row-PROD001")).toBeInTheDocument();
    expect(screen.getByTestId("pricing-row-PROD003")).toBeInTheDocument();
    expect(screen.queryByTestId("pricing-row-PROD002")).not.toBeInTheDocument();
  });

  it("adds a product to the work group from its row checkbox and remembers it", () => {
    // Arrange
    renderPage();

    // Act
    fireEvent.click(screen.getByTestId("pricing-row-workgroup-PROD002"));

    // Assert
    expect(screen.getByTestId("pricing-row-workgroup-PROD002")).toBeChecked();
    expect(
      JSON.parse(
        window.localStorage.getItem(PRICING_WORK_GROUP_STORAGE_KEY) ?? "[]",
      ),
    ).toEqual(["PROD002"]);
  });

  it("removes a product from the work group when its checkbox is cleared", () => {
    // Arrange
    window.localStorage.setItem(
      PRICING_WORK_GROUP_STORAGE_KEY,
      JSON.stringify(["PROD002"]),
    );
    renderPage();
    expect(screen.getByTestId("pricing-row-workgroup-PROD002")).toBeChecked();

    // Act
    fireEvent.click(screen.getByTestId("pricing-row-workgroup-PROD002"));

    // Assert
    expect(screen.getByTestId("pricing-row-workgroup-PROD002")).not.toBeChecked();
    expect(
      JSON.parse(
        window.localStorage.getItem(PRICING_WORK_GROUP_STORAGE_KEY) ?? "[]",
      ),
    ).toEqual([]);
  });

  it("adds every listed product to the work group from the column header", () => {
    // Arrange
    renderPage();

    // Act
    fireEvent.click(screen.getByTestId("pricing-workgroup-all"));

    // Assert
    expect(screen.getByTestId("pricing-row-workgroup-PROD002")).toBeChecked();
    expect(screen.getByTestId("pricing-row-workgroup-PROD003")).toBeChecked();
    expect(
      JSON.parse(
        window.localStorage.getItem(PRICING_WORK_GROUP_STORAGE_KEY) ?? "[]",
      ),
    ).toEqual(["PROD001", "PROD002", "PROD003"]);
  });

  it("takes them out again when the column header is cleared", () => {
    // Arrange
    window.localStorage.setItem(
      PRICING_WORK_GROUP_STORAGE_KEY,
      JSON.stringify(["PROD001", "PROD002", "PROD003"]),
    );
    renderPage();
    expect(screen.getByTestId("pricing-workgroup-all")).toBeChecked();

    // Act
    fireEvent.click(screen.getByTestId("pricing-workgroup-all"));

    // Assert
    expect(screen.getByTestId("pricing-row-workgroup-PROD002")).not.toBeChecked();
    expect(
      JSON.parse(
        window.localStorage.getItem(PRICING_WORK_GROUP_STORAGE_KEY) ?? "[]",
      ),
    ).toEqual([]);
    // PROD001 carries an edit, so it stays in the work group whatever the header says.
    expect(screen.getByTestId("pricing-row-workgroup-PROD001")).toBeChecked();
  });

  it("shows the column header as partially selected when only some rows are in", () => {
    // Arrange
    window.localStorage.setItem(
      PRICING_WORK_GROUP_STORAGE_KEY,
      JSON.stringify(["PROD002"]),
    );

    // Act
    renderPage();

    // Assert
    expect(screen.getByTestId("pricing-workgroup-all")).toBePartiallyChecked();
  });

  it("adds only the products the grid currently lists", async () => {
    // Arrange: the header acts on what the grid shows, not on the whole catalogue,
    // so the baseline answers with fewer rows once a name filter is applied.
    mockUsePricingBaselineQuery.mockImplementation(
      (filter?: { productName?: string }) =>
        ({
          data: {
            rows: filter?.productName ? rows.slice(0, 2) : rows,
            totals: serverTotals(),
          },
          isLoading: false,
          error: null,
          refetch: jest.fn(),
        }) as any,
    );
    renderPage();
    await applyProductNameFilter();

    // Act
    fireEvent.click(screen.getByTestId("pricing-workgroup-all"));

    // Assert: PROD003 is not listed any more, so the header leaves it alone.
    expect(
      JSON.parse(
        window.localStorage.getItem(PRICING_WORK_GROUP_STORAGE_KEY) ?? "[]",
      ),
    ).toEqual(["PROD001", "PROD002"]);
  });

  it("keeps an edited product in the work group without letting it be unpicked", () => {
    // Arrange
    renderPage();

    // Act
    const editedCheckbox = screen.getByTestId("pricing-row-workgroup-PROD001");

    // Assert
    expect(editedCheckbox).toBeChecked();
    expect(editedCheckbox).toBeDisabled();
  });

  it("summarizes every product by default", () => {
    // Arrange
    renderPage();

    // Act
    const lineText = revenueLineText();

    // Assert: with no name/code filter the grid already holds every product, so the
    // default scope is answered by the totals the main call returned.
    expect(screen.getByTestId("pricing-summary-scope-all")).toBeChecked();
    expect(lineText).toContain(formatCurrency(3000));
    expect(mockUsePricingSummaryQuery).toHaveBeenLastCalledWith(
      [],
      undefined,
      false,
    );
  });

  it("pulls a wider summary once a filter narrows the grid", async () => {
    // Arrange
    renderPage();

    // Act
    await applyProductNameFilter();

    // Assert: the filtered rows are worth 3 000 Kč, the whole catalogue 6 000 Kč.
    expect(revenueLineText()).toContain(formatCurrency(6000));
    expect(revenueLineText()).not.toContain(formatCurrency(3000));
  });

  it("summarizes the filter result when the scope is set to the filter", async () => {
    // Arrange
    renderPage();
    await applyProductNameFilter();

    // Act
    selectSummaryScope("filter");

    // Assert
    expect(revenueLineText()).toContain(formatCurrency(3000));
    expect(mockUsePricingSummaryQuery).toHaveBeenLastCalledWith(
      [],
      undefined,
      false,
    );
  });

  it("summarizes the work group including products the filter hides", async () => {
    // Arrange: PROD005 is pinned but outside the filter, so a summary built from the
    // grid's own rows would silently under-count it.
    window.localStorage.setItem(
      PRICING_WORK_GROUP_STORAGE_KEY,
      JSON.stringify(["PROD003", "PROD005"]),
    );
    renderPage();
    await applyProductNameFilter();

    // Act
    selectSummaryScope("workGroup");

    // Assert: edited PROD001 (1 000) + pinned PROD003 (1 000) + pinned PROD005 (2 000).
    expect(revenueLineText()).toContain(formatCurrency(4000));
  });

  it("summarizes the work group from the grid's own rows while nothing is filtered", () => {
    // Arrange: PROD005 is pinned, but with no filter applied the grid's rows are the
    // whole catalogue already -- and this fixture's catalogue has no PROD005 row.
    window.localStorage.setItem(
      PRICING_WORK_GROUP_STORAGE_KEY,
      JSON.stringify(["PROD003"]),
    );
    renderPage();

    // Act
    selectSummaryScope("workGroup");

    // Assert: edited PROD001 (1 000) + pinned PROD003 (1 000), no second round trip.
    expect(revenueLineText()).toContain(formatCurrency(2000));
    expect(mockUsePricingSummaryQuery).toHaveBeenLastCalledWith(
      [],
      undefined,
      false,
    );
  });

  it("keeps the summary scope independent of which rows the grid shows", () => {
    // Arrange: the two switches are deliberately independent -- narrowing the grid to
    // the work group must not narrow the summary with it.
    window.localStorage.setItem(
      PRICING_WORK_GROUP_STORAGE_KEY,
      JSON.stringify(["PROD003"]),
    );
    renderPage();

    // Act
    fireEvent.click(screen.getByTestId("pricing-work-group-filter"));

    // Assert
    expect(revenueLineText()).toContain(formatCurrency(3000));
  });

  it("shows a placeholder instead of numbers until the wider summary arrives", async () => {
    // Arrange
    mockUsePricingSummaryQuery.mockReturnValue({
      data: undefined,
      isLoading: true,
      isFetching: true,
      error: null,
    } as any);
    renderPage();

    // Act
    await applyProductNameFilter();

    // Assert: no totals line may show the filter's numbers under an "all products"
    // label -- that would put the wrong figure next to the scope the user picked.
    expect(screen.getByTestId("pricing-summary-loading")).toBeInTheDocument();
    expect(screen.queryByTestId("totals-line-Obrat")).not.toBeInTheDocument();
  });

  it("says so when the wider summary cannot be loaded", async () => {
    // Arrange
    mockUsePricingSummaryQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: new Error("boom"),
    } as any);
    renderPage();

    // Act
    await applyProductNameFilter();

    // Assert
    expect(screen.getByTestId("pricing-summary-error")).toBeInTheDocument();
  });
});
