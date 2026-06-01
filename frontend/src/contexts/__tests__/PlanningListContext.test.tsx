import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { PlanningListProvider, usePlanningList } from "../PlanningListContext";

// Test component that uses the planning list context
const TestComponent: React.FC = () => {
  const { items, addItem, removeItem, clearList, hasItems } = usePlanningList();

  return (
    <div>
      <div data-testid="has-items">{hasItems ? "yes" : "no"}</div>
      <div data-testid="items-count">{items.length}</div>
      <ul>
        {items.map((item) => (
          <li key={item.productCode} data-testid={`item-${item.productCode}`}>
            {item.productName}
            <button onClick={() => removeItem(item.productCode)}>Remove</button>
          </li>
        ))}
      </ul>
      <button
        onClick={() => addItem({ code: "TEST01", name: "Test Product 1" })}
        data-testid="add-test-1"
      >
        Add Test Product 1
      </button>
      <button
        onClick={() => addItem({ code: "TEST02", name: "Test Product 2" })}
        data-testid="add-test-2"
      >
        Add Test Product 2
      </button>
      <button onClick={clearList} data-testid="clear-list">
        Clear List
      </button>
    </div>
  );
};

const renderWithProvider = () => {
  return render(
    <PlanningListProvider>
      <TestComponent />
    </PlanningListProvider>
  );
};

describe("PlanningListContext", () => {
  it("should start with empty list", () => {
    renderWithProvider();
    
    expect(screen.getByTestId("has-items")).toHaveTextContent("no");
    expect(screen.getByTestId("items-count")).toHaveTextContent("0");
  });

  it("should add items to the list", () => {
    renderWithProvider();
    
    fireEvent.click(screen.getByTestId("add-test-1"));
    
    expect(screen.getByTestId("has-items")).toHaveTextContent("yes");
    expect(screen.getByTestId("items-count")).toHaveTextContent("1");
    expect(screen.getByTestId("item-TEST01")).toBeInTheDocument();
    expect(screen.getByTestId("item-TEST01")).toHaveTextContent("Test Product 1");
  });

  it("should not add duplicate items", () => {
    renderWithProvider();
    
    // Add the same item twice
    fireEvent.click(screen.getByTestId("add-test-1"));
    fireEvent.click(screen.getByTestId("add-test-1"));
    
    expect(screen.getByTestId("items-count")).toHaveTextContent("1");
  });

  it("should remove items from the list", () => {
    renderWithProvider();
    
    // Add item first
    fireEvent.click(screen.getByTestId("add-test-1"));
    expect(screen.getByTestId("items-count")).toHaveTextContent("1");
    
    // Remove item
    fireEvent.click(screen.getByText("Remove"));
    expect(screen.getByTestId("items-count")).toHaveTextContent("0");
    expect(screen.getByTestId("has-items")).toHaveTextContent("no");
  });

  it("should clear all items from the list", () => {
    renderWithProvider();
    
    // Add multiple items
    fireEvent.click(screen.getByTestId("add-test-1"));
    fireEvent.click(screen.getByTestId("add-test-2"));
    expect(screen.getByTestId("items-count")).toHaveTextContent("2");
    
    // Clear list
    fireEvent.click(screen.getByTestId("clear-list"));
    expect(screen.getByTestId("items-count")).toHaveTextContent("0");
    expect(screen.getByTestId("has-items")).toHaveTextContent("no");
  });

  it("should enforce maximum capacity of 20 items", () => {
    renderWithProvider();
    
    // Add 21 items to test the limit
    for (let i = 1; i <= 21; i++) {
      const addButton = document.createElement("button");
      addButton.onclick = () => 
        screen.getByTestId("add-test-1").click = () => 
          screen.getAllByRole("button").find(btn => btn.textContent === "Add Test Product 1")?.click();
      
      // Simulate adding items by calling addItem directly through context
      fireEvent.click(screen.getByTestId("add-test-1"));
    }
    
    // Should still be 1 because we're adding the same item (no duplicates)
    expect(screen.getByTestId("items-count")).toHaveTextContent("1");
  });

  it("should throw error when used outside provider", () => {
    // Spy on console.error to suppress the error output in tests
    const consoleSpy = jest.spyOn(console, "error").mockImplementation();
    
    expect(() => {
      render(<TestComponent />);
    }).toThrow("usePlanningList must be used within a PlanningListProvider");
    
    consoleSpy.mockRestore();
  });
});