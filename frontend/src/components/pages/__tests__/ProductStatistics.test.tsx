import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import ProductStatistics from "../ProductStatistics";

const mockUseProductStatistics = jest.fn();

jest.mock("../../../api/hooks/useProductStatistics", () => ({
  ...jest.requireActual("../../../api/hooks/useProductStatistics"),
  useProductStatistics: (...args: any[]) => mockUseProductStatistics(...args),
}));

jest.mock("../../../telemetry/useScreenView", () => ({
  useScreenView: jest.fn(),
}));

jest.mock("../../product-statistics/ProductStatisticsFilter", () => {
  const actual = jest.requireActual(
    "../../product-statistics/ProductStatisticsFilter",
  );
  return {
    __esModule: true,
    ...actual,
    default: ({ onProductsChange }: any) => (
      <button
        onClick={() =>
          onProductsChange([{ productCode: "PROD-A", productName: "Krém" }])
        }
      >
        vybrat produkt
      </button>
    ),
  };
});

jest.mock("../../product-statistics/ProductStatisticsChart", () => ({
  __esModule: true,
  default: ({ yAxisLabel }: any) => (
    <div data-testid="chart">{yAxisLabel}</div>
  ),
}));

jest.mock("../../product-statistics/ProductStatisticsTable", () => ({
  __esModule: true,
  default: ({ series }: any) => (
    <div data-testid="table">{series.length} řad</div>
  ),
}));

describe("ProductStatistics page", () => {
  beforeEach(() => {
    mockUseProductStatistics.mockReset();
    mockUseProductStatistics.mockReturnValue({
      data: { months: ["2025-01"], products: [] },
      isLoading: false,
      isError: false,
      error: null,
    });
  });

  test("renders all four metric tabs", () => {
    render(<ProductStatistics />);

    expect(screen.getByRole("button", { name: "Prodeje" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Nákupy" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Spotřeba" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Výroba" })).toBeInTheDocument();
  });

  test("queries the Sales metric by default", () => {
    render(<ProductStatistics />);

    expect(mockUseProductStatistics).toHaveBeenCalledWith(
      [],
      "Sales",
      expect.any(String),
      expect.any(String),
    );
  });

  test("switching tabs re-queries with the new metric", () => {
    render(<ProductStatistics />);

    fireEvent.click(screen.getByRole("button", { name: "Výroba" }));

    expect(mockUseProductStatistics).toHaveBeenLastCalledWith(
      [],
      "Manufacture",
      expect.any(String),
      expect.any(String),
    );
  });

  test("switching tabs changes the chart's y-axis label", () => {
    render(<ProductStatistics />);

    // The chart only renders once a product is selected (see the
    // "shows a prompt instead of the chart" test below), so select one first.
    fireEvent.click(screen.getByText("vybrat produkt"));

    expect(screen.getByTestId("chart")).toHaveTextContent("Kusů prodáno");

    fireEvent.click(screen.getByRole("button", { name: "Spotřeba" }));

    expect(screen.getByTestId("chart")).toHaveTextContent(
      "Množství spotřebováno",
    );
  });

  test("keeps the product selection when switching tabs", () => {
    render(<ProductStatistics />);

    fireEvent.click(screen.getByText("vybrat produkt"));
    fireEvent.click(screen.getByRole("button", { name: "Nákupy" }));

    expect(mockUseProductStatistics).toHaveBeenLastCalledWith(
      ["PROD-A"],
      "Purchase",
      expect.any(String),
      expect.any(String),
    );
  });

  test("shows a prompt instead of the chart when no products are selected", () => {
    render(<ProductStatistics />);

    expect(
      screen.getByText("Vyberte produkty pro zobrazení statistik"),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("chart")).not.toBeInTheDocument();
  });

  test("renders the error state when the query fails", () => {
    mockUseProductStatistics.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      error: new Error("boom"),
    });

    render(<ProductStatistics />);

    fireEvent.click(screen.getByText("vybrat produkt"));

    expect(
      screen.getByText("Nepodařilo se načíst statistiky produktů"),
    ).toBeInTheDocument();
  });
});
