import React from "react";
import { render, screen, fireEvent, act } from "@testing-library/react";
import ProductStatisticsFilter, {
  defaultDateFrom,
  defaultDateTo,
  MAX_SELECTED_PRODUCTS,
} from "../ProductStatisticsFilter";

// The mock exposes onSelectMany so tests can drive the real handleProductsChange —
// the cap and the blank-code filter live there, not in react-select.
let mockLastOnSelectMany: ((items: any[]) => void) | undefined;

const mockAutocomplete = ({ values, onSelectMany }: any) => {
  mockLastOnSelectMany = onSelectMany;
  return (
    <div data-testid="catalog-autocomplete">{values?.length ?? 0} vybráno</div>
  );
};

jest.mock("../../common/CatalogAutocomplete", () => ({
  __esModule: true,
  default: (props: any) => mockAutocomplete(props),
  CatalogAutocomplete: (props: any) => mockAutocomplete(props),
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

  test("shows an error when dateFrom is empty", () => {
    render(<ProductStatisticsFilter {...baseProps} dateFrom="" />);

    // An empty month is a format problem, not an inverted range — saying so is what
    // tells the user which field to fix.
    expect(
      screen.getByText("Zadejte období ve formátu RRRR-MM."),
    ).toBeInTheDocument();
  });

  test("shows the inverted-range error when dateFrom is after dateTo", () => {
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

  test("bounds both month inputs to the supported history window", () => {
    render(<ProductStatisticsFilter {...baseProps} />);

    // Without min/max, typing a year digit-by-digit walks through 0002-01, 0020-01, ...
    // and each intermediate value fires a request the backend rejects with a 400.
    expect(screen.getByLabelText("Od")).toHaveAttribute("min", "2020-01");
    expect(screen.getByLabelText("Do")).toHaveAttribute("min", "2020-01");
    expect(screen.getByLabelText("Od")).toHaveAttribute("max", defaultDateTo());
    expect(screen.getByLabelText("Do")).toHaveAttribute("max", defaultDateTo());
  });

  test("shows a format error for a month the query would silently reject", () => {
    // Safari renders <input type="month"> as a text field, so "2025-1" is reachable.
    render(<ProductStatisticsFilter {...baseProps} dateFrom="2025-1" />);

    expect(
      screen.getByText("Zadejte období ve formátu RRRR-MM."),
    ).toBeInTheDocument();
  });

  test("shows an error for a range ending before the first month with history", () => {
    render(
      <ProductStatisticsFilter {...baseProps} dateFrom="2015-01" dateTo="2019-06" />,
    );

    expect(
      screen.getByText("Data jsou k dispozici až od 2020-01."),
    ).toBeInTheDocument();
  });

  test("shows an error for a range wider than the backend cap", () => {
    render(
      <ProductStatisticsFilter {...baseProps} dateFrom="2020-01" dateTo="2030-02" />,
    );

    expect(
      screen.getByText("Rozsah nesmí přesáhnout 120 měsíců."),
    ).toBeInTheDocument();
  });

  test("caps the selection and says the extra pick was ignored", () => {
    const onProductsChange = jest.fn();
    render(
      <ProductStatisticsFilter
        {...baseProps}
        onProductsChange={onProductsChange}
      />,
    );

    const eleven = Array.from({ length: 11 }, (_, index) => ({
      productCode: `PROD-${index}`,
      productName: `Produkt ${index}`,
    }));

    act(() => mockLastOnSelectMany?.(eleven));

    expect(onProductsChange).toHaveBeenCalledWith(
      eleven.slice(0, MAX_SELECTED_PRODUCTS),
    );
    expect(
      screen.getByText(
        `Porovnat lze nejvýše ${MAX_SELECTED_PRODUCTS} produktů, další výběr byl ignorován.`,
      ),
    ).toBeInTheDocument();
  });

  test("drops items without a product code and falls back to the code for a missing name", () => {
    const onProductsChange = jest.fn();
    render(
      <ProductStatisticsFilter
        {...baseProps}
        onProductsChange={onProductsChange}
      />,
    );

    act(() =>
      mockLastOnSelectMany?.([
        { productCode: "", productName: "Bez kódu" },
        { productCode: "PROD-A", productName: undefined },
      ]),
    );

    expect(onProductsChange).toHaveBeenCalledWith([
      { productCode: "PROD-A", productName: "PROD-A" },
    ]);
  });
});
