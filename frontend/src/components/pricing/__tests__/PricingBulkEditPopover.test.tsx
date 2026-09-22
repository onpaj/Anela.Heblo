import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { PricingEditField } from "../../../api/generated/api-client";
import PricingBulkEditPopover from "../PricingBulkEditPopover";

const renderPopover = (
  props: Partial<React.ComponentProps<typeof PricingBulkEditPopover>> = {},
) => {
  const onApply = jest.fn();
  const onClose = jest.fn();
  render(
    <PricingBulkEditPopover
      field={PricingEditField.Price}
      productCount={3}
      onApply={onApply}
      onClose={onClose}
      {...props}
    />,
  );
  return { onApply, onClose };
};

const amountInput = () => screen.getByTestId("pricing-bulk-editor-amount");

describe("PricingBulkEditPopover", () => {
  it("says which column and how many products the change will hit", () => {
    // Arrange / Act
    renderPopover();

    // Assert
    expect(screen.getByText(/Cena/)).toBeInTheDocument();
    expect(screen.getByText(/3 produkty/)).toBeInTheDocument();
  });

  it("steps the amount by one in both directions", () => {
    // Arrange / Act
    renderPopover();

    // Assert: a native number spinner, so the browser's own up/down arrows apply.
    expect(amountInput()).toHaveAttribute("type", "number");
    expect(amountInput()).toHaveAttribute("step", "1");
  });

  it("applies the typed percentage", () => {
    // Arrange
    const { onApply } = renderPopover();

    // Act
    fireEvent.change(amountInput(), { target: { value: "10" } });
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));

    // Assert
    expect(onApply).toHaveBeenCalledWith({
      field: PricingEditField.Price,
      percent: 10,
    });
  });

  it("applies a negative percentage as a cut", () => {
    // Arrange
    const { onApply } = renderPopover();

    // Act
    fireEvent.change(amountInput(), { target: { value: "-5" } });
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));

    // Assert
    expect(onApply).toHaveBeenCalledWith({
      field: PricingEditField.Price,
      percent: -5,
    });
  });

  it("takes zero as a change back to the original values", () => {
    // Arrange
    const { onApply } = renderPopover();

    // Act
    fireEvent.change(amountInput(), { target: { value: "0" } });
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));

    // Assert
    expect(onApply).toHaveBeenCalledWith({
      field: PricingEditField.Price,
      percent: 0,
    });
  });

  it("refuses an empty draft instead of posting a no-op", () => {
    // Arrange
    const { onApply, onClose } = renderPopover();

    // Act
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));

    // Assert: the editor stays open and says why, rather than reading as a dead button.
    expect(onApply).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByTestId("pricing-bulk-editor-error")).toHaveTextContent(
      /Zadejte změnu v procentech/,
    );
    expect(amountInput()).toHaveAttribute("aria-invalid", "true");
  });

  it("clears the complaint as soon as the draft is corrected", () => {
    // Arrange
    renderPopover();
    fireEvent.click(screen.getByTestId("pricing-bulk-editor-apply"));

    // Act
    fireEvent.change(amountInput(), { target: { value: "5" } });

    // Assert
    expect(
      screen.queryByTestId("pricing-bulk-editor-error"),
    ).not.toBeInTheDocument();
  });

  it("applies on Enter", () => {
    // Arrange
    const { onApply } = renderPopover();

    // Act
    fireEvent.change(amountInput(), { target: { value: "7" } });
    fireEvent.keyDown(amountInput(), { key: "Enter" });

    // Assert
    expect(onApply).toHaveBeenCalledWith(
      expect.objectContaining({ percent: 7 }),
    );
  });

  it("closes on Escape without applying", () => {
    // Arrange
    const { onApply, onClose } = renderPopover();

    // Act
    fireEvent.change(amountInput(), { target: { value: "7" } });
    fireEvent.keyDown(amountInput(), { key: "Escape" });

    // Assert
    expect(onApply).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledWith(true);
  });

  it("closes when the interaction moves outside the editor", () => {
    // Arrange: a stray click elsewhere in the grid must discard, never commit.
    const { onApply, onClose } = renderPopover();

    // Act
    fireEvent.mouseDown(document.body);

    // Assert
    expect(onApply).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledWith(false);
  });
});
