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

  // M0Percentage/M1Percentage are plain decimal divisions server-side, so a normal
  // row (P=499, Cm=175) arrives as 64.92985971943888 and used to be rendered raw
  // inside a 64px input.
  it("renders a full-precision percentage rounded to two decimals", () => {
    render(
      <PricingEditableCell
        value={64.92985971943888}
        field={PricingEditField.M0Percentage}
        productCode={productCode}
        onCommit={jest.fn()}
      />,
    );

    const input = screen.getByTestId(
      `pricing-cell-${productCode}-${PricingEditField.M0Percentage}`,
    ) as HTMLInputElement;
    expect(input.value).toBe("64.93");
  });

  it("renders a whole amount without trailing zeros", () => {
    render(
      <PricingEditableCell
        value={500}
        field={PricingEditField.Price}
        productCode={productCode}
        onCommit={jest.fn()}
      />,
    );

    const input = screen.getByTestId(
      `pricing-cell-${productCode}-${PricingEditField.Price}`,
    ) as HTMLInputElement;
    expect(input.value).toBe("500");
  });

  it("resyncs to the rounded form when the value prop changes while unfocused", () => {
    const { rerender } = render(
      <PricingEditableCell
        value={80}
        field={PricingEditField.M0Percentage}
        productCode={productCode}
        onCommit={jest.fn()}
      />,
    );

    rerender(
      <PricingEditableCell
        value={82.857142857142857}
        field={PricingEditField.M0Percentage}
        productCode={productCode}
        onCommit={jest.fn()}
      />,
    );

    const input = screen.getByTestId(
      `pricing-cell-${productCode}-${PricingEditField.M0Percentage}`,
    ) as HTMLInputElement;
    expect(input.value).toBe("82.86");
  });

  it("never commits the rounded display value on its own -- only a real keystroke can commit", () => {
    const onCommit = jest.fn();
    render(
      <PricingEditableCell
        value={64.92985971943888}
        field={PricingEditField.M0Percentage}
        productCode={productCode}
        onCommit={onCommit}
      />,
    );

    const input = screen.getByTestId(
      `pricing-cell-${productCode}-${PricingEditField.M0Percentage}`,
    ) as HTMLInputElement;

    // The draft ("64.93") genuinely differs from the underlying value
    // (64.92985971943888). Focusing and leaving must still post nothing: the
    // dirty flag is set only by onChange, so a rounded display value can never
    // round-trip back to the server as if the user had typed it.
    expect(input.value).toBe("64.93");
    fireEvent.focus(input);
    fireEvent.blur(input);

    expect(onCommit).not.toHaveBeenCalled();
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
