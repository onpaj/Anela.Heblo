import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import ProductStatisticsFilter, {
  defaultDateFrom,
  defaultDateTo,
  MAX_SELECTED_PRODUCTS,
} from "../ProductStatisticsFilter";

jest.mock("../../common/CatalogAutocomplete", () => ({
  __esModule: true,
  default: ({ values }: any) => (
    <div data-testid="catalog-autocomplete">{values?.length ?? 0} vybráno</div>
  ),
  CatalogAutocomplete: ({ values }: any) => (
    <div data-testid="catalog-autocomplete">{values?.length ?? 0} vybráno</div>
  ),
}));

const baseProps = {
  selectedProducts: [],
  onProductsChange: jest.fn(),
  dateFrom: "2025-01",
  dateTo: "2025-06",
  onDateFromChange: jest.fn(),
  onDateToChange: jest.fn(),
};

describe("ProductStatisticsFilter", () => {
  test("renders both month inputs with the given values", () => {
    render(<ProductStatisticsFilter {...baseProps} />);

    expect(screen.getByLabelText("Od")).toHaveValue("2025-01");
    expect(screen.getByLabelText("Do")).toHaveValue("2025-06");
  });

  test("calls onDateFromChange when the from month changes", () => {
    const onDateFromChange = jest.fn();
    render(
      <ProductStatisticsFilter {...baseProps} onDateFromChange={onDateFromChange} />,
    );

    fireEvent.change(screen.getByLabelText("Od"), {
      target: { value: "2024-11" },
    });

    expect(onDateFromChange).toHaveBeenCalledWith("2024-11");
  });

  test("shows an error when the range is inverted", () => {
    render(
      <ProductStatisticsFilter {...baseProps} dateFrom="2025-06" dateTo="2025-01" />,
    );

    expect(
      screen.getByText('Datum "Od" musí být dříve než "Do".'),
    ).toBeInTheDocument();
  });

  test("shows the selection cap message when the maximum is reached", () => {
    const selectedProducts = Array.from(
      { length: MAX_SELECTED_PRODUCTS },
      (_, i) => ({ productCode: `PROD-${i}`, productName: `Produkt ${i}` }),
    );

    render(
      <ProductStatisticsFilter {...baseProps} selectedProducts={selectedProducts} />,
    );

    expect(
      screen.getByText(`Maximálně ${MAX_SELECTED_PRODUCTS} produktů.`),
    ).toBeInTheDocument();
  });

  test("does not show the inverted-range error for a valid range", () => {
    render(<ProductStatisticsFilter {...baseProps} />);

    expect(
      screen.queryByText('Datum "Od" musí být dříve než "Do".'),
    ).not.toBeInTheDocument();
  });

  test("does not show the inverted-range error when dateFrom equals dateTo", () => {
    render(
      <ProductStatisticsFilter {...baseProps} dateFrom="2025-03" dateTo="2025-03" />,
    );

    expect(
      screen.queryByText('Datum "Od" musí být dříve než "Do".'),
    ).not.toBeInTheDocument();
  });

  test("does not show the selection cap message below the maximum", () => {
    render(<ProductStatisticsFilter {...baseProps} />);

    expect(
      screen.queryByText(`Maximálně ${MAX_SELECTED_PRODUCTS} produktů.`),
    ).not.toBeInTheDocument();
  });

  test("defaultDateTo returns the current month and defaultDateFrom is twelve months earlier", () => {
    const now = new Date(2025, 7, 15); // August 2025

    expect(defaultDateTo(now)).toBe("2025-08");
    expect(defaultDateFrom(now)).toBe("2024-08");
  });
});
