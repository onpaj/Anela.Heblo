import React from "react";
import { render, screen, fireEvent, act } from "@testing-library/react";
import ProductStatisticsFilter, {
  defaultDateFrom,
  defaultDateTo,
  MAX_SELECTED_PRODUCTS,
} from "../ProductStatisticsFilter";
import { TimePeriod, resolveTimePeriod, getTimePeriodDisplayText } from "../../../utils/timePeriod";

// The mock exposes onSelectMany so tests can drive the real handleProductsChange —
// the cap and the blank-code filter live there, not in react-select.
let mockLastOnSelectMany: ((items: any[]) => void) | undefined;

const mockAutocomplete = ({ values, onSelectMany }: any) => {
  mockLastOnSelectMany = onSelectMany;
  return (
    <div data-testid="catalog-autocomplete">{values?.length ?? 0} vybráno</div>
  );
};

// The picker modal queries the catalog; these tests have no QueryClientProvider and
// only exercise products that are already selected, so an empty result set is enough.
jest.mock("../../../api/hooks/useCatalogAutocomplete", () => ({
  useCatalogAutocomplete: () => ({
    data: { items: [] },
    isLoading: false,
    isError: false,
  }),
}));

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

  describe("quick period buckets", () => {
    // The buckets resolve against the real clock, so expectations are derived from the
    // same helper the page uses rather than hardcoded months.
    const monthOf = (date: Date) =>
      `${date.getFullYear()}-${`${date.getMonth() + 1}`.padStart(2, "0")}`;

    const y2y = resolveTimePeriod(TimePeriod.Y2Y).primary!;

    test("renders a button per shared time period bucket", () => {
      render(<ProductStatisticsFilter {...baseProps} />);

      for (const period of [
        TimePeriod.Y2Y,
        TimePeriod.PreviousQuarter,
        TimePeriod.FutureQuarter,
        TimePeriod.PreviousSeason,
        TimePeriod.Q9M,
      ]) {
        expect(
          screen.getByRole("button", {
            name: getTimePeriodDisplayText(period),
          }),
        ).toBeInTheDocument();
      }
    });

    test("applies the month of each end of the picked bucket", () => {
      const onDateFromChange = jest.fn();
      const onDateToChange = jest.fn();
      render(
        <ProductStatisticsFilter
          {...baseProps}
          onDateFromChange={onDateFromChange}
          onDateToChange={onDateToChange}
        />,
      );

      fireEvent.click(
        screen.getByRole("button", {
          name: getTimePeriodDisplayText(TimePeriod.Y2Y),
        }),
      );

      expect(onDateFromChange).toHaveBeenCalledWith(monthOf(y2y.from));
      expect(onDateToChange).toHaveBeenCalledWith(monthOf(y2y.to));
    });

    test("marks the bucket matching the current range as pressed", () => {
      render(
        <ProductStatisticsFilter
          {...baseProps}
          dateFrom={monthOf(y2y.from)}
          dateTo={monthOf(y2y.to)}
        />,
      );

      expect(
        screen.getByRole("button", {
          name: getTimePeriodDisplayText(TimePeriod.Y2Y),
        }),
      ).toHaveAttribute("aria-pressed", "true");
      expect(
        screen.getByRole("button", {
          name: getTimePeriodDisplayText(TimePeriod.PreviousSeason),
        }),
      ).toHaveAttribute("aria-pressed", "false");
    });
  });

  describe("catalog picker", () => {
    test("opens the picker modal", () => {
      render(<ProductStatisticsFilter {...baseProps} />);

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: /Vybrat z katalogu/ }));

      expect(
        screen.getByRole("dialog", { name: "Vybrat produkty" }),
      ).toBeInTheDocument();
    });

    test("confirming the picker applies its selection", () => {
      const onProductsChange = jest.fn();
      render(
        <ProductStatisticsFilter
          {...baseProps}
          selectedProducts={[
            { productCode: "AKL027", productName: "Demineralizovaná voda" },
          ]}
          onProductsChange={onProductsChange}
        />,
      );

      fireEvent.click(screen.getByRole("button", { name: /Vybrat z katalogu/ }));
      fireEvent.click(
        screen.getByRole("checkbox", { name: "Demineralizovaná voda (AKL027)" }),
      );
      fireEvent.click(screen.getByRole("button", { name: "Potvrdit" }));

      expect(onProductsChange).toHaveBeenCalledWith([]);
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
  });
});
