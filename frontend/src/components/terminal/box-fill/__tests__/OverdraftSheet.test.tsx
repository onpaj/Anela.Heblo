import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import OverdraftSheet from "../OverdraftSheet";
import type { ManufacturedProductInventoryItem } from "../../../../api/hooks/useManufacturedProductInventory";

const item: ManufacturedProductInventoryItem = {
  id: 7, productCode: "P-1", productName: "Krém", amount: 4, createdAt: "", createdBy: "", log: [],
};

describe("OverdraftSheet", () => {
  it("offers negative-stock and remaining-only choices", () => {
    const onAddNegative = jest.fn();
    const onAddRemaining = jest.fn();
    render(
      <OverdraftSheet
        item={item}
        requestedAmount={10}
        isSubmitting={false}
        onAddNegative={onAddNegative}
        onAddRemaining={onAddRemaining}
        onCancel={jest.fn()}
      />,
    );

    fireEvent.click(screen.getByTestId("overdraft-add-negative"));
    fireEvent.click(screen.getByTestId("overdraft-add-remaining"));
    expect(onAddNegative).toHaveBeenCalledTimes(1);
    expect(onAddRemaining).toHaveBeenCalledTimes(1);
  });
});
