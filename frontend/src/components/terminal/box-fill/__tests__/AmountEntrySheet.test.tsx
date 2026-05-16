import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import AmountEntrySheet from "../AmountEntrySheet";
import type { ManufacturedProductInventoryItem } from "../../../../api/hooks/useManufacturedProductInventory";

const item: ManufacturedProductInventoryItem = {
  id: 7, productCode: "P-1", productName: "Krém", amount: 10, createdAt: "", createdBy: "", log: [],
};

describe("AmountEntrySheet", () => {
  it("prefills the initial amount and confirms a positive number", () => {
    const onConfirm = jest.fn();
    render(
      <AmountEntrySheet item={item} initialAmount={3} isSubmitting={false} onConfirm={onConfirm} onCancel={jest.fn()} />,
    );

    expect((screen.getByTestId("amount-entry-input") as HTMLInputElement).value).toBe("3");
    fireEvent.click(screen.getByTestId("amount-entry-confirm"));
    expect(onConfirm).toHaveBeenCalledWith(3);
  });

  it("shows an error and does not confirm a non-positive amount", () => {
    const onConfirm = jest.fn();
    render(
      <AmountEntrySheet item={item} isSubmitting={false} onConfirm={onConfirm} onCancel={jest.fn()} />,
    );

    fireEvent.click(screen.getByTestId("amount-entry-confirm"));
    expect(onConfirm).not.toHaveBeenCalled();
    expect(screen.getByText("Zadejte kladné číslo")).toBeInTheDocument();
  });
});
