import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import PricingEditableCell from "../PricingEditableCell";
import { PricingEditField, PricingRowDto } from "../../../api/generated/api-client";

const productCode = "PROD001";

// Baseline: price 420, material 175, manufacturing 70, sold 1000.
// => baseline M0 = 245 (58.333 %), M1 = 175 (41.667 %)
const row = (overrides: Partial<PricingRowDto> = {}): PricingRowDto =>
  ({
    productCode,
    productName: "Product",
    baselinePrice: 420,
    baselineMaterialCost: 175,
    baselineManufacturingCost: 70,
    baselineQuantity: 1000,
    baselineM0Amount: 245,
    baselineM1Amount: 175,
    baselineM0Percentage: 58.33333333,
    baselineM1Percentage: 41.66666667,
    price: 420,
    materialCost: 175,
    manufacturingCost: 70,
    forecastQuantity: 1000,
    m0Amount: 245,
    m1Amount: 175,
    m0Percentage: 58.33333333,
    m1Percentage: 41.66666667,
    ...overrides,
  }) as PricingRowDto;

const renderCell = (
  props: Partial<React.ComponentProps<typeof PricingEditableCell>> = {},
) => {
  const onCommit = jest.fn();
  render(
    <PricingEditableCell
      row={row()}
      field={PricingEditField.Price}
      onCommit={onCommit}
      {...props}
    />,
  );
  return { onCommit };
};

const cell = (field = PricingEditField.Price) =>
  screen.getByTestId(`pricing-cell-${productCode}-${field}`);
const editor = (field = PricingEditField.Price) =>
  screen.queryByTestId(`pricing-editor-${productCode}-${field}`);
const valueInput = (field = PricingEditField.Price) =>
  screen.getByTestId(`pricing-editor-value-${productCode}-${field}`);

describe("PricingEditableCell", () => {
  it("formats its value according to the column it belongs to", () => {
    const { unmount } = render(
      <PricingEditableCell
        row={row()}
        field={PricingEditField.M0Percentage}
        onCommit={jest.fn()}
      />,
    );
    // A full-precision percentage must not be printed raw into the grid.
    expect(cell(PricingEditField.M0Percentage)).toHaveTextContent("58,33");
    unmount();

    render(
      <PricingEditableCell
        row={row()}
        field={PricingEditField.ForecastQuantity}
        onCommit={jest.fn()}
      />,
    );
    expect(cell(PricingEditField.ForecastQuantity)).toHaveTextContent("1 000");
  });

  it("opens the editor on click and commits what it applies", () => {
    const { onCommit } = renderCell();

    expect(editor()).not.toBeInTheDocument();
    fireEvent.click(cell());
    expect(editor()).toBeInTheDocument();

    fireEvent.change(
      screen.getByTestId(`pricing-editor-value-${productCode}-${PricingEditField.Price}`),
      { target: { value: "500" } },
    );
    fireEvent.click(
      screen.getByTestId(`pricing-editor-apply-${productCode}-${PricingEditField.Price}`),
    );

    expect(onCommit).toHaveBeenCalledTimes(1);
    expect(onCommit).toHaveBeenCalledWith(productCode, PricingEditField.Price, 500);
    // Applying closes the editor, so the next recalculation lands on a settled cell.
    expect(editor()).not.toBeInTheDocument();
  });

  it("opens the editor from the keyboard", () => {
    renderCell();

    fireEvent.keyDown(cell(), { key: "Enter" });

    expect(editor()).toBeInTheDocument();
  });

  it("closes the editor without committing when it is dismissed", () => {
    const { onCommit } = renderCell();

    fireEvent.click(cell());
    fireEvent.click(
      screen.getByTestId(`pricing-editor-cancel-${productCode}-${PricingEditField.Price}`),
    );

    expect(editor()).not.toBeInTheDocument();
    expect(onCommit).not.toHaveBeenCalled();
  });

  it("reports the change against the real state on a cell that moved", () => {
    renderCell({ row: row({ price: 504 }) });

    // 504 against a real 420: the tooltip is the whole "% change vs. real state"
    // affordance, so it must carry both the original value and the change.
    expect(cell()).toHaveAttribute("title", expect.stringContaining("420"));
    expect(cell().getAttribute("title")).toContain("20");
  });

  it("says nothing on a cell that matches the real state", () => {
    renderCell();

    expect(cell()).not.toHaveAttribute("title");
  });

  it("reports a margin ratio change in percentage points", () => {
    render(
      <PricingEditableCell
        row={row({ m0Percentage: 65 })}
        field={PricingEditField.M0Percentage}
        onCommit={jest.fn()}
      />,
    );

    expect(cell(PricingEditField.M0Percentage).getAttribute("title")).toContain("p.b.");
  });

  it("marks a cost increase as unfavourable and a price increase as favourable", () => {
    const { unmount } = render(
      <PricingEditableCell
        row={row({ price: 504 })}
        field={PricingEditField.Price}
        onCommit={jest.fn()}
      />,
    );
    // Up is good on a price...
    expect(cell().className).toContain("emerald");
    unmount();

    render(
      <PricingEditableCell
        row={row({ materialCost: 200 })}
        field={PricingEditField.MaterialCost}
        onCommit={jest.fn()}
      />,
    );
    // ...and bad on a cost. Colouring both green would be actively misleading.
    expect(cell(PricingEditField.MaterialCost).className).toContain("rose");
  });

  it("renders plain text with no editor when read-only", () => {
    const { onCommit } = renderCell({ readOnly: true });

    fireEvent.click(cell());

    expect(editor()).not.toBeInTheDocument();
    expect(onCommit).not.toHaveBeenCalled();
  });

  it("still reports the change against the real state when read-only", () => {
    renderCell({ row: row({ price: 504 }), readOnly: true });

    expect(cell().getAttribute("title")).toContain("420");
  });

  it("resyncs an open editor when a recalculation lands underneath it", () => {
    // A response for an edit made on ANOTHER cell can land while this editor is open.
    // The cell behind it re-renders from the new row, so an editor still showing the
    // pre-response number would commit a value the user never saw -- silently undoing
    // the recalculation they were about to accept.
    const onCommit = jest.fn();
    const { rerender } = render(
      <PricingEditableCell
        row={row()}
        field={PricingEditField.M0Amount}
        onCommit={onCommit}
      />,
    );

    fireEvent.click(cell(PricingEditField.M0Amount));
    expect(valueInput(PricingEditField.M0Amount)).toHaveValue("245");

    rerender(
      <PricingEditableCell
        row={row({ price: 520, m0Amount: 345 })}
        field={PricingEditField.M0Amount}
        onCommit={onCommit}
      />,
    );

    expect(valueInput(PricingEditField.M0Amount)).toHaveValue("345");
  });

  it("leaves an open editor's draft alone while the value underneath is unchanged", () => {
    // The counterpart to the resync above: an unrelated re-render must not wipe what
    // the user is typing.
    const onCommit = jest.fn();
    const { rerender } = render(
      <PricingEditableCell row={row()} field={PricingEditField.Price} onCommit={onCommit} />,
    );

    fireEvent.click(cell());
    fireEvent.change(valueInput(), { target: { value: "48" } });

    rerender(
      <PricingEditableCell
        row={row()}
        field={PricingEditField.Price}
        onCommit={onCommit}
        error="Cena musí být kladná"
      />,
    );

    expect(valueInput()).toHaveValue("48");
  });

  it("returns focus to the cell when the editor is dismissed", () => {
    // The grid runs to hundreds of rows, so a keyboard user who presses Escape must
    // land back on the cell they opened rather than on the document body.
    renderCell();

    fireEvent.click(cell());
    fireEvent.keyDown(editor()!, { key: "Escape" });

    expect(cell()).toHaveFocus();
  });

  it("closes the editor when focus moves to another cell", () => {
    // Without this, tabbing out of the editor onto the next cell and pressing Enter
    // leaves TWO editors mounted, the first one holding a now-stale draft.
    renderCell();

    fireEvent.click(cell());
    fireEvent.focusIn(document.body);

    expect(editor()).not.toBeInTheDocument();
  });

  it("shows a rejected edit's message on the cell", () => {
    renderCell({ error: "Cena musí být kladná" });

    expect(
      screen.getByTestId(`pricing-cell-error-${productCode}-${PricingEditField.Price}`),
    ).toHaveTextContent("Cena musí být kladná");
    expect(cell()).toHaveAttribute("aria-invalid", "true");
  });

  it("renders no error element when there is no error", () => {
    renderCell();

    expect(
      screen.queryByTestId(`pricing-cell-error-${productCode}-${PricingEditField.Price}`),
    ).not.toBeInTheDocument();
  });
});
