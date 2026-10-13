import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { PricingEditField } from "../../../api/generated/api-client";
import PricingBulkEditHeader from "../PricingBulkEditHeader";

const renderHeader = (
  props: Partial<React.ComponentProps<typeof PricingBulkEditHeader>> = {},
) => {
  const onApply = jest.fn();
  render(
    <PricingBulkEditHeader
      field={PricingEditField.Price}
      label="Cena"
      productCount={3}
      onApply={onApply}
      {...props}
    />,
  );
  return { onApply };
};

const trigger = () => screen.getByTestId(`pricing-bulk-edit-${PricingEditField.Price}`);
const editor = () => screen.queryByTestId("pricing-bulk-editor");

// A real pointer click dispatches mousedown before click. The popover dismisses
// itself on an outside mousedown, so anything that treats the trigger as "outside"
// closes the editor a beat before the trigger's own click reopens it.
const clickLikeAPointer = (element: HTMLElement) => {
  fireEvent.mouseDown(element);
  fireEvent.click(element);
};

describe("PricingBulkEditHeader", () => {
  it("opens the bulk editor from the column header", () => {
    // Arrange
    renderHeader();

    // Act
    clickLikeAPointer(trigger());

    // Assert
    expect(editor()).toBeInTheDocument();
    expect(trigger()).toHaveAttribute("aria-expanded", "true");
  });

  it("closes the bulk editor when the same header is clicked again", () => {
    // Arrange
    renderHeader();
    clickLikeAPointer(trigger());

    // Act
    clickLikeAPointer(trigger());

    // Assert
    expect(editor()).not.toBeInTheDocument();
    expect(trigger()).toHaveAttribute("aria-expanded", "false");
  });

  it("closes the bulk editor when the interaction moves elsewhere", () => {
    // Arrange
    renderHeader();
    clickLikeAPointer(trigger());

    // Act
    fireEvent.mouseDown(document.body);

    // Assert
    expect(editor()).not.toBeInTheDocument();
  });

  it("puts focus back on the header after a deliberate dismissal", () => {
    // Arrange: a keyboard user dropped on the document body loses their place in a
    // grid of hundreds of rows.
    renderHeader();
    clickLikeAPointer(trigger());

    // Act
    fireEvent.keyDown(screen.getByTestId("pricing-bulk-editor-amount"), {
      key: "Escape",
    });

    // Assert
    expect(trigger()).toHaveFocus();
  });

  it("reports how many products the change can land on", () => {
    // Arrange
    renderHeader({ productCount: 7 });

    // Act
    clickLikeAPointer(trigger());

    // Assert
    expect(screen.getByText(/7 produktů/)).toBeInTheDocument();
  });

  it("cannot be opened while editing is disabled", () => {
    // Arrange
    renderHeader({ disabled: true });

    // Act
    clickLikeAPointer(trigger());

    // Assert
    expect(editor()).not.toBeInTheDocument();
  });
});
