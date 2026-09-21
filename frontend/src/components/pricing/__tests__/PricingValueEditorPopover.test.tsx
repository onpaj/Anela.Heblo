import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import PricingValueEditorPopover from "../PricingValueEditorPopover";
import { PricingCellFacts } from "../pricingDelta";
import { PricingEditField } from "../../../api/generated/api-client";

const productCode = "PROD001";

const facts = (overrides: Partial<PricingCellFacts> = {}): PricingCellFacts => ({
  kind: "currency",
  baseline: 420,
  effective: 420,
  delta: 0,
  relativeChange: 0,
  ...overrides,
});

const renderPopover = (
  props: Partial<React.ComponentProps<typeof PricingValueEditorPopover>> = {},
) => {
  const onApply = jest.fn();
  const onClose = jest.fn();
  render(
    <PricingValueEditorPopover
      productCode={productCode}
      field={PricingEditField.Price}
      facts={facts()}
      onApply={onApply}
      onClose={onClose}
      {...props}
    />,
  );
  return { onApply, onClose };
};

const valueInput = (field = PricingEditField.Price) =>
  screen.getByTestId(`pricing-editor-value-${productCode}-${field}`);
const relativeInput = (field = PricingEditField.Price) =>
  screen.getByTestId(`pricing-editor-relative-${productCode}-${field}`);
const applyButton = (field = PricingEditField.Price) =>
  screen.getByTestId(`pricing-editor-apply-${productCode}-${field}`);
const cancelButton = (field = PricingEditField.Price) =>
  screen.getByTestId(`pricing-editor-cancel-${productCode}-${field}`);

describe("PricingValueEditorPopover", () => {
  it("opens on the cell's current value with its change against the real state", () => {
    renderPopover({
      facts: facts({ effective: 504, delta: 84, relativeChange: 20 }),
    });

    expect(valueInput()).toHaveValue("504");
    expect(relativeInput()).toHaveValue("20");
  });

  it("derives the value from a percentage change typed against the real state", () => {
    renderPopover();

    fireEvent.change(relativeInput(), { target: { value: "5" } });

    // 420 + 5 % = 441, computed from the BASELINE so retyping 5 never compounds.
    expect(valueInput()).toHaveValue("441");
  });

  it("derives the percentage change from a value typed directly", () => {
    renderPopover();

    fireEvent.change(valueInput(), { target: { value: "504" } });

    expect(relativeInput()).toHaveValue("20");
  });

  it("anchors a second relative edit to the real state, not to the value it just produced", () => {
    renderPopover();

    fireEvent.change(relativeInput(), { target: { value: "5" } });
    fireEvent.change(relativeInput(), { target: { value: "10" } });

    // 462, not 441 * 1.1 = 485.10.
    expect(valueInput()).toHaveValue("462");
  });

  it("adds percentage points rather than a ratio on a margin percentage cell", () => {
    renderPopover({
      field: PricingEditField.M0Percentage,
      facts: facts({ kind: "percentage", baseline: 58.33, effective: 58.33 }),
    });

    fireEvent.change(relativeInput(PricingEditField.M0Percentage), {
      target: { value: "5" },
    });

    expect(valueInput(PricingEditField.M0Percentage)).toHaveValue("63.33");
  });

  it("labels the relative field by its unit so the two are never confused", () => {
    const { unmount } = render(
      <PricingValueEditorPopover
        productCode={productCode}
        field={PricingEditField.Price}
        facts={facts()}
        onApply={jest.fn()}
        onClose={jest.fn()}
      />,
    );
    expect(screen.getByText("Změna %")).toBeInTheDocument();
    unmount();

    render(
      <PricingValueEditorPopover
        productCode={productCode}
        field={PricingEditField.M0Percentage}
        facts={facts({ kind: "percentage", baseline: 58.33, effective: 58.33 })}
        onApply={jest.fn()}
        onClose={jest.fn()}
      />,
    );
    expect(screen.getByText("Změna (p.b.)")).toBeInTheDocument();
  });

  it("accepts a Czech decimal comma in both fields", () => {
    const { onApply } = renderPopover();

    fireEvent.change(valueInput(), { target: { value: "441,50" } });
    fireEvent.click(applyButton());

    expect(onApply).toHaveBeenCalledWith(441.5);
  });

  it("accepts an explicitly signed relative change", () => {
    renderPopover();

    fireEvent.change(relativeInput(), { target: { value: "-3" } });

    expect(valueInput()).toHaveValue("407.4");
  });

  it("commits once on Použít and closes", () => {
    const { onApply, onClose } = renderPopover();

    fireEvent.change(valueInput(), { target: { value: "500" } });
    fireEvent.click(applyButton());

    expect(onApply).toHaveBeenCalledTimes(1);
    expect(onApply).toHaveBeenCalledWith(500);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("commits on Enter", () => {
    const { onApply } = renderPopover();

    fireEvent.change(valueInput(), { target: { value: "500" } });
    fireEvent.keyDown(valueInput(), { key: "Enter" });

    expect(onApply).toHaveBeenCalledWith(500);
  });

  it("discards a typed value on Zrušit", () => {
    const { onApply, onClose } = renderPopover();

    fireEvent.change(valueInput(), { target: { value: "500" } });
    fireEvent.click(cancelButton());

    expect(onApply).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("discards a typed value on Escape", () => {
    const { onApply, onClose } = renderPopover();

    fireEvent.change(valueInput(), { target: { value: "500" } });
    fireEvent.keyDown(valueInput(), { key: "Escape" });

    expect(onApply).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("discards on a click outside rather than committing a stray click", () => {
    const { onApply, onClose } = renderPopover();

    fireEvent.change(valueInput(), { target: { value: "500" } });
    fireEvent.mouseDown(document.body);

    expect(onApply).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("does not commit a value that is not a number", () => {
    const { onApply } = renderPopover();

    fireEvent.change(valueInput(), { target: { value: "abc" } });
    fireEvent.click(applyButton());

    expect(onApply).not.toHaveBeenCalled();
  });

  it("does not commit a value equal to the one already shown", () => {
    const { onApply, onClose } = renderPopover({
      facts: facts({ effective: 420 }),
    });

    fireEvent.click(applyButton());

    // Nothing changed, so there is no edit to post -- posting one would flag the row
    // as edited and pull it into the ceník export from a gesture that changed nothing.
    expect(onApply).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("offers no relative editing when the real state is zero", () => {
    renderPopover({
      field: PricingEditField.ManufacturingCost,
      facts: facts({ baseline: 0, effective: 0, relativeChange: null }),
    });

    // There is no multiplier that moves zero, so the field would be a trap.
    expect(relativeInput(PricingEditField.ManufacturingCost)).toBeDisabled();
    // The absolute value is still perfectly editable.
    expect(valueInput(PricingEditField.ManufacturingCost)).toBeEnabled();
  });

  it("shows the real state it is measuring against", () => {
    renderPopover({ facts: facts({ effective: 504, delta: 84, relativeChange: 20 }) });

    expect(
      screen.getByTestId(`pricing-editor-baseline-${productCode}-${PricingEditField.Price}`),
    ).toHaveTextContent("420");
  });
});
