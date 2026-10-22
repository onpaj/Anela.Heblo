import React from "react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ManufactureOrderFilters from "../ManufactureOrderFilters";
import { ManufactureOrderState } from "../../../../api/generated/api-client";

jest.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

jest.mock("../../../../api/hooks/useManufactureSettings", () => ({
  useManufactureSettingsQuery: () => ({ data: { manufactureGroupId: "group-1" } }),
}));

jest.mock("../../../common/CatalogAutocomplete", () => ({
  __esModule: true,
  default: ({ value, onSelect, placeholder }: any) => (
    <input
      data-testid="catalog-autocomplete"
      placeholder={placeholder}
      value={value}
      onChange={(e) => onSelect(e.target.value || null)}
    />
  ),
}));

jest.mock("../../../common/ResponsiblePersonCombobox", () => ({
  __esModule: true,
  default: ({ value, onChange, placeholder }: any) => (
    <input
      data-testid="responsible-person"
      placeholder={placeholder}
      value={value}
      onChange={(e) => onChange(e.target.value || null)}
    />
  ),
}));

const NULL_FILTERS = {
  orderNumber: null,
  state: null,
  dateFrom: null,
  dateTo: null,
  responsiblePerson: null,
  productCode: null,
  erpDocumentNumber: null,
  manualActionRequired: null,
  lotNumber: null,
};

const renderFilters = () => {
  const onFiltersChange = jest.fn();
  const onApplyFilters = jest.fn().mockResolvedValue(undefined);
  render(
    <ManufactureOrderFilters
      onFiltersChange={onFiltersChange}
      onApplyFilters={onApplyFilters}
    />,
  );
  return { onFiltersChange, onApplyFilters };
};

const expand = () => fireEvent.click(screen.getByText("Filtry"));
const applyButton = () => screen.getByText("Použít filtry");

describe("ManufactureOrderFilters", () => {
  describe("apply (expanded)", () => {
    it("maps all filled fields into the request and calls onApplyFilters", async () => {
      const user = userEvent.setup();
      const { onFiltersChange, onApplyFilters } = renderFilters();
      expand();

      await user.type(screen.getByPlaceholderText("Číslo zakázky"), "MO-1");
      fireEvent.change(screen.getByDisplayValue("Všechny stavy"), {
        target: { value: ManufactureOrderState.Planned },
      });
      // Date inputs have no accessible label association, so query by type.
      const [dateFrom, dateTo] = Array.from(
        // eslint-disable-next-line testing-library/no-node-access
        document.querySelectorAll<HTMLInputElement>('input[type="date"]'),
      );
      fireEvent.change(dateFrom, { target: { value: "2024-01-15" } });
      fireEvent.change(dateTo, { target: { value: "2024-02-20" } });
      await user.type(screen.getByTestId("responsible-person"), "Jan");
      await user.type(screen.getByTestId("catalog-autocomplete"), "P001");
      await user.type(screen.getByPlaceholderText("ERP číslo dokladu"), "ERP-9");
      await user.type(screen.getByPlaceholderText("LOT / Šarže"), "LOT-7");

      await user.click(applyButton());

      expect(onFiltersChange).toHaveBeenCalledTimes(1);
      const request = onFiltersChange.mock.calls[0][0];
      expect(request).toEqual({
        orderNumber: "MO-1",
        state: ManufactureOrderState.Planned,
        dateFrom: new Date("2024-01-15"),
        dateTo: new Date("2024-02-20"),
        responsiblePerson: "Jan",
        productCode: "P001",
        erpDocumentNumber: "ERP-9",
        manualActionRequired: null,
        lotNumber: "LOT-7",
      });
      expect(request.dateFrom).toBeInstanceOf(Date);
      expect(request.dateTo).toBeInstanceOf(Date);
      expect(onApplyFilters).toHaveBeenCalledTimes(1);
    });

    it("applies when Enter is pressed in the order number input", async () => {
      const user = userEvent.setup();
      const { onFiltersChange, onApplyFilters } = renderFilters();
      expand();

      await user.type(screen.getByPlaceholderText("Číslo zakázky"), "MO-2{Enter}");

      expect(onFiltersChange).toHaveBeenCalledWith({ ...NULL_FILTERS, orderNumber: "MO-2" });
      expect(onApplyFilters).toHaveBeenCalledTimes(1);
    });

    it("sends all nine fields as null when nothing is entered", async () => {
      const user = userEvent.setup();
      const { onFiltersChange } = renderFilters();
      expand();

      await user.click(applyButton());

      expect(onFiltersChange).toHaveBeenCalledWith(NULL_FILTERS);
      expect(Object.keys(onFiltersChange.mock.calls[0][0])).toHaveLength(9);
    });

    it("sends productCode null after typing and then clearing it", async () => {
      const user = userEvent.setup();
      const { onFiltersChange } = renderFilters();
      expand();

      const product = screen.getByTestId("catalog-autocomplete");
      await user.type(product, "P001");
      await user.clear(product);
      await user.click(applyButton());

      expect(onFiltersChange).toHaveBeenCalledWith(NULL_FILTERS);
    });

    it("maps the state select: empty -> null, value -> enum", async () => {
      const user = userEvent.setup();
      const { onFiltersChange } = renderFilters();
      expand();
      const select = screen.getByDisplayValue("Všechny stavy");

      await user.click(applyButton());
      expect(onFiltersChange.mock.calls[0][0].state).toBeNull();

      fireEvent.change(select, { target: { value: ManufactureOrderState.Completed } });
      await user.click(applyButton());
      expect(onFiltersChange.mock.calls[1][0].state).toBe(ManufactureOrderState.Completed);

      fireEvent.change(select, { target: { value: "" } });
      await user.click(applyButton());
      expect(onFiltersChange.mock.calls[2][0].state).toBeNull();
    });

    it("maps the manual action select: true / false / empty", async () => {
      const user = userEvent.setup();
      const { onFiltersChange } = renderFilters();
      expand();
      const select = screen.getByDisplayValue("Vše") as HTMLSelectElement;

      fireEvent.change(select, { target: { value: "true" } });
      await user.click(applyButton());
      expect(onFiltersChange.mock.calls[0][0].manualActionRequired).toBe(true);

      fireEvent.change(select, { target: { value: "false" } });
      await user.click(applyButton());
      expect(onFiltersChange.mock.calls[1][0].manualActionRequired).toBe(false);

      fireEvent.change(select, { target: { value: "" } });
      await user.click(applyButton());
      expect(onFiltersChange.mock.calls[2][0].manualActionRequired).toBeNull();
    });
  });

  describe("collapsed header", () => {
    beforeEach(() => jest.useFakeTimers());
    afterEach(() => jest.useRealTimers());

    it("shows the default summary text", () => {
      renderFilters();
      expect(screen.getByText("Klikněte pro rozbalení filtrů")).toBeInTheDocument();
      expect(screen.getByText("Vše")).toBeInTheDocument();
    });

    it("cycles the tri-state toggle; the auto-apply sees the previous state (issue 4435)", () => {
      const { onFiltersChange } = renderFilters();
      const toggle = () => screen.getByTitle(/Aktuálně se zobrazují/);
      const clickAndFlush = () => {
        fireEvent.click(toggle());
        act(() => {
          jest.advanceTimersByTime(50);
        });
      };

      // setTimeout(handleApplyFilters) captures the handler from the render in
      // which the click happened, so each auto-apply sends the PREVIOUS input
      // value: null -> true -> false are applied one click late.
      clickAndFlush();
      expect(onFiltersChange).toHaveBeenLastCalledWith(NULL_FILTERS);

      clickAndFlush();
      expect(onFiltersChange).toHaveBeenLastCalledWith({ ...NULL_FILTERS, manualActionRequired: true });

      clickAndFlush();
      expect(onFiltersChange).toHaveBeenLastCalledWith({ ...NULL_FILTERS, manualActionRequired: false });

      expect(onFiltersChange).toHaveBeenCalledTimes(3);
    });

    it("shows 'Aktivní filtry' after a filter has been applied", () => {
      renderFilters();
      fireEvent.change(screen.getByTestId("catalog-autocomplete"), { target: { value: "P001" } });
      fireEvent.click(screen.getByText("Hledat produkt"));
      expect(screen.getByText("Aktivní filtry")).toBeInTheDocument();
    });
  });

  describe("reset", () => {
    it("clears inputs and sends an all-null request without applying", async () => {
      const user = userEvent.setup();
      const { onFiltersChange, onApplyFilters } = renderFilters();
      expand();

      await user.type(screen.getByPlaceholderText("Číslo zakázky"), "MO-1");
      await user.type(screen.getByPlaceholderText("LOT / Šarže"), "LOT-7");
      fireEvent.change(screen.getByDisplayValue("Všechny stavy"), {
        target: { value: ManufactureOrderState.Draft },
      });

      await user.click(screen.getByText("Vymazat filtry"));

      expect(onFiltersChange).toHaveBeenCalledTimes(1);
      expect(onFiltersChange).toHaveBeenCalledWith(NULL_FILTERS);
      expect(onApplyFilters).not.toHaveBeenCalled();
      expect(screen.getByPlaceholderText("Číslo zakázky")).toHaveValue("");
      expect(screen.getByPlaceholderText("LOT / Šarže")).toHaveValue("");
      expect(screen.getByDisplayValue("Všechny stavy")).toBeInTheDocument();
    });
  });
});
