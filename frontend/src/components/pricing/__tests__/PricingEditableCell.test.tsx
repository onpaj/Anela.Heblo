import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import PricingEditableCell from "../PricingEditableCell";
import { PricingEditField } from "../../../api/generated/api-client";

describe("PricingEditableCell", () => {
  const productCode = "PROD001";

  it("does not fire onCommit while typing, only on blur", () => {
    const onCommit = jest.fn();
    render(
      <PricingEditableCell
        value={100}
        field={PricingEditField.Price}
        productCode={productCode}
        onCommit={onCommit}
      />,
    );

    const input = screen.getByTestId(`pricing-cell-${productCode}-${PricingEditField.Price}`);
    fireEvent.change(input, { target: { value: "150" } });
    // Typing alone must never fire the mutation -- this is the whole "recalc on
    // blur" decision the design hinges on.
    expect(onCommit).not.toHaveBeenCalled();

    fireEvent.blur(input);
    expect(onCommit).toHaveBeenCalledTimes(1);
    expect(onCommit).toHaveBeenCalledWith(productCode, PricingEditField.Price, 150);
  });

  it("commits on Enter", () => {
    const onCommit = jest.fn();
    render(
      <PricingEditableCell
        value={100}
        field={PricingEditField.Price}
        productCode={productCode}
        onCommit={onCommit}
      />,
    );

    const input = screen.getByTestId(`pricing-cell-${productCode}-${PricingEditField.Price}`);
    fireEvent.change(input, { target: { value: "200" } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(onCommit).toHaveBeenCalledTimes(1);
    expect(onCommit).toHaveBeenCalledWith(productCode, PricingEditField.Price, 200);

    // Enter also blurs the field for good UX; that blur must not re-fire the
    // same commit a second time.
    fireEvent.blur(input);
    expect(onCommit).toHaveBeenCalledTimes(1);
  });

  it("reverts to the previous value on Escape and fires nothing", () => {
    const onCommit = jest.fn();
    render(
      <PricingEditableCell
        value={100}
        field={PricingEditField.Price}
        productCode={productCode}
        onCommit={onCommit}
      />,
    );

    const input = screen.getByTestId(
      `pricing-cell-${productCode}-${PricingEditField.Price}`,
    ) as HTMLInputElement;
    fireEvent.change(input, { target: { value: "999" } });
    fireEvent.keyDown(input, { key: "Escape" });

    expect(input.value).toBe("100");
    expect(onCommit).not.toHaveBeenCalled();

    // A blur after Escape must still fire nothing -- the draft was already
    // reverted to match the committed value.
    fireEvent.blur(input);
    expect(onCommit).not.toHaveBeenCalled();
  });

  it("fires nothing when committing an unchanged value", () => {
    const onCommit = jest.fn();
    render(
      <PricingEditableCell
        value={100}
        field={PricingEditField.Price}
        productCode={productCode}
        onCommit={onCommit}
      />,
    );

    const input = screen.getByTestId(`pricing-cell-${productCode}-${PricingEditField.Price}`);
    // Focus, retype the exact same value, blur.
    fireEvent.change(input, { target: { value: "100" } });
    fireEvent.blur(input);

    expect(onCommit).not.toHaveBeenCalled();
  });

  it("accepts a Czech decimal comma", () => {
    const onCommit = jest.fn();
    render(
      <PricingEditableCell
        value={100}
        field={PricingEditField.M0Percentage}
        productCode={productCode}
        onCommit={onCommit}
      />,
    );

    const input = screen.getByTestId(
      `pricing-cell-${productCode}-${PricingEditField.M0Percentage}`,
    );
    fireEvent.change(input, { target: { value: "45,5" } });
    fireEvent.blur(input);

    expect(onCommit).toHaveBeenCalledWith(productCode, PricingEditField.M0Percentage, 45.5);
  });

  it("shows the error message on the cell when an error prop is set", () => {
    render(
      <PricingEditableCell
        value={100}
        field={PricingEditField.Price}
        productCode={productCode}
        onCommit={jest.fn()}
        error="Cena musí být větší než nula"
      />,
    );

    expect(
      screen.getByTestId(`pricing-cell-error-${productCode}-${PricingEditField.Price}`),
    ).toHaveTextContent("Cena musí být větší než nula");
  });

  it("renders no error element when error is not set", () => {
    render(
      <PricingEditableCell
        value={100}
        field={PricingEditField.Price}
        productCode={productCode}
        onCommit={jest.fn()}
      />,
    );

    expect(
      screen.queryByTestId(`pricing-cell-error-${productCode}-${PricingEditField.Price}`),
    ).not.toBeInTheDocument();
  });
});
