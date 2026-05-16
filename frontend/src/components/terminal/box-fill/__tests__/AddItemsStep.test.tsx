import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import AddItemsStep from "../AddItemsStep";
import * as inventoryHook from "../../../../api/hooks/useManufacturedProductInventory";
import * as useBoxFill from "../../../../api/hooks/useBoxFill";

jest.mock("../../../../utils/errorHandler", () => ({ getErrorMessage: () => "Chyba" }));

const inventoryItem = {
  id: 7, productCode: "P-1", productName: "Krém", amount: 10, createdAt: "", createdBy: "", log: [],
};

const box: useBoxFill.TerminalBox = { id: 1, code: "B001", state: "Opened", itemCount: 0, items: [] };

const addMutateAsync = jest.fn();
const removeMutateAsync = jest.fn();

beforeEach(() => {
  addMutateAsync.mockReset();
  removeMutateAsync.mockReset();
  jest.spyOn(inventoryHook, "useManufacturedProductInventoryQuery").mockReturnValue({
    data: { items: [inventoryItem], totalCount: 1 }, isLoading: false, error: null,
  } as unknown as ReturnType<typeof inventoryHook.useManufacturedProductInventoryQuery>);
  jest.spyOn(useBoxFill, "useAddBoxItem").mockReturnValue({
    mutateAsync: addMutateAsync, isPending: false,
  } as unknown as ReturnType<typeof useBoxFill.useAddBoxItem>);
  jest.spyOn(useBoxFill, "useRemoveBoxItem").mockReturnValue({
    mutateAsync: removeMutateAsync, isPending: false,
  } as unknown as ReturnType<typeof useBoxFill.useRemoveBoxItem>);
});

describe("AddItemsStep", () => {
  it("disables the proceed button while the box is empty", () => {
    render(
      <AddItemsStep box={box} resumed={false} amountMemory={{}} onBoxUpdated={jest.fn()} onAmountUsed={jest.fn()} onProceed={jest.fn()} />,
    );
    expect(screen.getByTestId("proceed-to-transit")).toBeDisabled();
  });

  it("adds an in-stock item and reports the box update and used amount", async () => {
    const updatedBox = { ...box, itemCount: 1, items: [{ id: 5, productCode: "P-1", productName: "Krém", amount: 2 }] };
    addMutateAsync.mockResolvedValue({ success: true, transportBox: updatedBox });
    const onBoxUpdated = jest.fn();
    const onAmountUsed = jest.fn();

    render(
      <AddItemsStep box={box} resumed={false} amountMemory={{}} onBoxUpdated={onBoxUpdated} onAmountUsed={onAmountUsed} onProceed={jest.fn()} />,
    );

    fireEvent.click(screen.getByTestId("inventory-row-7"));
    fireEvent.change(screen.getByTestId("amount-entry-input"), { target: { value: "2" } });
    fireEvent.click(screen.getByTestId("amount-entry-confirm"));

    await waitFor(() => expect(onBoxUpdated).toHaveBeenCalledWith(updatedBox));
    expect(onAmountUsed).toHaveBeenCalledWith("P-1", 2);
    expect(addMutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ boxId: 1, productCode: "P-1", amount: 2, sourceInventoryId: 7, allowNegativeStock: false }),
    );
  });

  it("opens the overdraft sheet when the amount exceeds stock", () => {
    render(
      <AddItemsStep box={box} resumed={false} amountMemory={{}} onBoxUpdated={jest.fn()} onAmountUsed={jest.fn()} onProceed={jest.fn()} />,
    );

    fireEvent.click(screen.getByTestId("inventory-row-7"));
    fireEvent.change(screen.getByTestId("amount-entry-input"), { target: { value: "25" } });
    fireEvent.click(screen.getByTestId("amount-entry-confirm"));

    expect(screen.getByTestId("overdraft-add-negative")).toBeInTheDocument();
    expect(addMutateAsync).not.toHaveBeenCalled();
  });
});
