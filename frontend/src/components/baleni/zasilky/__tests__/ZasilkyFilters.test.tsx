import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { ZasilkyFilters } from "../ZasilkyFilters";

const emptyFilters = {
  orderCode: "",
  customerName: "",
  packageNumber: "",
  carrier: "",
  fromDate: "",
  toDate: "",
};

describe("ZasilkyFilters", () => {
  it("renders all filter inputs and the search button", () => {
    render(<ZasilkyFilters value={emptyFilters} onChange={jest.fn()} />);

    expect(screen.getByPlaceholderText("Objednávka")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Zákazník")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Číslo balíku")).toBeInTheDocument();
    expect(screen.getByRole("combobox")).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Všichni dopravci" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Zásilkovna" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Osobní odběr" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Hledat" })).toBeInTheDocument();
  });

  it("does not call onChange while typing (no auto-search)", () => {
    const onChange = jest.fn();
    render(<ZasilkyFilters value={emptyFilters} onChange={onChange} />);

    fireEvent.change(screen.getByPlaceholderText("Objednávka"), {
      target: { value: "ORD-1" },
    });

    expect(onChange).not.toHaveBeenCalled();
  });

  it("calls onChange with current values when Hledat button is clicked", () => {
    const onChange = jest.fn();
    render(<ZasilkyFilters value={emptyFilters} onChange={onChange} />);

    fireEvent.change(screen.getByPlaceholderText("Objednávka"), {
      target: { value: "ORD-1" },
    });
    fireEvent.change(screen.getByPlaceholderText("Zákazník"), {
      target: { value: "Alice" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Hledat" }));

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ orderCode: "ORD-1", customerName: "Alice" }),
    );
  });

  it("calls onChange when form is submitted via Enter", () => {
    const onChange = jest.fn();
    render(<ZasilkyFilters value={emptyFilters} onChange={onChange} />);

    const input = screen.getByPlaceholderText("Číslo balíku");
    fireEvent.change(input, { target: { value: "PKG-99" } });
    fireEvent.submit(input.closest("form")!);

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ packageNumber: "PKG-99" }),
    );
  });

  it("includes all field values in the onChange payload", () => {
    const onChange = jest.fn();
    render(<ZasilkyFilters value={emptyFilters} onChange={onChange} />);

    fireEvent.change(screen.getByPlaceholderText("Objednávka"), { target: { value: "O1" } });
    fireEvent.change(screen.getByPlaceholderText("Zákazník"), { target: { value: "Bob" } });
    fireEvent.change(screen.getByPlaceholderText("Číslo balíku"), { target: { value: "P1" } });
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "PPL" } });
    fireEvent.click(screen.getByRole("button", { name: "Hledat" }));

    expect(onChange).toHaveBeenCalledWith({
      orderCode: "O1",
      customerName: "Bob",
      packageNumber: "P1",
      carrier: "PPL",
      fromDate: "",
      toDate: "",
    });
  });
});
