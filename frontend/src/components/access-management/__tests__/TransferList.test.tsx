import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import TransferList from "../TransferList";

const items = [
  { id: "a", label: "Item A" },
  { id: "b", label: "Item B" },
  { id: "c", label: "Item C" },
];

describe("TransferList", () => {
  it("renders unassigned items in available column and assigned items in assigned column", () => {
    render(
      <TransferList
        available={items}
        assignedIds={["b"]}
        onChange={jest.fn()}
        labels={{ available: "Available", assigned: "Assigned" }}
      />
    );
    expect(screen.getByText("Item A")).toBeInTheDocument();
    expect(screen.getByText("Item C")).toBeInTheDocument();
    // Item B is in the assigned column — appears once only
    expect(screen.getAllByText("Item B")).toHaveLength(1);
  });

  it("clicking the assign (+) button moves an item to assigned and calls onChange", () => {
    const onChange = jest.fn();
    render(
      <TransferList
        available={items}
        assignedIds={["b"]}
        onChange={onChange}
        labels={{ available: "Available", assigned: "Assigned" }}
      />
    );
    fireEvent.click(screen.getByRole("button", { name: /assign item a/i }));
    expect(onChange).toHaveBeenCalledWith(["b", "a"]);
  });

  it("clicking the remove (−) button moves an item to available and calls onChange", () => {
    const onChange = jest.fn();
    render(
      <TransferList
        available={items}
        assignedIds={["b"]}
        onChange={onChange}
        labels={{ available: "Available", assigned: "Assigned" }}
      />
    );
    fireEvent.click(screen.getByRole("button", { name: /remove item b/i }));
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("renders section headers in the available column when groupBy is provided", () => {
    const groupedItems = [
      { id: "a", label: "Item A" },
      { id: "b", label: "Item B" },
    ];
    render(
      <TransferList
        available={groupedItems}
        assignedIds={[]}
        onChange={jest.fn()}
        groupBy={(item) => (item.id === "a" ? "Section One" : "Section Two")}
        labels={{ available: "Available", assigned: "Assigned" }}
      />
    );
    expect(screen.getByText("Section One")).toBeInTheDocument();
    expect(screen.getByText("Section Two")).toBeInTheDocument();
  });

  it("shows sublabel text when provided", () => {
    const sublabelItem = [{ id: "x", label: "Item X", sublabel: "hint text" }];
    render(
      <TransferList
        available={sublabelItem}
        assignedIds={[]}
        onChange={jest.fn()}
      />
    );
    expect(screen.getByText("hint text")).toBeInTheDocument();
  });
});
