import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import TransferList from "../TransferList";

jest.mock("../../../hooks/useMediaQuery", () => ({
  useIsMobile: jest.fn(() => false),
}));

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

describe("TransferList — mobile", () => {
  const { useIsMobile } = require("../../../hooks/useMediaQuery");

  beforeEach(() => {
    useIsMobile.mockReturnValue(true);
  });

  afterEach(() => {
    useIsMobile.mockReturnValue(false);
  });

  it("shows available and assigned tabs with counts", () => {
    render(
      <TransferList
        available={items}
        assignedIds={["b"]}
        onChange={jest.fn()}
        labels={{ available: "Available", assigned: "Assigned" }}
      />
    );
    expect(
      screen.getByRole("tab", { name: /available \(2\)/i })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("tab", { name: /assigned \(1\)/i })
    ).toBeInTheDocument();
  });

  it("shows only the available side by default and hides assigned items", () => {
    render(
      <TransferList available={items} assignedIds={["b"]} onChange={jest.fn()} />
    );
    expect(screen.getByText("Item A")).toBeInTheDocument();
    expect(screen.getByText("Item C")).toBeInTheDocument();
    expect(screen.queryByText("Item B")).not.toBeInTheDocument();
  });

  it("switches to the assigned tab and shows assigned items", () => {
    render(
      <TransferList available={items} assignedIds={["b"]} onChange={jest.fn()} />
    );
    fireEvent.click(screen.getByRole("tab", { name: /assigned/i }));
    expect(screen.getByText("Item B")).toBeInTheDocument();
    expect(screen.queryByText("Item A")).not.toBeInTheDocument();
  });

  it("still assigns via the + button on mobile", () => {
    const onChange = jest.fn();
    render(
      <TransferList available={items} assignedIds={["b"]} onChange={onChange} />
    );
    fireEvent.click(screen.getByRole("button", { name: /assign item a/i }));
    expect(onChange).toHaveBeenCalledWith(["b", "a"]);
  });

  it("search filters the active tab", () => {
    render(
      <TransferList
        available={items}
        assignedIds={[]}
        onChange={jest.fn()}
        searchable
        searchPlaceholder="Search…"
      />
    );
    fireEvent.change(screen.getByLabelText("Search…"), {
      target: { value: "Item A" },
    });
    expect(screen.getByText("Item A")).toBeInTheDocument();
    expect(screen.queryByText("Item C")).not.toBeInTheDocument();
  });
});
