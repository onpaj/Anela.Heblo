import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import MarketingActionFilters, { EMPTY_FILTERS, type MarketingFilters } from "../MarketingActionFilters";
import { MarketingActionType } from "../../../../api/generated/api-client";

const noop = () => {};

function renderFilters(overrides: Partial<{
  filters: MarketingFilters;
  onChange: (f: MarketingFilters) => void;
  onClear: () => void;
}> = {}) {
  const props = {
    filters: overrides.filters ?? EMPTY_FILTERS,
    onChange: overrides.onChange ?? noop,
    onClear: overrides.onClear ?? noop,
  };
  return render(<MarketingActionFilters {...props} />);
}

describe("MarketingActionFilters — Typ akce dropdown", () => {
  it("renders the dropdown with 'Všechny typy' as the default option label", () => {
    renderFilters();
    const select = screen.getByLabelText("Typ akce") as HTMLSelectElement;
    expect(select).toBeInTheDocument();
    expect(select.value).toBe("");
    expect(screen.getByRole("option", { name: "Všechny typy" })).toBeInTheDocument();
  });

  it("renders all six action-type options with Czech labels", () => {
    renderFilters();
    expect(screen.getByRole("option", { name: "Sociální sítě" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Blog" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Newsletter" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "PR" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Událost" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Meeting" })).toBeInTheDocument();
  });

  it("renders Typ akce as the first control, left of the search input", () => {
    renderFilters();
    const select = screen.getByLabelText("Typ akce");
    const search = screen.getByPlaceholderText("Hledat název...");
    expect(select.compareDocumentPosition(search) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("calls onChange with actionType set when an option is selected", () => {
    const onChange = jest.fn();
    renderFilters({ onChange });
    const select = screen.getByLabelText("Typ akce");
    fireEvent.change(select, { target: { value: MarketingActionType.Blog } });
    expect(onChange).toHaveBeenCalledWith({
      ...EMPTY_FILTERS,
      actionType: MarketingActionType.Blog,
    });
  });

  it("calls onChange with actionType cleared when 'Všechny typy' is re-selected", () => {
    const onChange = jest.fn();
    renderFilters({
      filters: { ...EMPTY_FILTERS, actionType: MarketingActionType.Blog },
      onChange,
    });
    const select = screen.getByLabelText("Typ akce");
    fireEvent.change(select, { target: { value: "" } });
    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_FILTERS, actionType: "" });
  });

  it("shows 'Zrušit filtry' when only actionType is set and calls onClear when clicked", () => {
    const onClear = jest.fn();
    renderFilters({
      filters: { ...EMPTY_FILTERS, actionType: MarketingActionType.PR },
      onClear,
    });
    const clearBtn = screen.getByRole("button", { name: /Zrušit filtry/ });
    fireEvent.click(clearBtn);
    expect(onClear).toHaveBeenCalledTimes(1);
  });

  it("hides 'Zrušit filtry' when all filters are empty", () => {
    renderFilters();
    expect(screen.queryByRole("button", { name: /Zrušit filtry/ })).not.toBeInTheDocument();
  });

  it("EMPTY_FILTERS includes actionType as empty string", () => {
    expect(EMPTY_FILTERS).toEqual({
      searchText: "",
      dateFrom: "",
      dateTo: "",
      actionType: "",
    });
  });
});
