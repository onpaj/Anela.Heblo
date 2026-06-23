import React, { useState } from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import {
  PurchasePlanningListProvider,
  usePurchasePlanningList,
} from "../PurchasePlanningListContext";

const TestComponent: React.FC = () => {
  const { items, addItem, removeItem, clearList, hasItems } =
    usePurchasePlanningList();
  const [codeInput, setCodeInput] = useState("");

  return (
    <div>
      <div data-testid="has-items">{hasItems ? "yes" : "no"}</div>
      <div data-testid="items-count">{items.length}</div>
      <ul>
        {items.map((item) => (
          <li key={item.productCode} data-testid={`item-${item.productCode}`}>
            <span data-testid={`item-${item.productCode}-name`}>
              {item.productName}
            </span>
            <span data-testid={`item-${item.productCode}-supplier`}>
              {item.supplier}
            </span>
            <span data-testid={`item-${item.productCode}-supplierCode`}>
              {item.supplierCode}
            </span>
            <button onClick={() => removeItem(item.productCode)}>Remove</button>
          </li>
        ))}
      </ul>
      <input
        data-testid="code-input"
        value={codeInput}
        onChange={(e) => setCodeInput(e.target.value)}
      />
      <button
        data-testid="add-item"
        onClick={() =>
          addItem({
            code: codeInput,
            name: `Product ${codeInput}`,
            supplier: "Test Supplier",
            supplierCode: `SC-${codeInput}`,
          })
        }
      >
        Add Item
      </button>
      <button data-testid="clear-list" onClick={clearList}>
        Clear List
      </button>
      <button
        data-testid="remove-unknown"
        onClick={() => removeItem("UNKNOWN")}
      >
        Remove Unknown
      </button>
    </div>
  );
};

const renderWithProvider = () =>
  render(
    <PurchasePlanningListProvider>
      <TestComponent />
    </PurchasePlanningListProvider>
  );

describe("PurchasePlanningListContext", () => {
  it("should not add duplicate items", () => {
    renderWithProvider();

    fireEvent.change(screen.getByTestId("code-input"), {
      target: { value: "MAT01" },
    });
    fireEvent.click(screen.getByTestId("add-item"));
    fireEvent.click(screen.getByTestId("add-item"));

    expect(screen.getByTestId("items-count")).toHaveTextContent("1");
    expect(screen.getByTestId("item-MAT01")).toBeInTheDocument();
  });

  it("should enforce maximum capacity of 20 items", () => {
    renderWithProvider();

    for (let i = 1; i <= 20; i++) {
      fireEvent.change(screen.getByTestId("code-input"), {
        target: { value: `MAT${i}` },
      });
      fireEvent.click(screen.getByTestId("add-item"));
    }
    expect(screen.getByTestId("items-count")).toHaveTextContent("20");

    fireEvent.change(screen.getByTestId("code-input"), {
      target: { value: "MAT21" },
    });
    fireEvent.click(screen.getByTestId("add-item"));

    expect(screen.getByTestId("items-count")).toHaveTextContent("20");
    expect(screen.queryByTestId("item-MAT21")).not.toBeInTheDocument();
  });

  it("should add a single item with correct field values", () => {
    renderWithProvider();

    fireEvent.change(screen.getByTestId("code-input"), {
      target: { value: "MAT01" },
    });
    fireEvent.click(screen.getByTestId("add-item"));

    expect(screen.getByTestId("items-count")).toHaveTextContent("1");
    expect(screen.getByTestId("has-items")).toHaveTextContent("yes");
    expect(screen.getByTestId("item-MAT01")).toBeInTheDocument();
    expect(screen.getByTestId("item-MAT01-name")).toHaveTextContent(
      "Product MAT01"
    );
    expect(screen.getByTestId("item-MAT01-supplier")).toHaveTextContent(
      "Test Supplier"
    );
    expect(screen.getByTestId("item-MAT01-supplierCode")).toHaveTextContent(
      "SC-MAT01"
    );
  });

  it("should remove an existing item from the list", () => {
    renderWithProvider();

    fireEvent.change(screen.getByTestId("code-input"), {
      target: { value: "MAT01" },
    });
    fireEvent.click(screen.getByTestId("add-item"));
    expect(screen.getByTestId("items-count")).toHaveTextContent("1");

    fireEvent.click(screen.getByText("Remove"));

    expect(screen.getByTestId("items-count")).toHaveTextContent("0");
    expect(screen.getByTestId("has-items")).toHaveTextContent("no");
  });

  it("should not throw and should leave list unchanged when removing a non-existent code", () => {
    renderWithProvider();

    fireEvent.change(screen.getByTestId("code-input"), {
      target: { value: "MAT01" },
    });
    fireEvent.click(screen.getByTestId("add-item"));
    expect(screen.getByTestId("items-count")).toHaveTextContent("1");

    // Invoke removeItem with a code not in the list — exercises the no-op branch.
    fireEvent.click(screen.getByTestId("remove-unknown"));

    expect(screen.getByTestId("items-count")).toHaveTextContent("1");
    expect(screen.getByTestId("item-MAT01")).toBeInTheDocument();
  });

  it("should clear all items from the list", () => {
    renderWithProvider();

    fireEvent.change(screen.getByTestId("code-input"), {
      target: { value: "MAT01" },
    });
    fireEvent.click(screen.getByTestId("add-item"));
    fireEvent.change(screen.getByTestId("code-input"), {
      target: { value: "MAT02" },
    });
    fireEvent.click(screen.getByTestId("add-item"));
    expect(screen.getByTestId("items-count")).toHaveTextContent("2");

    fireEvent.click(screen.getByTestId("clear-list"));

    expect(screen.getByTestId("items-count")).toHaveTextContent("0");
    expect(screen.getByTestId("has-items")).toHaveTextContent("no");
  });

  it("should throw when used outside PurchasePlanningListProvider", () => {
    const consoleSpy = jest
      .spyOn(console, "error")
      .mockImplementation(() => {});

    expect(() => {
      render(<TestComponent />);
    }).toThrow(
      "usePurchasePlanningList must be used within a PurchasePlanningListProvider"
    );

    consoleSpy.mockRestore();
  });
});
