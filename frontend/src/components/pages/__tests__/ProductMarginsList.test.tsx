import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import ProductMarginsList from "../ProductMarginsList";
import * as useProductMarginsHook from "../../../api/hooks/useProductMargins";

// Mock the hooks
jest.mock("../../../api/hooks/useProductMargins");
// Only this suite opens the help sheet, so only this suite resolves its lazy
// chunk and needs react-markdown's ESM stubbed. That is what the lazy() in
// ProductMarginsList buys - every other suite keeps it out of its module graph.
jest.mock("react-markdown", () => ({
  __esModule: true,
  default: ({ children }: { children: string }) => <div data-testid="markdown">{children}</div>,
}));
jest.mock("remark-gfm", () => ({ __esModule: true, default: () => {} }));
jest.mock("../CatalogDetail", () => {
  return function MockCatalogDetail({ isOpen, onClose, productCode }: any) {
    return isOpen ? (
      <div data-testid="catalog-detail-modal">
        <div>Product Code: {productCode}</div>
        <button onClick={onClose}>Close</button>
      </div>
    ) : null;
  };
});

const mockUseProductMargins =
  useProductMarginsHook.useProductMarginsQuery as jest.MockedFunction<
    typeof useProductMarginsHook.useProductMarginsQuery
  >;

const mockData = {
  items: [
    {
      productCode: "PROD001",
      productName: "Test Product 1",
      priceWithoutVat: 150,
      purchasePrice: 100,
      manufactureDifficulty: 2.5,
      priceWithoutVatIsFromEshop: true,
      // M0-M3 margin levels using MarginLevelDto structure
      m0: {
        percentage: 80.0,
        amount: 120,
        costLevel: 30,
        costTotal: 30,
      },
      m1: {
        percentage: 66.67,
        amount: 100,
        costLevel: 20,
        costTotal: 50,
      },
      m2: {
        percentage: 50.0,
        amount: 75,
        costLevel: 25,
        costTotal: 75,
      },
      m3: {
        percentage: 40.0,
        amount: 60,
        costLevel: 15,
        costTotal: 90,
      },
    },
    {
      productCode: "PROD002",
      productName: "Test Product 2",
      priceWithoutVat: 200,
      purchasePrice: 140,
      manufactureDifficulty: 3.0,
      priceWithoutVatIsFromEshop: false,
      // M0-M3 margin levels using MarginLevelDto structure
      m0: {
        percentage: 75.0,
        amount: 150,
        costLevel: 40,
        costTotal: 40,
      },
      m1: {
        percentage: 60.0,
        amount: 120,
        costLevel: 30,
        costTotal: 70,
      },
      m2: {
        percentage: 45.0,
        amount: 90,
        costLevel: 30,
        costTotal: 100,
      },
      m3: {
        percentage: 35.0,
        amount: 70,
        costLevel: 12,
        costTotal: 112,
      },
    },
  ],
  totalCount: 2,
  pageNumber: 1,
  pageSize: 20,
  totalPages: 1,
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

describe("ProductMarginsList", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("renders loading state", () => {
    mockUseProductMargins.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    expect(
      screen.getByText("Načítání marží produktů..."),
    ).toBeInTheDocument();
  });

  it("renders error state", () => {
    const error = new Error("API Error");
    mockUseProductMargins.mockReturnValue({
      data: undefined,
      isLoading: false,
      error,
      refetch: jest.fn(),
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    expect(
      screen.getByText("Chyba při načítání marží: API Error"),
    ).toBeInTheDocument();
  });

  it("renders table with M0-M3 columns", () => {
    mockUseProductMargins.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    // Check that all M0-M3 headers are present
    expect(screen.getByText("M0 %")).toBeInTheDocument();
    expect(screen.getByText("M1 %")).toBeInTheDocument();
    expect(screen.getByText("M2 %")).toBeInTheDocument();
    expect(screen.getByText("M3 %")).toBeInTheDocument();

    // Check that data is displayed
    expect(screen.getByText("Test Product 1")).toBeInTheDocument();
    expect(screen.getByText("Test Product 2")).toBeInTheDocument();
  });

  it("displays margin percentages with correct formatting", () => {
    mockUseProductMargins.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    // Check formatted percentage values
    expect(screen.getByText("80.00%")).toBeInTheDocument(); // M0 for first product
    expect(screen.getByText("66.67%")).toBeInTheDocument(); // M1 for first product
    expect(screen.getByText("50.00%")).toBeInTheDocument(); // M2 for first product
    expect(screen.getByText("40.00%")).toBeInTheDocument(); // M3 for first product
  });

  it("shows tooltips with cost breakdown on hover", async () => {
    mockUseProductMargins.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    // Check that the percentage values are displayed
    expect(screen.getByText("80.00%")).toBeInTheDocument();
    expect(screen.getByText("66.67%")).toBeInTheDocument();
    expect(screen.getByText("50.00%")).toBeInTheDocument();

    // Verify tooltip text is in the DOM via title attributes (accessible method)
    const m0Cell = screen.getByText("80.00%");
    expect(m0Cell).toBeInTheDocument();

    const m1Cell = screen.getByText("66.67%");
    expect(m1Cell).toBeInTheDocument();

    const m2Cell = screen.getByText("50.00%");
    expect(m2Cell).toBeInTheDocument();
  });

  it("handles sorting by M3 percentage by default", () => {
    const mockRefetch = jest.fn();
    mockUseProductMargins.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    // Default sorting should be by M3 percentage descending (M3 is the final margin level)
    expect(mockUseProductMargins).toHaveBeenCalledWith(
      "", // productCodeFilter
      "", // productNameFilter
      "Product", // productTypeFilter
      1, // pageNumber
      20, // pageSize
      "m3Percentage", // sortBy
      true, // sortDescending
    );
  });

  it("allows sorting by different margin levels", async () => {
    const user = userEvent.setup();
    const mockRefetch = jest.fn();
    mockUseProductMargins.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    // Click on M0 header to sort by M0
    const m0Header = screen.getByText("M0 %");
    await user.click(m0Header);

    // Check that hook is called with new sort parameter
    expect(mockUseProductMargins).toHaveBeenLastCalledWith(
      "", // productCodeFilter
      "", // productNameFilter
      "Product", // productTypeFilter
      1, // pageNumber
      20, // pageSize
      "m0Percentage", // sortBy
      false, // sortDescending (first click should be ascending)
    );
  });

  it("sorts by M3 percentage when the M3 header is clicked", async () => {
    const user = userEvent.setup();
    mockUseProductMargins.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    // M3 is the initial sort column, so clicking it flips direction rather than
    // switching column - which is exactly what distinguishes a wired-up header
    // from one that only happens to match the initial useState value.
    const m3Header = screen.getByText("M3 %");
    await user.click(m3Header);

    expect(mockUseProductMargins).toHaveBeenLastCalledWith(
      "", // productCodeFilter
      "", // productNameFilter
      "Product", // productTypeFilter
      1, // pageNumber
      20, // pageSize
      "m3Percentage", // sortBy
      false, // sortDescending (flipped off the descending default)
    );
  });

  it("renders a dash for a product with no M3 data", () => {
    const dataWithoutM3 = {
      ...mockData,
      items: [{ ...mockData.items[0], m3: undefined }],
    };

    mockUseProductMargins.mockReturnValue({
      data: dataWithoutM3,
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    // The other levels still render, so a missing M3 must degrade to "-" rather
    // than blanking the row or throwing on undefined.
    expect(screen.getByText("80.00%")).toBeInTheDocument();
    expect(screen.queryByText("40.00%")).not.toBeInTheDocument();
    expect(screen.getAllByText("-").length).toBeGreaterThan(0);
  });

  it("handles filters correctly", async () => {
    const user = userEvent.setup();
    const mockRefetch = jest.fn().mockResolvedValue({});
    mockUseProductMargins.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: mockRefetch,
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    // Find filter inputs
    const productNameInput = screen.getByPlaceholderText("Název produktu...");
    const productCodeInput = screen.getByPlaceholderText("Kód produktu...");
    const filterButton = screen.getByText("Filtrovat");

    // Enter filter values
    await user.type(productNameInput, "Test");
    await user.type(productCodeInput, "PROD");
    await user.click(filterButton);

    // Check that refetch was called
    await waitFor(() => {
      expect(mockRefetch).toHaveBeenCalled();
    });
  });

  it("opens catalog detail modal when row is clicked", async () => {
    const user = userEvent.setup();
    mockUseProductMargins.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    // Click on first product row
    const firstRow = screen.getByText("Test Product 1").closest("tr");
    if (firstRow) {
      await user.click(firstRow);
    }

    // Check that modal is opened
    expect(screen.getByTestId("catalog-detail-modal")).toBeInTheDocument();
    expect(screen.getByText("Product Code: PROD001")).toBeInTheDocument();
  });

  it("displays price indicators for eshop vs ERP prices", () => {
    mockUseProductMargins.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    // Check that both price values are displayed
    expect(screen.getByText("150 Kč")).toBeInTheDocument();
    expect(screen.getByText("200 Kč")).toBeInTheDocument();

    // Check that products show price information
    expect(screen.getByText("Test Product 1")).toBeInTheDocument();
    expect(screen.getByText("Test Product 2")).toBeInTheDocument();
  });

  it("has proper page structure following layout standards", () => {
    mockUseProductMargins.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    // Check main heading is present
    expect(
      screen.getByRole("heading", { name: "Marže produktů" }),
    ).toBeInTheDocument();

    // Check filters section is present
    expect(screen.getByText("Filtry:")).toBeInTheDocument();

    // Check pagination info is shown
    expect(screen.getByText(/1-2 z 2/)).toBeInTheDocument();
  });

  it("opens the margin levels help sheet from the question mark and closes it again", async () => {
    // Arrange
    const user = userEvent.setup();
    mockUseProductMargins.mockReturnValue({
      data: mockData,
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    } as any);
    global.fetch = jest.fn(async () => ({
      ok: true,
      headers: new Headers({ "content-type": "text/markdown" }),
      text: async () => "# Hladiny marže",
    })) as unknown as typeof fetch;

    render(<ProductMarginsList />, { wrapper: createWrapper() });

    // Act - the sheet is lazy, so it resolves asynchronously
    await user.click(
      screen.getByRole("button", { name: "Hladiny marže — nápověda", exact: true }),
    );

    // Assert
    const sheet = await screen.findByTestId("margin-levels-help-sheet");
    expect(sheet).toBeInTheDocument();

    // Act - closing clears the state rather than only hiding the sheet
    await user.click(await screen.findByLabelText("Zavřít"));

    // Assert
    await waitFor(() =>
      expect(screen.queryByTestId("margin-levels-help-sheet")).not.toBeInTheDocument(),
    );
  });
});
